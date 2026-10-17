using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LiteDeck;

/// <summary>Painel em formato de popover, ancorado na bandeja (sem moldura de janela).</summary>
sealed class MainForm : Form, IMessageFilter
{
    const int W = 348, X = 12, ViewTop = 98, ViewH = 494;

    readonly Hw? _hw;
    readonly Settings _s = Settings.Load();
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer _kbDebounce = new() { Interval = 120 };
    readonly System.Windows.Forms.Timer _fanDebounce = new() { Interval = 250 };

    // cabeçalho / rodapé
    readonly IconButton _powerBtn, _menuBtn;
    readonly Label _statusTxt, _dutyTxt;
    readonly Panel _dot;
    bool _connected = true;

    readonly TabBar _tabs;
    readonly Panel[] _views = new Panel[4];
    readonly Panel[] _contents = new Panel[4];

    // Início
    readonly LaptopView _laptop;
    readonly Label _nowTxt, _modeTag, _brightBig;
    readonly Slider _bright;
    readonly Toggle _boost;
    readonly Label _cpuVal, _gpuVal, _cpuSub, _gpuSub;

    // Teclado
    readonly Panel _swatch;
    readonly Slider _r, _g, _b, _hue, _bright2;
    readonly Label _hex, _brightLbl2;
    readonly Toggle _kbOn;

    // Geral
    readonly KeyBlocker _keys = new();
    readonly Toggle _winLock, _numLock, _fnLock, _usbCharge, _acOn, _osd, _wifi, _bt, _cam, _touch, _optimus;
    Label _optimusNote = null!;
    bool? _optimusAtBoot;
    readonly Panel _hzRow;
    bool _syncGen;

    // Ventoinhas
    readonly List<Tile[]> _tileSets = new();
    readonly Slider _fixed;
    readonly Label _fixedLbl, _fan1Txt, _fan2Txt;

    bool _loading = true, _reallyExit, _syncHue, _syncBright, _modal;
    DateTime _lastHide;

