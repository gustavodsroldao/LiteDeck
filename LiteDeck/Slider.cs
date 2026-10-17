using System.Drawing.Drawing2D;

namespace LiteDeck;

/// <summary>Slider fino, plano, no estilo do exemplo.</summary>
public sealed class Slider : Control
{
    int _value, _min, _max = 100;
    bool _drag;
    public Color Accent { get; set; } = Theme.Accent;
    public bool Rainbow { get; set; }
    public event EventHandler? ValueChanged;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 26; Cursor = Cursors.Hand;
    }

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }
    public int Value
    {
        get => _value;
        set { value = Math.Clamp(value, _min, _max); if (value == _value) return; _value = value; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }

    float Frac => _max == _min ? 0 : (float)(_value - _min) / (_max - _min);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);
        int pad = 10, h = Rainbow ? 8 : 4, y = (Height - h) / 2;
        var track = new Rectangle(pad, y, Width - pad * 2, h);
        using (var p = Theme.Round(track, h / 2f))
        {
            if (Rainbow)
            {
                using var lg = new LinearGradientBrush(track, Color.Red, Color.Red, 0f);
                lg.InterpolationColors = new ColorBlend
                {
                    Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f },
                    Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                };
                g.FillPath(lg, p);
            }
            else using (var b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
        }
        int cx = track.X + (int)(track.Width * Frac);
        if (!Rainbow && cx > track.X)
        {
            using var p = Theme.Round(new RectangleF(track.X, track.Y, Math.Max(cx - track.X, h), h), h / 2f);
            using var b = new SolidBrush(Enabled ? Accent : Color.FromArgb(80, 80, 88)); g.FillPath(b, p);
        }
        int r = 8;
        using var kb = new SolidBrush(Enabled ? Color.White : Color.FromArgb(130, 130, 136));
        g.FillEllipse(kb, cx - r, Height / 2 - r, r * 2, r * 2);
    }

    void SetFromX(int x)
    {
        int pad = 10; float f = Math.Clamp((x - pad) / (float)(Width - pad * 2), 0f, 1f);
        Value = _min + (int)Math.Round(f * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) { _drag = true; Capture = true; SetFromX(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}
