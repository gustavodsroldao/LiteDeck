using System.Drawing.Drawing2D;

namespace LiteDeck;

/// <summary>Notebook desenhado em vetor: as teclas acendem na cor e no brilho escolhidos.</summary>
sealed class LaptopView : Control
{
    // espaço de desenho de referência; é escalado para caber no controle
    const float RefW = 480, RefH = 340;

    Color _color = Color.White;
    float _bright = 1f;

    public LaptopView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Atualiza a luz do teclado (bright: 0..1; 0 = apagado).</summary>
    public void SetKeyboard(Color color, float bright)
    {
        _color = color; _bright = Math.Clamp(bright, 0f, 1f);
        Invalidate();
    }

    static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    static GraphicsPath Poly(params PointF[] p) { var g = new GraphicsPath(); g.AddPolygon(p); return g; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float sc = Math.Min(Width / RefW, Height / RefH);
        g.TranslateTransform((Width - RefW * sc) / 2, (Height - RefH * sc) / 2);
        g.ScaleTransform(sc, sc);

        float lit = _bright;

        // sombra e reflexo embaixo do notebook, na cor do teclado
        using (var gp = new GraphicsPath())
        {
            gp.AddEllipse(30, 300, 420, 44);
            using var pg = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(lit > 0 ? (int)(80 * lit) : 60, lit > 0 ? _color : Color.Black), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } };
            g.FillPath(pg, gp);
        }

        // ---- tampa e tela ----
        var lid = new RectangleF(52, 4, 376, 236);
        using (var p = Theme.Round(lid, 11))
        {
            using (var b = new LinearGradientBrush(lid, Color.FromArgb(40, 40, 46), Color.FromArgb(26, 26, 30), 90f)) g.FillPath(b, p);
            using var pen = new Pen(Color.FromArgb(70, 70, 78), 1.2f); g.DrawPath(pen, p);
        }
        var screen = new RectangleF(63, 15, 354, 212);
        using (var p = Theme.Round(screen, 4))
        {
            using (var b = new LinearGradientBrush(screen, Color.FromArgb(20, 22, 32), Color.FromArgb(9, 9, 13), 90f)) g.FillPath(b, p);
            var old = g.Clip; g.SetClip(p, CombineMode.Intersect);
            // luz ambiente da cor do teclado refletida na tela
            if (lit > 0)
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(screen.X + 40, screen.Bottom - 110, screen.Width - 80, 190);
                    using var pg = new PathGradientBrush(gp) { CenterColor = Color.FromArgb((int)(55 * lit), _color), SurroundColors = new[] { Color.FromArgb(0, _color) } };
                    g.FillPath(pg, gp);
                }
            // brilho diagonal do vidro
            using (var sheen = Poly(new(screen.X, screen.Y), new(screen.X + 150, screen.Y), new(screen.X + 60, screen.Bottom), new(screen.X, screen.Bottom)))
            using (var b = new SolidBrush(Color.FromArgb(9, 255, 255, 255))) g.FillPath(b, sheen);
            g.Clip = old;
        }
        using (var b = new SolidBrush(Color.FromArgb(70, 70, 78))) g.FillEllipse(b, 238.5f, 7.5f, 3, 3);

        // ---- dobradiça e base ----
        using (var p = Theme.Round(new RectangleF(70, 238, 340, 8), 3)) using (var b = new SolidBrush(Color.FromArgb(18, 18, 21))) g.FillPath(b, p);
        using (var deck = Poly(new(44, 245), new(436, 245), new(466, 312), new(14, 312)))
        {
            using (var b = new LinearGradientBrush(new RectangleF(14, 245, 452, 67), Color.FromArgb(50, 50, 57), Color.FromArgb(36, 36, 42), 90f)) g.FillPath(b, deck);
            using var pen = new Pen(Color.FromArgb(66, 66, 74)); g.DrawPath(pen, deck);
        }
        using (var lip = Poly(new(14, 312), new(466, 312), new(460, 320), new(20, 320)))
        using (var b = new SolidBrush(Color.FromArgb(24, 24, 28))) g.FillPath(b, lip);

        // ---- teclado ----
        using (var well = Poly(new(66, 250), new(414, 250), new(424, 292), new(56, 292)))
        using (var b = new SolidBrush(Color.FromArgb(20, 20, 24))) g.FillPath(b, well);

        var off = Color.FromArgb(44, 44, 50);
        var face = lit > 0 ? Mix(off, _color, 0.25f + 0.65f * lit) : off;
        using var keyBrush = new SolidBrush(face);
        using var haloBrush = new SolidBrush(Color.FromArgb((int)(70 * lit), _color));

        const int rows = 6;
        for (int r = 0; r < rows; r++)
        {
            float t = (r + 0.5f) / rows;
            float y = 252.5f + r * 6.4f, h = 4.6f;
            float left = 68 - 10 * t, right = 412 + 10 * t, width = right - left;
            float mainW = width * 0.775f, gap = width * 0.025f, numW = width - mainW - gap;

            // bloco principal: 15 teclas; na última linha a barra de espaço ocupa o meio
            const int keys = 15; float kw = mainW / keys;
            for (int k = 0; k < keys; k++)
            {
                float x = left + k * kw, w = kw - 1.6f;
                if (r == rows - 1 && k is >= 4 and <= 9) { if (k > 4) continue; w = kw * 6 - 1.6f; }
                DrawKey(g, new RectangleF(x, y, w, h), keyBrush, haloBrush, lit);
            }
            // teclado numérico: 4 colunas
            float nx = left + mainW + gap, nkw = numW / 4;
            for (int k = 0; k < 4; k++) DrawKey(g, new RectangleF(nx + k * nkw, y, nkw - 1.6f, h), keyBrush, haloBrush, lit);
        }

        // brilho geral sobre as teclas
        if (lit > 0)
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(50, 240, 380, 64);
                using var pg = new PathGradientBrush(gp) { CenterColor = Color.FromArgb((int)(42 * lit), _color), SurroundColors = new[] { Color.FromArgb(0, _color) } };
                g.FillPath(pg, gp);
            }

        // touchpad
        using (var p = Theme.Round(new RectangleF(196, 296, 88, 13), 3))
        {
            using (var b = new SolidBrush(Color.FromArgb(42, 42, 48))) g.FillPath(b, p);
            using var pen = new Pen(Color.FromArgb(62, 62, 70), 0.8f); g.DrawPath(pen, p);
        }
    }

    static void DrawKey(Graphics g, RectangleF r, Brush face, Brush halo, float lit)
    {
        if (lit > 0) using (var hp = Theme.Round(RectangleF.Inflate(r, 1.1f, 1.1f), 1.6f)) g.FillPath(halo, hp);
        using var p = Theme.Round(r, 1.1f);
        g.FillPath(face, p);
    }
}

static class Hsv
{
    public static Color ToColor(float h, float s, float v)
    {
        int hi = (int)(h / 60) % 6; float f = h / 60 - (float)Math.Floor(h / 60);
        float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (float r, float g, float b) = hi switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }
}
