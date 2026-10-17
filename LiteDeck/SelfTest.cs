using System.Text;

namespace LiteDeck;

/// <summary>Autoteste de hardware: exercita cada função no EC, confere a leitura de volta e restaura o estado original.</summary>
static class SelfTest
{
    public static void Run(string outFile)
    {
        var sb = new StringBuilder();
        int ok = 0, fail = 0;
        void Log(string s) { sb.AppendLine(s); File.WriteAllText(outFile, sb.ToString()); }
        void Check(string name, bool pass, string detail) { if (pass) ok++; else fail++; Log($"[{(pass ? "OK  " : "FALHA")}] {name}: {detail}"); }

        using var hw = new Hw();
        var ec = hw.Ec;

        // estado original
        byte origBright = ec.Read(0x78C), r0 = ec.Read(0x769), g0 = ec.Read(0x76A), b0 = ec.Read(0x76B), fan751 = ec.Read(0x751);
        var table = new byte[0x60]; for (int i = 0; i < table.Length; i++) table[i] = ec.Read(0xF00 + i);
        Log($"Estado original: 078C={origBright:x2} cor={r0},{g0},{b0} 0751={fan751:x2}");

        try
        {
            // ---- Sensores ----
            var st = hw.ReadStatus();
            Check("Sensores", st.CpuTemp is > 15 and < 110 && st.GpuTemp is >= 0 and < 110 && st.Fan1Rpm < 8000 && st.Fan2Rpm < 8000,
                  $"CPU {st.CpuTemp}°C, GPU {st.GpuTemp}°C, fans {st.Fan1Rpm}/{st.Fan2Rpm} rpm, duty {st.Duty}%");

            // ---- Brilho do teclado: todos os níveis, incluindo apagar ----
            foreach (int lvl in new[] { 0, 1, 2, 3, 4, 0 })
            {
                hw.SetKeyboardLevel(lvl);
                Thread.Sleep(700);
                byte reg = ec.Read(0x78C);
                Check($"Brilho nível {lvl}", hw.ReadKeyboardLevel() == lvl, $"078C={reg:x2} (bit aplicar {(((reg & 0x10) == 0) ? "consumido" : "pendente")})");
            }
            hw.SetKeyboardLevel(4);

            // ---- Cor ----
            foreach (var (name, c, exp) in new[] { ("vermelho", Color.FromArgb(255, 0, 0), (50, 0, 0)), ("verde", Color.FromArgb(0, 255, 0), (0, 50, 0)), ("azul", Color.FromArgb(0, 0, 255), (0, 0, 50)), ("branco", Color.White, (50, 50, 50)) })
            {
                hw.SetKeyboardColor(c);
                Thread.Sleep(700);
                var got = (ec.Read(0x769), ec.Read(0x76A), ec.Read(0x76B));
                Check($"Cor {name}", got == exp, $"lido {got}, esperado {exp}, 0767={ec.Read(0x767):x2}");
            }

            // ---- Perfis de ventoinha: a tabela escrita tem que voltar igual ----
            foreach (var (mode, t) in new[] { (FanMode.Default, Fans.Default), (FanMode.Silent, Fans.Silent), (FanMode.Turbo, Fans.Turbo), (FanMode.Fixed, Fans.Fixed(70)) })
            {
                hw.SetFanMode(mode, 70);
                int diffs = 0;
                for (int i = 0; i < 16; i++)
                {
                    if (ec.Read(0xF00 + i) != t.Cpu.Up[i] || ec.Read(0xF10 + i) != t.Cpu.Down[i] || ec.Read(0xF20 + i) != t.Cpu.Duty[i]) diffs++;
                    if (ec.Read(0xF30 + i) != t.Gpu.Up[i] || ec.Read(0xF40 + i) != t.Gpu.Down[i] || ec.Read(0xF50 + i) != t.Gpu.Duty[i]) diffs++;
                }
                Check($"Perfil {mode}", diffs == 0, diffs == 0 ? "tabela gravada e lida igual" : $"{diffs} entradas diferentes");
            }
            Thread.Sleep(6000);
            var fx = hw.ReadStatus();
            Check("Fixo 70% em ação", fx.Duty >= 40, $"duty atual {fx.Duty}% (sobe em rampa até 70%), fans {fx.Fan1Rpm}/{fx.Fan2Rpm} rpm");

            // ---- Opções gerais no EC: alterna, confere e volta ao estado original ----
            foreach (var (name, get, set) in new (string, Func<bool>, Action<bool>)[]
            {
                ("Bloquear tecla Fn", () => hw.FnLock, v => hw.FnLock = v),
                ("Carregar USB desligado", () => hw.UsbCharging, v => hw.UsbCharging = v),
                ("Ligar ao conectar a fonte", () => hw.AcPowerOn, v => hw.AcPowerOn = v),
                ("Bloquear tecla Windows", () => hw.WinKeyLocked, v => hw.SetWinKeyLock(v)),
                ("Touchpad", () => hw.TouchpadOn, v => hw.TouchpadOn = v),
            })
            {
                bool orig = get();
                set(!orig); Thread.Sleep(600); bool flipped = get();
                set(orig); Thread.Sleep(600); bool back = get();
                if (back != orig) { set(orig); Thread.Sleep(1500); back = get(); }   // nunca deixa a opção trocada
                Check(name, flipped == !orig && back == orig, $"original {orig}, alternado {flipped}, restaurado {back}");
            }
            bool usbBefore = hw.UsbCharging;
            hw.SetKeyboardColor(Color.White); Thread.Sleep(300);
            Check("Cor preserva carregamento USB", hw.UsbCharging == usbBefore, $"USB antes {usbBefore}, depois {hw.UsbCharging}");

            // ---- Fan Boost ----
            hw.SetFanBoost(true);
            Thread.Sleep(4000);
            var bs = hw.ReadStatus();
            Check("Fan Boost ligado", (ec.Read(0x751) & 0x40) != 0 && bs.Duty >= 95, $"0751={ec.Read(0x751):x2}, duty {bs.Duty}%, fans {bs.Fan1Rpm}/{bs.Fan2Rpm} rpm");
            hw.SetFanBoost(false);
            Thread.Sleep(1500);
            Check("Fan Boost desligado", (ec.Read(0x751) & 0x40) == 0, $"0751={ec.Read(0x751):x2}");
        }
        catch (Exception e) { fail++; Log("ERRO: " + e); }
        finally
        {
            ec.Write(0x751, fan751);
            for (int i = 0; i < table.Length; i++) ec.Write(0xF00 + i, table[i]);
            hw.SetKeyboardColor(Color.FromArgb(Math.Min(255, r0 * 255 / 50), Math.Min(255, g0 * 255 / 50), Math.Min(255, b0 * 255 / 50)));
            ec.Write(0x78C, (origBright & 0xE0) | 0x10);
            Log("Estado original restaurado.");
        }
        Log($"\nResultado: {ok} OK, {fail} falha(s).");
    }
}