    static readonly string[] FanNames = { "Padrão", "Silencioso", "Turbo", "Fixo" };
    static readonly Color[] Presets =
    {
        Color.White, Color.FromArgb(255, 0, 0), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 220, 0),
        Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 220), Color.FromArgb(0, 90, 255), Color.FromArgb(180, 0, 255), Color.FromArgb(255, 0, 140),
    };

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int val, int size);

    public MainForm(Hw? hw, bool startHidden)
    {
        _hw = hw;
        Text = "LiteDeck";
        Icon = MakeIcon();
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(372, 620);
        ShowInTaskbar = false; TopMost = true; DoubleBuffered = true; KeyPreview = true;

        if (_s.IsNew)
        {
            // Primeiro uso: assume a última cor/brilho que o teclado estava usando e liga o início automático.
            try
            {
                var k = _hw!.ReadKeyboardState();
                var c = k.Color.GetBrightness() < 0.02f ? Color.White : k.Color;
                (_s.KbR, _s.KbG, _s.KbB) = (c.R, c.G, c.B);
                _s.KbLevel = k.Brightness > 0 ? k.Brightness : 3;
            }
            catch { }
            if (_hw != null) SetAutostart(true);
            _s.Save();
        }

        // ---------- Cabeçalho ----------
        Controls.Add(new PictureBox { Left = X, Top = 13, Size = new Size(28, 28), Image = MakeBrand(), BackColor = Color.Transparent });
        Controls.Add(At(Theme.Label(MachineName(), Theme.Title), X + 36, 11));
        Controls.Add(At(Theme.Label("LiteDeck  ·  chassi Uniwill", Theme.Small, Theme.Muted), X + 36, 28));
        _menuBtn = new IconButton(Icons.More) { Left = X + W - 62, Top = 12, Size = new Size(28, 28) }; Controls.Add(_menuBtn);
        _powerBtn = new IconButton(Icons.Power) { Left = X + W - 30, Top = 12, Size = new Size(28, 28) }; Controls.Add(_powerBtn);

        _tabs = new TabBar(Icons.Home, Icons.Keyboard, "fan", Icons.Settings) { Left = X, Top = 52, Width = W };
        Controls.Add(_tabs);

        for (int i = 0; i < 4; i++)
        {
            _views[i] = new Panel { Left = 0, Top = ViewTop, Size = new Size(372, ViewH), BackColor = Theme.Bg, Visible = i == 0 };
            _contents[i] = new Panel { Left = 0, Top = 0, Width = 372, Height = ViewH, BackColor = Theme.Bg };
            _views[i].Controls.Add(_contents[i]);
            Controls.Add(_views[i]);
        }

        // ---------- Rodapé ----------
        var line = new Panel { Left = 0, Top = 596, Size = new Size(372, 1), BackColor = Theme.Border }; Controls.Add(line);
        _dot = new Panel { Left = X + 2, Top = 605, Size = new Size(7, 7), BackColor = Theme.Bg };
        _dot.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(_connected ? Color.FromArgb(70, 200, 110) : Color.FromArgb(220, 70, 70)); e.Graphics.FillEllipse(b, 0, 0, 6, 6); };
        Controls.Add(_dot);
        _statusTxt = Theme.Label("Conectado", Theme.Small, Theme.Muted); Controls.Add(At(_statusTxt, X + 13, 601));
        _dutyTxt = Theme.Label("", Theme.Caps, Theme.Muted); _dutyTxt.Top = 603; Controls.Add(_dutyTxt);

        // ========== Início ==========
        var home = _contents[0];
        var hero = Card(X, 0, W, 196); home.Controls.Add(hero);
        _laptop = new LaptopView { Left = 6, Top = 6, Size = new Size(W - 12, 184) }; hero.Controls.Add(_laptop);

        home.Controls.Add(At(Theme.Label(Icons.Dot, Theme.Caps, Theme.Accent), X + 1, 205));
        home.Controls.Add(At(Theme.Label("AGORA NO NOTEBOOK", Theme.Caps, Theme.Muted), X + 11, 205));
        _modeTag = Theme.Label("", Theme.Caps, Theme.Accent); _modeTag.Top = 205; home.Controls.Add(_modeTag);
        _nowTxt = Theme.Label("", Theme.Body); home.Controls.Add(At(_nowTxt, X + 1, 220));

        var br = Card(X, 246, W, 96); home.Controls.Add(br);
        br.Controls.Add(Glyph(Icons.Brightness, 13, 13, Theme.Muted));
        br.Controls.Add(At(Theme.Label("Brilho do teclado", Theme.Body), 34, 11));
        _brightBig = Theme.Label("0", Theme.Big); _brightBig.Top = 3; br.Controls.Add(_brightBig);
        var minus = new RoundBtn(Icons.Minus) { Left = 12, Top = 40, Size = new Size(28, 28) };
        var plus = new RoundBtn(Icons.Plus) { Left = W - 40, Top = 40, Size = new Size(28, 28) };
        _bright = new Slider { Left = 44, Top = 41, Width = W - 88, Maximum = Hw.BrightLevels };
        br.Controls.AddRange(new Control[] { minus, plus, _bright });
        br.Controls.Add(At(Theme.Label("Teclado retroiluminado", Theme.Small, Theme.Muted), 14, 74));
        minus.Click += (_, _) => { _kbOn.Checked = true; _bright.Value -= 1; };
        plus.Click += (_, _) => { _kbOn.Checked = true; _bright.Value += 1; };

        var fv = Card(X, 350, W, 132); home.Controls.Add(fv);
        fv.Controls.Add(Glyph(Icons.Gauge, 13, 13, Theme.Muted));
        fv.Controls.Add(At(Theme.Label("Perfis de ventoinha", Theme.Body), 34, 11));
        var all = Theme.Label("Ver todos", Theme.Small, Theme.Muted); all.Cursor = Cursors.Hand; all.Click += (_, _) => _tabs.Selected = 2;
        all.Left = W - 14 - all.PreferredWidth; all.Top = 13; fv.Controls.Add(all);
        AddTiles(fv, 40);

        var sn = Card(X, 490, W, 124); home.Controls.Add(sn);
        sn.Controls.Add(Glyph(Icons.Chip, 13, 13, Theme.Muted));
        sn.Controls.Add(At(Theme.Label("Sensores", Theme.Body), 34, 11));
        (_cpuSub, _cpuVal) = ListRow(sn, 40, Icons.Chip, "Processador");
        (_gpuSub, _gpuVal) = ListRow(sn, 80, Icons.Display, "Placa de vídeo");

        var bo = Card(X, 622, W, 54); home.Controls.Add(bo);
        bo.Controls.Add(new IconTile(Icons.Bolt, Theme.Accent) { Left = 12, Top = 12 });
        bo.Controls.Add(At(Theme.Label("Fan Boost", Theme.Body), 52, 9));
        bo.Controls.Add(At(Theme.Label("Ventoinhas no máximo", Theme.Small, Theme.Muted), 52, 27));
        _boost = new Toggle { Left = W - 54, Top = 15 }; bo.Controls.Add(_boost);
        home.Height = 684;

        // ========== Teclado ==========
        var kc = _contents[1];
        var kb = Card(X, 0, W, 404); kc.Controls.Add(kb);
        kb.Controls.Add(Glyph(Icons.Keyboard, 13, 13, Theme.Muted));
        kb.Controls.Add(At(Theme.Label("Teclado RGB", Theme.Body), 34, 11));
        _kbOn = new Toggle { Left = W - 54, Top = 10 }; kb.Controls.Add(_kbOn);
        _swatch = new Panel { Left = 14, Top = 46, Size = new Size(48, 48), BackColor = Theme.Card };
        _swatch.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(Current); e.Graphics.FillEllipse(b, 2, 2, 43, 43); using var p = new Pen(Theme.Border, 2); e.Graphics.DrawEllipse(p, 2, 2, 43, 43); };
        kb.Controls.Add(_swatch);
        _hex = Theme.Label("#FFFFFF", Theme.Title); kb.Controls.Add(At(_hex, 72, 46));
        var more = new Chip("Escolher cor…") { Left = 72, Top = 68, Size = new Size(104, 26), Font = Theme.Small };
        more.Click += (_, _) => PickColor(); kb.Controls.Add(more);
        for (int i = 0; i < Presets.Length; i++)
        {
            var c = Presets[i];
            var sw = new Panel { Left = 14 + i * 35, Top = 110, Size = new Size(26, 26), BackColor = Theme.Card, Cursor = Cursors.Hand };
            sw.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(c); e.Graphics.FillEllipse(b, 1, 1, 23, 23); using var p = new Pen(Theme.Border); e.Graphics.DrawEllipse(p, 1, 1, 23, 23); };
            sw.Click += (_, _) => { _kbOn.Checked = true; SetColor(c); };
            kb.Controls.Add(sw);
        }
        kb.Controls.Add(At(Theme.Label("MATIZ", Theme.Caps, Theme.Muted), 16, 154));
        _hue = new Slider { Left = 6, Top = 170, Width = W - 12, Minimum = 0, Maximum = 359, Rainbow = true }; kb.Controls.Add(_hue);
        kb.Controls.Add(At(Theme.Label("CANAIS RGB", Theme.Caps, Theme.Muted), 16, 206));
        _r = ColorSlider(kb, "R", 224, Color.FromArgb(232, 80, 80));
        _g = ColorSlider(kb, "G", 254, Color.FromArgb(80, 196, 110));
        _b = ColorSlider(kb, "B", 284, Color.FromArgb(80, 140, 236));
        kb.Controls.Add(At(Theme.Label("BRILHO", Theme.Caps, Theme.Muted), 16, 326));
        _brightLbl2 = Theme.Label("0%", Theme.Small, Theme.Muted); kb.Controls.Add(At(_brightLbl2, W - 42, 324));
        _bright2 = new Slider { Left = 6, Top = 344, Width = W - 12, Maximum = Hw.BrightLevels }; kb.Controls.Add(_bright2);
        kb.Controls.Add(At(Theme.Label("Clique em uma cor para ligar a luz nela.", Theme.Small, Theme.Muted), 16, 378));
        kc.Height = 412;

        // ========== Ventoinhas ==========
        var fc = _contents[2];
        var fan = Card(X, 0, W, 236); fc.Controls.Add(fan);
        fan.Controls.Add(Glyph(Icons.Gauge, 13, 13, Theme.Muted));
        fan.Controls.Add(At(Theme.Label("Perfil", Theme.Body), 34, 11));
        AddTiles(fan, 40);
        fan.Controls.Add(At(Theme.Label("VELOCIDADE FIXA", Theme.Caps, Theme.Muted), 16, 140));
        _fixedLbl = Theme.Label("60%", Theme.Small, Theme.Muted); fan.Controls.Add(At(_fixedLbl, W - 42, 138));
        _fixed = new Slider { Left = 6, Top = 158, Width = W - 12, Minimum = 20, Maximum = 100 }; fan.Controls.Add(_fixed);
        fan.Controls.Add(At(Theme.Label("Usada no perfil Fixo, com rampa curta.", Theme.Small, Theme.Muted), 16, 196));
        fan.Controls.Add(At(Theme.Label("Silencioso −20% e Turbo +15% sobre a curva padrão.", Theme.Small, Theme.Muted), 16, 212));
        var rp = Card(X, 244, W, 124); fc.Controls.Add(rp);
        rp.Controls.Add(Glyph(Icons.Chip, 13, 13, Theme.Muted));
        rp.Controls.Add(At(Theme.Label("Rotação", Theme.Body), 34, 11));
        Label s1, s2;
        (s1, _fan1Txt) = ListRow(rp, 40, "fan", "Ventoinha 1"); s1.Text = "Processador";
        (s2, _fan2Txt) = ListRow(rp, 80, "fan", "Ventoinha 2"); s2.Text = "Placa de vídeo";
        fc.Height = 376;

        // ========== Geral ==========
        var gc = _contents[3];
        var qs = Card(X, 0, W, 40 + 6 * 50 + 6); gc.Controls.Add(qs);
        qs.Controls.Add(Glyph(Icons.Settings, 13, 13, Theme.Muted));
        qs.Controls.Add(At(Theme.Label("Troca rápida", Theme.Body), 34, 11));
        _winLock = SwitchRow(qs, 40, "Bloquear tecla Windows", "Bloqueio no firmware, vale com o app fechado.");
        _numLock = SwitchRow(qs, 90, "Bloquear teclado numérico", "Desativa o bloco numérico enquanto o app roda.");
        _fnLock = SwitchRow(qs, 140, "Bloquear tecla Fn", "F1–F12 como função principal, sem segurar Fn.");
        _usbCharge = SwitchRow(qs, 190, "Carregar USB desligado", "Mantém energia nas USB desligado ou hibernando.");
        _acOn = SwitchRow(qs, 240, "Ligar ao conectar a fonte", "Liga o notebook sozinho quando a fonte é plugada.");
        _osd = SwitchRow(qs, 290, "Ocultar OSD do fabricante", "Esconde os avisos de tela das teclas Fn.");

        var dv = Card(X, qs.Bottom + 8, W, 40 + 4 * 50 + 6); gc.Controls.Add(dv);
        dv.Controls.Add(Glyph(Icons.Display, 13, 13, Theme.Muted));
        dv.Controls.Add(At(Theme.Label("Dispositivos Windows", Theme.Body), 34, 11));
        _wifi = SwitchRow(dv, 40, "Wi-Fi", "Liga ou desliga o rádio Wi-Fi.");
        _bt = SwitchRow(dv, 90, "Bluetooth", "Ativa ou desativa o adaptador Bluetooth.");
        _cam = SwitchRow(dv, 140, "Webcam", "Ativa ou desativa a câmera integrada.");
        _touch = SwitchRow(dv, 190, "Touchpad", "Desliga o touchpad direto no controlador.");

        var tl = Card(X, dv.Bottom + 8, W, 96); gc.Controls.Add(tl);
        tl.Controls.Add(Glyph(Icons.Display, 13, 13, Theme.Muted));
        tl.Controls.Add(At(Theme.Label("Taxa de atualização da tela", Theme.Body), 34, 11));
        _hzRow = new Panel { Left = 12, Top = 42, Size = new Size(W - 24, 34), BackColor = Theme.Card }; tl.Controls.Add(_hzRow);
        tl.Controls.Add(At(Theme.Label("Aplica na tela principal, na resolução atual.", Theme.Small, Theme.Muted), 14, 78));
        _optimus = SwitchRow(tl, 104, "Desativar NVIDIA Optimus", "dGPU direto na tela; aplica após reiniciar.");
        _optimusNote = Theme.Label("", Theme.Small, Theme.Accent); tl.Controls.Add(At(_optimusNote, 14, 140));
        tl.Height = 162;
        gc.Height = tl.Bottom + 8;

        _tabs.SelectedChanged += (_, _) => { for (int i = 0; i < 4; i++) { _views[i].Visible = i == _tabs.Selected; _contents[i].Top = 0; } if (_tabs.Selected == 3) RefreshGeneral(); };

        LoadUi();
        WireEvents();
        WireGeneral();
        _keys.BlockNumpad = _s.NumpadLock;
        _loading = false;
        RefreshAll();

        ApplyAll();
        _poll.Tick += (_, _) => Poll();
        _poll.Start(); Poll();

        BuildTray();
        Application.AddMessageFilter(this);
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) BeginInvoke(ApplyAll); };

        if (startHidden) { Opacity = 0; Load += (_, _) => { Hide(); Opacity = 1; }; }
        else PlaceNearTray();
    }

    // ---------- helpers de UI ----------
    static T At<T>(T c, int x, int y) where T : Control { c.Left = x; c.Top = y; return c; }
    static RoundedPanel Card(int x, int y, int w, int h) => new() { Left = x, Top = y, Size = new Size(w, h) };
    static Label Glyph(string g, int x, int y, Color c) { var l = Theme.Label(g, Icons.Font(11), c); l.Left = x; l.Top = y; return l; }

    /// <summary>Linha de lista no estilo "Entradas": ícone, título, subtítulo e valor à direita.</summary>
    static (Label sub, Label value) ListRow(Panel p, int y, string glyph, string title)
    {
        p.Controls.Add(new IconTile(glyph) { Left = 12, Top = y });
        p.Controls.Add(At(Theme.Label(title, Theme.Body), 52, y - 1));
        var sub = Theme.Label("--", Theme.Small, Theme.Muted); p.Controls.Add(At(sub, 52, y + 16));
        var val = Theme.Label("--", Theme.Title); val.Top = y + 5; val.Left = p.Width - 70; p.Controls.Add(val);
        return (sub, val);
    }

    static void RightAlign(Label l, int right) => l.Left = right - l.PreferredWidth;

    Slider ColorSlider(Panel parent, string name, int y, Color accent)
    {
        parent.Controls.Add(At(Theme.Label(name, Theme.Small, Theme.Muted), 16, y + 5));
        var s = new Slider { Left = 32, Top = y, Width = parent.Width - 40, Minimum = 0, Maximum = 255, Accent = accent };
        parent.Controls.Add(s);
        return s;
    }

    /// <summary>Linha com título, descrição e interruptor (estilo "Troca Rápida").</summary>
    static Toggle SwitchRow(Panel p, int y, string title, string desc)
    {
        p.Controls.Add(At(Theme.Label(title, Theme.Body), 14, y));
        p.Controls.Add(At(Theme.Label(desc, Theme.Small, Theme.Muted), 14, y + 18));
        var t = new Toggle { Left = p.Width - 54, Top = y + 6 }; p.Controls.Add(t);
        return t;
    }

    void AddTiles(Panel parent, int top)
    {
        string[] glyphs = { "fan", Icons.Moon, Icons.Bolt, Icons.Sliders };
        var set = new Tile[4];
        int gap = (W - 28 - 4 * 72) / 3;
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            set[i] = new Tile(glyphs[i], FanNames[i]) { Left = 14 + i * (72 + gap), Top = top };
            set[i].Click += (_, _) => { _s.Fan = (FanMode)idx; UpdateFanUi(); QueueFan(); RefreshAll(); };
            parent.Controls.Add(set[i]);
        }
        _tileSets.Add(set);
    }

    static Bitmap MakeBrand()
    {
        var bmp = new Bitmap(28, 28);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 0, 0, 27, 27);
        Icons.DrawFan(g, new RectangleF(6, 6, 15, 15), Color.White, Theme.Accent);
        return bmp;
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Theme.Accent); g.FillEllipse(bg, 1, 1, 62, 62);
            Icons.DrawFan(g, new RectangleF(14, 14, 36, 36), Color.White, Theme.Accent);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>Fabricante e modelo lidos do BIOS em tempo de execução.</summary>
    static string MachineName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var name = $"{k?.GetValue("SystemManufacturer")} {k?.GetValue("SystemProductName")}".Trim();
            return name.Length > 0 ? name : Environment.MachineName;
        }
        catch { return Environment.MachineName; }
    }

    static Color FromHsv(double h, double s, double v) => Hsv.ToColor((float)h, (float)s, (float)v);
    static Color TempColor(int t) => t < 65 ? Color.FromArgb(90, 205, 125) : t < 80 ? Color.FromArgb(240, 180, 60) : Color.FromArgb(236, 84, 74);

    // ---------- estado ----------
    Color Current => Color.FromArgb(_r.Value, _g.Value, _b.Value);

    void LoadUi()
    {
        _r.Value = _s.KbR; _g.Value = _s.KbG; _b.Value = _s.KbB; _hue.Value = (int)Current.GetHue() % 360;
        _bright.Value = _s.KbLevel; _bright2.Value = _s.KbLevel; _fixed.Value = _s.FanFixed;
        _kbOn.Checked = _s.KbApply; _boost.Checked = _s.FanBoost; _powerBtn.Active = _s.KbApply;
        UpdateFanUi();
    }

    /// <summary>Atualiza textos e o desenho do notebook a partir do estado atual.</summary>
    void RefreshAll()
    {
        _swatch.Invalidate();
        _hex.Text = $"#{Current.R:X2}{Current.G:X2}{Current.B:X2}";
        _brightBig.Text = $"{_bright.Value}/{Hw.BrightLevels}"; RightAlign(_brightBig, W - 12);
        _brightLbl2.Text = $"Nível {_bright2.Value}"; _fixedLbl.Text = _fixed.Value + "%";
        _laptop.SetKeyboard(Current, _kbOn.Checked ? _bright.Value / (float)Hw.BrightLevels : 0f);
        _powerBtn.Active = _kbOn.Checked;
        string fan = _s.Fan == FanMode.Fixed ? $"FIXO {_s.FanFixed}%" : FanNames[(int)_s.Fan].ToUpperInvariant();
        _modeTag.Text = _boost.Checked ? "FAN BOOST" : fan; RightAlign(_modeTag, X + W - 1);
        _nowTxt.Text = _kbOn.Checked ? _bright.Value == 0 ? "Luz do teclado no nível 0 (apagada)." : $"Teclado {_hex.Text}, brilho nível {_bright.Value} de {Hw.BrightLevels}." : "Luz do teclado apagada.";
    }

    void UpdateFanUi()
    {
        foreach (var set in _tileSets) for (int i = 0; i < set.Length; i++) set[i].Selected = (int)_s.Fan == i;
        _fixed.Enabled = _s.Fan == FanMode.Fixed;
    }

    void WireEvents()
    {
        void Col(object? _, EventArgs __) { if (_loading) return; RefreshAll(); _kbDebounce.Stop(); _kbDebounce.Start(); }
        _r.ValueChanged += Col; _g.ValueChanged += Col; _b.ValueChanged += Col; _kbOn.Changed += Col;
        _powerBtn.Click += (_, _) => _kbOn.Checked = !_kbOn.Checked;

        // brilho: dois sliders sincronizados
        _bright.ValueChanged += (_, _) => { if (_syncBright) return; _syncBright = true; _bright2.Value = _bright.Value; _syncBright = false; Col(null, EventArgs.Empty); };
        _bright2.ValueChanged += (_, _) => { if (_syncBright) return; _syncBright = true; _bright.Value = _bright2.Value; _syncBright = false; Col(null, EventArgs.Empty); };

        _hue.ValueChanged += (_, _) =>
        {
            if (_loading || _syncHue) return;
            _syncHue = true; SetColor(FromHsv(_hue.Value, 1, 1)); _syncHue = false;
        };
        EventHandler syncHue = (_, _) => { if (_syncHue) return; _syncHue = true; _hue.Value = (int)Current.GetHue() % 360; _syncHue = false; };
        _r.ValueChanged += syncHue; _g.ValueChanged += syncHue; _b.ValueChanged += syncHue;
        _kbDebounce.Tick += (_, _) => { _kbDebounce.Stop(); SaveKb(); ApplyKeyboard(); };

        _fixed.ValueChanged += (_, _) => { if (_loading) return; _s.FanFixed = _fixed.Value; RefreshAll(); QueueFan(); };
        _fanDebounce.Tick += (_, _) => { _fanDebounce.Stop(); _s.Save(); Safe(ApplyFans); };

        _boost.Changed += (_, _) => { SetBoost(_boost.Checked); RefreshAll(); };

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        var auto = new ToolStripMenuItem("Iniciar com o Windows") { Checked = IsAutostart(), CheckOnClick = true };
        auto.CheckedChanged += (_, _) => SetAutostart(auto.Checked);
        menu.Items.Add(auto);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair do LiteDeck", null, (_, _) => ExitApp());
        _menuBtn.Click += (_, _) => { _modal = true; menu.Show(_menuBtn, new Point(0, _menuBtn.Height)); };
        menu.Closed += (_, _) => _modal = false;

        Deactivate += (_, _) => { if (!_modal && Visible) { Hide(); _lastHide = DateTime.Now; } };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        FormClosing += (_, e) => { if (!_reallyExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    // ---------- Geral ----------
    void WireGeneral()
    {
        _winLock.Changed += (_, _) => { if (_syncGen) return; _s.WinKeyLock = _winLock.Checked; _s.Save(); RunGeneral(() => _hw!.SetWinKeyLock(_winLock.Checked)); };
        _fnLock.Changed += (_, _) => { if (_syncGen) return; _s.FnLock = _fnLock.Checked; _s.Save(); RunGeneral(() => _hw!.FnLock = _fnLock.Checked); };
        _usbCharge.Changed += (_, _) => { if (_syncGen) return; _s.UsbCharging = _usbCharge.Checked; _s.Save(); RunGeneral(() => _hw!.UsbCharging = _usbCharge.Checked); };
        _acOn.Changed += (_, _) => { if (_syncGen) return; _s.AcPowerOn = _acOn.Checked; _s.Save(); RunGeneral(() => _hw!.AcPowerOn = _acOn.Checked); };
        _numLock.Changed += (_, _) => { if (_syncGen) return; _s.NumpadLock = _keys.BlockNumpad = _numLock.Checked; _s.Save(); };
        _osd.Changed += (_, _) => { if (_syncGen) return; Safe(() => General.OsdHidden = _osd.Checked); };
        _wifi.Changed += (_, _) => { if (!_syncGen) RunGeneral(() => General.SetWifi(_wifi.Checked)); };
        _bt.Changed += (_, _) => { if (!_syncGen) RunGeneral(() => General.SetDevice(General.Device.Bluetooth, _bt.Checked)); };
        _cam.Changed += (_, _) => { if (!_syncGen) RunGeneral(() => General.SetDevice(General.Device.Webcam, _cam.Checked)); };
        _touch.Changed += (_, _) => { if (!_syncGen) RunGeneral(() => _hw!.TouchpadOn = _touch.Checked); };
        _optimus.Changed += (_, _) => { if (!_syncGen) RunGeneral(() => Uefi.OptimusDisabled = _optimus.Checked); };
    }

    /// <summary>Executa a ação fora da thread da UI (desativar dispositivos leva ~1 s) e depois relê o estado.</summary>
    void RunGeneral(Action a)
    {
        Task.Run(() => { try { a(); } catch (Exception ex) { BeginInvoke(() => Safe(() => throw ex)); } })
            .ContinueWith(_ => RefreshGeneral());
    }

    void RefreshGeneral()
    {
        Task.Run(() =>
        {
            bool? wifi = null, bt = null, cam = null, touch = null; bool osd = false; int[] hz = Array.Empty<int>(); int cur = 0;
            try { wifi = General.WifiOn(); } catch { }
            try { bt = General.DeviceOn(General.Device.Bluetooth); } catch { }
            try { cam = General.DeviceOn(General.Device.Webcam); } catch { }
            bool? touchEc = null, win = null, fn = null, usb = null, ac = null;
            try { touchEc = _hw?.TouchpadOn; win = _hw?.WinKeyLocked; fn = _hw?.FnLock; usb = _hw?.UsbCharging; ac = _hw?.AcPowerOn; } catch { }
            touch = touchEc;
            bool? optimus = null; try { optimus = Uefi.OptimusDisabled; } catch { }
            try { osd = General.OsdHidden; } catch { }
            try { hz = General.AvailableHz(); cur = General.CurrentHz; } catch { }
            if (!IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _syncGen = true;
                void Set(Toggle t, bool? v) { t.Enabled = v != null; t.Checked = v ?? false; }
                Set(_wifi, wifi); Set(_bt, bt); Set(_cam, cam); Set(_touch, touch);
                _osd.Checked = osd; _numLock.Checked = _s.NumpadLock;
                Set(_winLock, win); Set(_fnLock, fn); Set(_usbCharge, usb); Set(_acOn, ac); Set(_optimus, optimus);
                _optimusAtBoot ??= optimus;
                _optimusNote.Text = optimus != null && optimus != _optimusAtBoot ? "Reinicie para que a mudança tenha efeito." : "";
                _syncGen = false;
                BuildHzChips(hz, cur);
            });
        });
    }

    void BuildHzChips(int[] hz, int cur)
    {
        _hzRow.Controls.Clear();
        var list = hz.Length > 5 ? hz.Skip(hz.Length - 5).ToArray() : hz;
        int w = list.Length == 0 ? 0 : (_hzRow.Width - (list.Length - 1) * 6) / list.Length;
        for (int i = 0; i < list.Length; i++)
        {
            int v = list[i];
            var c = new Chip($"{v} Hz") { Left = i * (w + 6), Top = 0, Size = new Size(w, 32), Selected = v == cur, Font = Theme.Small };
            c.Click += (_, _) => RunGeneral(() => General.SetHz(v));
            _hzRow.Controls.Add(c);
        }
    }

    void SetBoost(bool on)
    {
        if (_loading) return;
        _s.FanBoost = on; _s.Save();
        Safe(() => _hw!.SetFanBoost(on));
    }

    void SaveKb() { _s.KbR = _r.Value; _s.KbG = _g.Value; _s.KbB = _b.Value; _s.KbLevel = _bright.Value; _s.KbApply = _kbOn.Checked; _s.Save(); }
    void QueueFan() { _fanDebounce.Stop(); _fanDebounce.Start(); }
    void SetColor(Color c) { _r.Value = c.R; _g.Value = c.G; _b.Value = c.B; }

    void PickColor()
    {
        _modal = true;
        try
        {
            using var dlg = new ColorDialog { Color = Current, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) { _kbOn.Checked = true; SetColor(dlg.Color); }
        }
        finally { _modal = false; Activate(); }
    }

    void ExitApp() { _keys.Dispose(); _reallyExit = true; _tray.Visible = false; Application.Exit(); }

    /// <summary>Usado pelo modo --shot para capturas da interface.</summary>
    public void ShowTab(int i) => _tabs.Selected = i;
    public Control PageContent(int i) => _contents[i];

    // ---------- rolagem pela roda do mouse ----------
    public bool PreFilterMessage(ref Message m)
    {
        const int WM_MOUSEWHEEL = 0x020A;
        if (m.Msg != WM_MOUSEWHEEL || !Visible) return false;
        int i = _tabs.Selected; var view = _views[i]; var content = _contents[i];
        if (!view.RectangleToScreen(view.ClientRectangle).Contains(Cursor.Position)) return false;
        int delta = (short)((long)m.WParam >> 16);
        int min = Math.Min(0, view.Height - content.Height - 8);
        content.Top = Math.Clamp(content.Top + delta / 120 * 56, min, 0);
        return true;
    }

    // ---------- hardware ----------
    void ApplyKeyboard() => Safe(() => { _hw!.SetKeyboardColor(Current); _hw.SetKeyboardLevel(_kbOn.Checked ? _bright.Value : 0); });
    void ApplyFans() => _hw!.SetFanMode(_s.Fan, _s.FanFixed);

    void ApplyAll()
    {
        if (_hw == null) return;
        ApplyKeyboard();
        Safe(() => { if (_s.Fan != FanMode.Default) ApplyFans(); _hw.SetFanBoost(_s.FanBoost); });
        Safe(() =>
        {
            if (_s.WinKeyLock is bool w) _hw.SetWinKeyLock(w);
            if (_s.FnLock is bool f) _hw.FnLock = f;
            if (_s.UsbCharging is bool u) _hw.UsbCharging = u;
            if (_s.AcPowerOn is bool a) _hw.AcPowerOn = a;
        });
    }

    void Safe(Action a)
    {
        try { a(); } catch (Exception ex) { if (_hw != null) _tray.ShowBalloonTip(3000, "LiteDeck", ex.Message, ToolTipIcon.Warning); }
    }

    void Poll()
    {
        try
        {
            var s = _hw!.ReadStatus();
            _cpuVal.Text = $"{s.CpuTemp} °C"; _cpuVal.ForeColor = TempColor(s.CpuTemp); RightAlign(_cpuVal, W - 14);
            _gpuVal.Text = $"{s.GpuTemp} °C"; _gpuVal.ForeColor = TempColor(s.GpuTemp); RightAlign(_gpuVal, W - 14);
            _cpuSub.Text = $"Ventoinha 1  ·  {s.Fan1Rpm} rpm"; _gpuSub.Text = $"Ventoinha 2  ·  {s.Fan2Rpm} rpm";
            _fan1Txt.Text = $"{s.Fan1Rpm} rpm"; RightAlign(_fan1Txt, W - 14);
            _fan2Txt.Text = $"{s.Fan2Rpm} rpm"; RightAlign(_fan2Txt, W - 14);
            _dutyTxt.Text = $"VENTOINHA {s.Duty}%"; RightAlign(_dutyTxt, X + W);
            _tray.Text = $"CPU {s.CpuTemp}°C · GPU {s.GpuTemp}°C · {s.Fan1Rpm}/{s.Fan2Rpm} rpm";
            SetConnected(true);
        }
        catch { SetConnected(false); }
    }

    void SetConnected(bool ok)
    {
        if (ok == _connected && _statusTxt.Text.Length > 0) return;
        _connected = ok; _statusTxt.Text = ok ? "Conectado" : "Sem acesso ao EC"; _dot.Invalidate();
    }

    // ---------- bandeja ----------
    void BuildTray()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Abrir painel", null, (_, _) => ShowPanel());
        menu.Items.Add(new ToolStripSeparator());
        var fanMenu = new ToolStripMenuItem("Ventoinhas");
        foreach (var (name, mode) in new[] { ("Padrão", FanMode.Default), ("Silencioso", FanMode.Silent), ("Turbo", FanMode.Turbo), ("Fixo", FanMode.Fixed) })
            fanMenu.DropDownItems.Add(name, null, (_, _) => { _s.Fan = mode; UpdateFanUi(); QueueFan(); RefreshAll(); });
        var boost = new ToolStripMenuItem("Fan Boost (máximo)") { CheckOnClick = true };
        boost.CheckedChanged += (_, _) => { if (boost.Checked != _boost.Checked) _boost.Checked = boost.Checked; };
        _boost.Changed += (_, _) => boost.Checked = _boost.Checked;
        fanMenu.DropDownItems.Add(new ToolStripSeparator()); fanMenu.DropDownItems.Add(boost);
        menu.Items.Add(fanMenu);
        var kbMenu = new ToolStripMenuItem("Teclado");
        foreach (var (name, c) in new[] { ("Branco", Color.White), ("Vermelho", Color.Red), ("Verde", Color.Lime), ("Azul", Color.FromArgb(0, 90, 255)), ("Ciano", Color.Cyan), ("Roxo", Color.FromArgb(180, 0, 255)) })
            kbMenu.DropDownItems.Add(name, null, (_, _) => { _kbOn.Checked = true; SetColor(c); });
        kbMenu.DropDownItems.Add(new ToolStripSeparator());
        kbMenu.DropDownItems.Add("Ligar/Desligar luz", null, (_, _) => _kbOn.Checked = !_kbOn.Checked);
        menu.Items.Add(kbMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());

        _tray.Icon = Icon; _tray.Text = "LiteDeck"; _tray.ContextMenuStrip = menu; _tray.Visible = _hw != null;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Visible) Hide();
            else if ((DateTime.Now - _lastHide).TotalMilliseconds > 300) ShowPanel();
        };
    }

    void PlaceNearTray()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Right - Width - 10, wa.Bottom - Height - 10);
    }

    void ShowPanel()
    {
        PlaceNearTray();
        Show(); Activate();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80; /* WS_EX_TOOLWINDOW: fora do Alt+Tab */ return cp; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4);
        int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
        int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16); DwmSetWindowAttribute(Handle, 34, ref border, 4);
    }

    // ---------- autostart (Agendador de Tarefas, privilégio máximo, sem UAC) ----------
    const string TaskName = "LiteDeck";

    static bool IsAutostart() => RunSchtasks($"/Query /TN {TaskName}") == 0;

    static void SetAutostart(bool on)
    {
        if (on) RunSchtasks($"/Create /TN {TaskName} /TR \"\\\"{Environment.ProcessPath}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F");
        else RunSchtasks($"/Delete /TN {TaskName} /F");
    }

    static int RunSchtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit(8000); return p.ExitCode;
        }
        catch { return -1; }
    }
}
