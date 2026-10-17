using System.Drawing;

namespace LiteDeck;

public enum FanMode { Default, Silent, Turbo, Fixed }

/// <summary>Operações de alto nível sobre o hardware (teclado, fans, sensores).</summary>
public sealed class Hw : IDisposable
{
    readonly Ec _ec = new();

    // ---- Teclado retroiluminado ----
    const int RegBright = 0x78C, RegApply = 0x767, RegInit = 0x7C5, RegR = 0x769, RegG = 0x76A, RegB = 0x76B;
    /// <summary>Níveis de brilho suportados pelo EC (0 = apagado).</summary>
    public const int BrightLevels = 4;

    static int Scale(byte c) => (int)Math.Round(c * 50.0 / 255.0);

    // 0x0767 mistura bits independentes: 0x01 = comando "alternar tecla Windows" (o EC limpa),
    // 0x10 = carregamento USB desligado (persistente), 0x20 = comando "aplicar cor" (o EC limpa).
    // Por isso toda escrita preserva os bits persistentes e nunca reenvia um comando pendente.
    int Reg767Persistent => _ec.Read(RegApply) & ~(0x01 | 0x20);

    public void SetKeyboardColor(Color c)
    {
        int keep = Reg767Persistent;
        _ec.Write(RegApply, keep);
        _ec.Write(RegInit, 0);
        _ec.Write(RegR, Scale(c.R));
        _ec.Write(RegG, Scale(c.G));
        _ec.Write(RegB, Scale(c.B));
        _ec.Write(RegApply, keep | 0x20);
    }

    /// <summary>Lê do EC a cor/brilho atuais do teclado (última configuração aplicada, inclusive pelo app original).</summary>
    public (Color Color, int Brightness) ReadKeyboardState()
    {
        static int Up(int v) => Math.Clamp((int)Math.Round(v * 255.0 / 50.0), 0, 255);
        var c = Color.FromArgb(Up(_ec.Read(RegR)), Up(_ec.Read(RegG)), Up(_ec.Read(RegB)));
        return (c, LevelOf(_ec.Read(RegBright)));
    }

    // 0x078C: nível nos bits 5-7 (passos de 0x20, 0..4) + bit 0x10 "aplicar", que o EC limpa depois de aplicar.
    public void SetKeyboardLevel(int level) => _ec.Write(RegBright, (Math.Clamp(level, 0, BrightLevels) << 5) | 0x10);

    public int ReadKeyboardLevel() => LevelOf(_ec.Read(RegBright));

    static int LevelOf(int reg) => Math.Clamp((reg >> 5) & 7, 0, BrightLevels);

    // ---- Ventoinhas ----
    const int RegFanCtl = 0x751;

    public void SetFanBoost(bool on)
    {
        int v = _ec.Read(RegFanCtl);
        _ec.Write(RegFanCtl, on ? v | 0x40 : v & ~0x40);
    }

    public void SetFanMode(FanMode mode, int fixedPercent)
    {
        var t = mode switch
        {
            FanMode.Silent => Fans.Silent,
            FanMode.Turbo => Fans.Turbo,
            FanMode.Fixed => Fans.Fixed(fixedPercent),
            _ => Fans.Default,
        };
        Fans.WriteTable(_ec, t);
    }

    // ---- Opções gerais no EC ----
    void SetBit(int reg, int bit, bool on, int keepMask = 0xFF)
    {
        int v = _ec.Read(reg) & keepMask;
        _ec.Write(reg, on ? v | bit : v & ~bit);
    }

    /// <summary>Tecla Windows bloqueada pelo firmware (estado em 0x0768 bit 0; o comando em 0x0767 bit 0 alterna).</summary>
    public bool WinKeyLocked => (_ec.Read(0x768) & 0x01) != 0;

    /// <summary>
    /// O comando só alterna e o EC demora para refletir o estado (&gt;600 ms), então espera a confirmação
    /// antes de retornar; assim chamadas seguidas nunca mandam o comando duas vezes.
    /// </summary>
    public bool SetWinKeyLock(bool on)
    {
        if (WinKeyLocked == on) return true;
        // o EC só reage à transição do bit de comando: espera um comando anterior ser consumido
        for (int i = 0; i < 20 && (_ec.Read(RegApply) & 0x01) != 0; i++) Thread.Sleep(100);
        _ec.Write(RegApply, Reg767Persistent | 0x01);
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(100);
            if (WinKeyLocked == on) return true;
        }
        return false;
    }

    /// <summary>Fn lock: F1–F12 como função primária (0x074E bit 0x10).</summary>
    public bool FnLock { get => (_ec.Read(0x74E) & 0x10) != 0; set => SetBit(0x74E, 0x10, value); }

    /// <summary>Mantém energia nas portas USB com o notebook desligado/hibernando (0x0767 bit 0x10).</summary>
    public bool UsbCharging { get => (_ec.Read(RegApply) & 0x10) != 0; set => SetBit(RegApply, 0x10, value, ~(0x01 | 0x20)); }

    /// <summary>Liga o notebook sozinho ao conectar a fonte (0x0726 bit 0x08).</summary>
    public bool AcPowerOn { get => (_ec.Read(0x726) & 0x08) != 0; set => SetBit(0x726, 0x08, value); }

    /// <summary>Touchpad habilitado no EC (0x07A6 bit 0x08).</summary>
    public bool TouchpadOn { get => (_ec.Read(0x7A6) & 0x08) != 0; set => SetBit(0x7A6, 0x08, value); }

    // ---- Sensores ----
    public record Status(int CpuTemp, int GpuTemp, int Fan1Rpm, int Fan2Rpm, int Duty);

    public Status ReadStatus() => new(
        _ec.Read(0x43E), _ec.Read(0x44F),
        _ec.Read(0x464) << 8 | _ec.Read(0x465),
        _ec.Read(0x46C) << 8 | _ec.Read(0x46D),
        _ec.Read(0x75B) / 2);

    public Ec Ec => _ec;
    public void Dispose() => _ec.Dispose();
}
