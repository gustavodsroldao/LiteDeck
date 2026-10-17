using System.Diagnostics;
using System.Text;

namespace LiteDeck;

/// <summary>
/// Diagnóstico: monitora a RAM do EC e registra cada byte que muda, com horário.
/// Serve para mapear opções que o app do fabricante grava por caminhos difíceis de interceptar (ex.: WMI).
/// Termina quando o arquivo "&lt;saida&gt;.stop" existir.
/// </summary>
static class EcWatch
{
    public static void Run(string outFile)
    {
        using var ec = new Ec();
        var ranges = new[] { (0x0000, 0x00FF), (0x0400, 0x07FF), (0x1800, 0x18FF) };
        var prev = ranges.Select(r => Dump(ec, r.Item1, r.Item2)).ToArray();
        var changes = new Dictionary<int, int>();
        var sw = Stopwatch.StartNew();
        string stop = outFile + ".stop";
        using var w = new StreamWriter(outFile, false, Encoding.UTF8) { AutoFlush = true };
        w.WriteLine($"# início {DateTime.Now:HH:mm:ss}");

        int errors = 0;
        while (!File.Exists(stop))
        {
            Thread.Sleep(250);
            for (int k = 0; k < ranges.Length; k++)
            {
                byte[] cur;
                // o app do fabricante disputa o EC; uma leitura falha de vez em quando — anota e tenta de novo
                try { cur = Dump(ec, ranges[k].Item1, ranges[k].Item2); }
                catch (Exception e) { if (errors++ < 20) w.WriteLine($"{DateTime.Now:HH:mm:ss.fff} # erro de leitura: {e.Message}"); continue; }
                for (int i = 0; i < cur.Length; i++)
                {
                    if (cur[i] == prev[k][i]) continue;
                    int addr = ranges[k].Item1 + i;
                    changes[addr] = changes.GetValueOrDefault(addr) + 1;
                    w.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {addr:x4}: {prev[k][i]:x2} -> {cur[i]:x2}");
                }
                prev[k] = cur;
            }
        }
        w.WriteLine("# contagem de mudanças por endereço (muitas = ruído de sensor):");
        foreach (var kv in changes.OrderByDescending(x => x.Value)) w.WriteLine($"#   {kv.Key:x4}: {kv.Value}");
        File.Delete(stop);
    }

    static byte[] Dump(Ec ec, int from, int to)
    {
        var b = new byte[to - from + 1];
        for (int i = 0; i < b.Length; i++) b[i] = ec.Read(from + i);
        return b;
    }
}
