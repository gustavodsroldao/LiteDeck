using System.Drawing.Drawing2D;

namespace LiteDeck;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(22, 22, 25);
    public static readonly Color Card = Color.FromArgb(31, 31, 35);
    public static readonly Color CardHover = Color.FromArgb(40, 40, 45);
    public static readonly Color Border = Color.FromArgb(46, 46, 52);
    public static readonly Color Track = Color.FromArgb(58, 58, 64);
    public static readonly Color Accent = Color.FromArgb(226, 58, 58);
    public static readonly Color Text = Color.FromArgb(236, 236, 238);
    public static readonly Color Muted = Color.FromArgb(132, 132, 140);
    public static readonly Font Body = new("Segoe UI", 9f);
    public static readonly Font Small = new("Segoe UI", 7.75f);
    public static readonly Font Caps = new("Segoe UI Semibold", 6.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Big = new("Segoe UI Semibold", 17f);

    public static Label Label(string text, Font? f = null, Color? c = null) =>
        new() { Text = text, Font = f ?? Body, ForeColor = c ?? Text, AutoSize = true, BackColor = Color.Transparent };

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}

static class Icons
{
    static readonly string Family = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    static readonly Dictionary<float, Font> Cache = new();
    public static Font Font(float size) { if (!Cache.TryGetValue(size, out var f)) Cache[size] = f = new Font(Family, size); return f; }

    public const string Home = "", Keyboard = "", Settings = "", More = "", Power = "", Bolt = "",
        Moon = "", Brightness = "", Plus = "", Minus = "", Gauge = "", Chevron = "", Sliders = "", Chip = "", Display = "", Dot = "●";

    public static void Draw(Graphics g, string glyph, float size, Rectangle r, Color c) =>
        TextRenderer.DrawText(g, glyph, Font(size), r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

    /// <summary>Ventoinha desenhada à mão (não existe glyph padrão).</summary>
    public static void DrawFan(Graphics g, RectangleF r, Color c, Color hubColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(c);
        for (int i = 0; i < 3; i++) g.FillPie(b, r.X, r.Y, r.Width, r.Height, i * 120 - 20, 62);
        float d = r.Width * 0.3f; using var hub = new SolidBrush(hubColor);
        g.FillEllipse(hub, r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
    }
}

/// <summary>Cartão plano com cantos arredondados e borda de 1px.</summary>
sealed class RoundedPanel : Panel
{
    public RoundedPanel() { BackColor = Theme.Card; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Theme.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 14);
        using (var b = new SolidBrush(BackColor)) g.FillPath(b, p);
        using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
    }
}

/// <summary>Botão de ícone para o cabeçalho.</summary>
sealed class IconButton : Control
{
    bool _hover, _on;
    public string Glyph { get; set; }
    public bool Active { get => _on; set { _on = value; Invalidate(); } }
    public IconButton(string glyph) { Glyph = glyph; Size = new Size(34, 34); Cursor = Cursors.Hand; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Bg);
        if (_hover || _on) using (var b = new SolidBrush(_on ? Color.FromArgb(38, Theme.Accent) : Theme.Card)) g.FillEllipse(b, 1, 1, Width - 3, Height - 3);
        Icons.Draw(g, Glyph, 12.5f, new Rectangle(0, 0, Width, Height), _on ? Theme.Accent : Theme.Muted);
    }
}

/// <summary>Botão redondo pequeno (− / +).</summary>
sealed class RoundBtn : Control
{
    bool _hover; readonly string _glyph;
    public RoundBtn(string glyph) { _glyph = glyph; Size = new Size(32, 32); Cursor = Cursors.Hand; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Card);
        using (var b = new SolidBrush(_hover ? Theme.Track : Theme.CardHover)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        Icons.Draw(g, _glyph, 9f, new Rectangle(0, 0, Width, Height), Theme.Text);
    }
}

/// <summary>Botão de texto selecionável.</summary>
sealed class Chip : Control
{
    bool _on, _hover;
    public bool Selected { get => _on; set { _on = value; Invalidate(); } }
    public Chip(string text) { Text = text; Font = Theme.Body; Cursor = Cursors.Hand; Height = 34; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Bg);
        using var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10);
        using (var b = new SolidBrush(_on ? Color.FromArgb(34, Theme.Accent) : _hover ? Theme.Track : Theme.CardHover)) g.FillPath(b, p);
        using (var pen = new Pen(_on ? Theme.Accent : Theme.Border)) g.DrawPath(pen, p);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), _on ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Tile quadrado com ícone e legenda embaixo, como os "Favoritos" do exemplo.</summary>
