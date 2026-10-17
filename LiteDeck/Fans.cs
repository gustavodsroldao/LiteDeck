namespace LiteDeck;

public sealed record FanCurve(byte[] Up, byte[] Down, byte[] Duty)
{
    public FanCurve Scaled(double f)
    {
        var d = Duty.Select(x => x == 0xFF || x == 0 ? x : (byte)Math.Clamp((int)Math.Round(x * f), 1, 0xC8)).ToArray();
        return new FanCurve(Up, Down, d);
    }
}

public sealed record FanTable(FanCurve Cpu, FanCurve Gpu);

public static class Fans
{
    const int Cpu = 0xF00, Gpu = 0xF30;

    static byte[] Pad(params byte[] v) { var r = Enumerable.Repeat((byte)0xFF, 16).ToArray(); v.CopyTo(r, 0); return r; }

    // Curva padrão capturada do app do fabricante (chassi Uniwill).
    public static readonly FanTable Default = new(
        new FanCurve(Pad(0x36, 0x39, 0x3c, 0x40, 0x44, 0x48, 0x4b, 0x4e, 0x51, 0x54),
                     Pad(0x00, 0x30, 0x3a, 0x3d, 0x41, 0x45, 0x49, 0x4c, 0x4f, 0x52, 0x55),
                     Pad(0x00, 0x3c, 0x46, 0x50, 0x5a, 0x64, 0x78, 0x8c, 0xa0, 0xb4, 0xbe, 0xbe, 0xbe, 0xbe, 0xbe, 0xbe)),
        new FanCurve(Pad(0x36, 0x36, 0x36, 0x3e, 0x3e, 0x41, 0x44, 0x47, 0x4a, 0x4d),
                     Pad(0x00, 0x30, 0x3a, 0x3a, 0x3a, 0x3f, 0x42, 0x45, 0x48, 0x4b, 0x4e),
                     Pad(0x00, 0x00, 0x46, 0x50, 0x5a, 0x64, 0x78, 0x8c, 0xa0, 0xb4, 0xbe, 0xbe, 0xbe, 0xbe, 0xbe, 0xbe)));

    public static FanTable Silent => new(Default.Cpu.Scaled(0.8), Default.Gpu.Scaled(0.8));
    public static FanTable Turbo => new(Default.Cpu.Scaled(1.15), Default.Gpu.Scaled(1.15));

    /// <summary>Velocidade fixa em % (o EC usa 0..200 = 0..100%).</summary>
    public static FanTable Fixed(int percent)
    {
        byte d = (byte)Math.Clamp(percent * 2, 0, 200);
        var c = new FanCurve(Pad(0x00), Pad(0x00), Pad(d));
        return new FanTable(c, c);
    }

    public static void WriteTable(Ec ec, FanTable t)
    {
        void W(int b, FanCurve c)
        {
            for (int i = 0; i < 16; i++) { ec.Write(b + i, c.Up[i]); ec.Write(b + 0x10 + i, c.Down[i]); ec.Write(b + 0x20 + i, c.Duty[i]); }
        }
        W(Cpu, t.Cpu); W(Gpu, t.Gpu);
    }
}
