using System.Text;

namespace LiteDeck;

/// <summary>Diagnóstico: descobre registradores de RPM/temperatura comparando dumps do EC em cenários diferentes.</summary>
static class Probe
{
    static byte[] Dump(Ec ec, int from, int to) { var b = new byte[to - from + 1]; for (int i = 0; i < b.Length; i++) b[i] = ec.Read(from + i); return b; }
    static byte[][] Snap(Ec ec, (int, int)[] r) => r.Select(x => Dump(ec, x.Item1, x.Item2)).ToArray();

    public static void Run(string outFile)
    {
        var sb = new StringBuilder();
        using var ec = new Ec();
        var regions = new[] { (0x0400, 0x04FF), (0x0700, 0x07FF), (0x1800, 0x18FF) };
        var saved = Dump(ec, 0xF00, 0xF5F);
        byte r751 = ec.Read(0x751);
        sb.AppendLine($"0751={r751:x2} 0743={ec.Read(0x743):x2} 0744={ec.Read(0x744):x2} 0745={ec.Read(0x745):x2} 0746={ec.Read(0x746):x2} 07c6={ec.Read(0x7c6):x2} 078c={ec.Read(0x78c):x2} 0767={ec.Read(0x767):x2} 0769={ec.Read(0x769):x2} 076a={ec.Read(0x76a):x2} 076b={ec.Read(0x76b):x2}");
        sb.AppendLine("tabela original: " + Convert.ToHexString(saved));
        File.WriteAllText(outFile, sb.ToString());

        var baseline = Snap(ec, regions);
        sb.AppendLine("== baseline"); for (int k = 0; k < regions.Length; k++) sb.AppendLine($"{regions[k].Item1:x4}: " + Convert.ToHexString(baseline[k]));

        try
        {
            Fans.WriteTable(ec, Fans.Fixed(100));
            Thread.Sleep(8000);
            var t1 = Snap(ec, regions);
            sb.AppendLine("== tabela fixa 100% (diff vs baseline)"); Diff(sb, regions, baseline, t1);
            File.WriteAllText(outFile, sb.ToString());

            ec.Write(0x751, r751 | 0x40);
            Thread.Sleep(8000);
            var t2 = Snap(ec, regions);
            sb.AppendLine("== bit 0x40 em 0751 (diff vs t1)"); Diff(sb, regions, t1, t2);
        }
        finally
        {
            ec.Write(0x751, r751);
            for (int i = 0; i < saved.Length; i++) ec.Write(0xF00 + i, saved[i]);
        }
        Thread.Sleep(6000);
        var t3 = Snap(ec, regions);
        sb.AppendLine("== restaurado (diff vs baseline)"); Diff(sb, regions, baseline, t3);
        File.WriteAllText(outFile, sb.ToString());
    }

    static void Diff(StringBuilder sb, (int, int)[] regs, byte[][] a, byte[][] b)
    {
        for (int k = 0; k < regs.Length; k++)
            for (int i = 0; i < a[k].Length; i++)
                if (a[k][i] != b[k][i]) sb.AppendLine($"  {regs[k].Item1 + i:x4}: {a[k][i]:x2} -> {b[k][i]:x2}");
    }
}