sealed class Tile : Control
{
    bool _on, _hover; readonly string _glyph; readonly string _label;
    public bool Selected { get => _on; set { _on = value; Invalidate(); } }
    public Tile(string glyph, string label) { _glyph = glyph; _label = label; Size = new Size(72, 84); Cursor = Cursors.Hand; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Card);
        var box = new RectangleF(1.25f, 1.25f, Width - 3f, 56.5f);
        using var p = Theme.Round(box, 14);
        using (var b = new SolidBrush(_on ? Color.FromArgb(34, Theme.Accent) : _hover ? Theme.Track : Theme.CardHover)) g.FillPath(b, p);
        using (var pen = new Pen(_on ? Theme.Accent : Theme.Border, _on ? 1.5f : 1f)) g.DrawPath(pen, p);
        var col = _on ? Theme.Accent : Theme.Text;
        if (_glyph == "fan") Icons.DrawFan(g, new RectangleF(Width / 2f - 12, 17, 24, 24), col, _on ? Color.FromArgb(34, 22, 22) : Theme.CardHover);
        else Icons.Draw(g, _glyph, 15f, new Rectangle(0, 0, Width, 58), col);
        TextRenderer.DrawText(g, _label, Theme.Small, new Rectangle(0, 62, Width, 20), _on ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
    }
}

/// <summary>Interruptor on/off.</summary>
sealed class Toggle : Control
{
    bool _on;
    public bool Checked { get => _on; set { if (_on == value) return; _on = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? Changed;
    public Toggle() { Size = new Size(42, 24); Cursor = Cursors.Hand; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Bg);
        using (var p = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f)) using (var b = new SolidBrush(_on ? Theme.Accent : Theme.Track)) g.FillPath(b, p);
        int x = _on ? Width - Height + 3 : 3;
        using var k = new SolidBrush(Color.White); g.FillEllipse(k, x, 3, Height - 7, Height - 7);
    }
}

/// <summary>Barra de abas: um contêiner segmentado com ícones.</summary>
sealed class TabBar : Control
{
    readonly string[] _glyphs; int _sel, _hover = -1;
    public event EventHandler? SelectedChanged;
    public int Selected { get => _sel; set { if (_sel == value) return; _sel = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); } }
    public TabBar(params string[] glyphs) { _glyphs = glyphs; Height = 38; Cursor = Cursors.Hand; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    RectangleF Cell(int i) { float pad = 4, w = (Width - pad * 2) / _glyphs.Length; return new RectangleF(pad + i * w + 2, pad, w - 4, Height - pad * 2 - 1); }
    protected override void OnMouseMove(MouseEventArgs e) { int h = -1; for (int i = 0; i < _glyphs.Length; i++) if (Cell(i).Contains(e.Location)) h = i; if (h != _hover) { _hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { for (int i = 0; i < _glyphs.Length; i++) if (Cell(i).Contains(e.Location)) Selected = i; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Bg);
        using (var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 11))
        {
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
            using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
        }
        for (int i = 0; i < _glyphs.Length; i++)
        {
            var r = Cell(i); bool on = i == _sel;
            if (on || i == _hover)
                using (var p = Theme.Round(r, 8)) using (var b = new SolidBrush(on ? Color.FromArgb(52, Theme.Accent) : Theme.CardHover)) g.FillPath(b, p);
            var col = on ? Theme.Accent : Theme.Muted;
            if (_glyphs[i] == "fan") Icons.DrawFan(g, new RectangleF(r.X + r.Width / 2f - 8, r.Y + r.Height / 2f - 8, 16, 16), col, on ? Color.FromArgb(60, 30, 32) : Theme.Card);
            else Icons.Draw(g, _glyphs[i], 11f, Rectangle.Round(r), col);
        }
    }
}

/// <summary>Quadradinho com ícone, usado nas linhas de lista (estilo "Entradas").</summary>
sealed class IconTile : Control
{
    readonly string _glyph; Color _color;
    public Color IconColor { get => _color; set { _color = value; Invalidate(); } }
    public IconTile(string glyph, Color? c = null) { _glyph = glyph; _color = c ?? Theme.Text; Size = new Size(30, 30); SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Card);
        using (var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8)) using (var b = new SolidBrush(Theme.CardHover)) g.FillPath(b, p);
        if (_glyph == "fan") Icons.DrawFan(g, new RectangleF(Width / 2f - 8, Height / 2f - 8, 16, 16), _color, Theme.CardHover);
        else Icons.Draw(g, _glyph, 10.5f, new Rectangle(0, 0, Width, Height), _color);
    }
}
