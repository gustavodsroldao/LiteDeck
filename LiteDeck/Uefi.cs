using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LiteDeck;

/// <summary>Acesso à variável UEFI de configurações do fabricante (chassi Uniwill).</summary>
static class Uefi
{
    public const string VarName = "UniWillVariable";
    public const string VarGuid = "{9f33f85c-13ca-4fd1-9c4a-96217722c593}";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetFirmwareEnvironmentVariableExW(string name, string guid, byte[] buf, int size, out int attributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetFirmwareEnvironmentVariableExW(string name, string guid, byte[] buf, int size, int attributes);

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(nint process, int access, out nint token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(nint token, bool disableAll, ref TokenPrivileges p, int len, nint prev, nint retLen);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll")] static extern nint GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    static bool _priv;

    static void EnablePrivilege()
    {
        if (_priv) return;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var tok)) throw new Win32Exception();
        try
        {
            if (!LookupPrivilegeValueW(null, "SeSystemEnvironmentPrivilege", out long luid)) throw new Win32Exception();
            var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 /* SE_PRIVILEGE_ENABLED */ };
            if (!AdjustTokenPrivileges(tok, false, ref tp, 0, 0, 0)) throw new Win32Exception();
        }
        finally { CloseHandle(tok); }
        _priv = true;
    }

    public static (byte[] Data, int Attributes) Read()
    {
        EnablePrivilege();
        var buf = new byte[4096];
        int n = GetFirmwareEnvironmentVariableExW(VarName, VarGuid, buf, buf.Length, out int attr);
        if (n == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível ler a variável UEFI.");
        return (buf[..n], attr);
    }

    public static void Write(byte[] data, int attributes)
    {
        EnablePrivilege();
        if (!SetFirmwareEnvironmentVariableExW(VarName, VarGuid, data, data.Length, attributes))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível gravar a variável UEFI.");
    }

    // ---- Optimus / MUX ----
    // Offset 0x62 da variável: 0x02 = conexão direta da dGPU (Optimus desativado), 0x04 = Optimus (híbrido iGPU+dGPU).
    // Mapeado comparando a variável antes/depois de alternar a opção no app do fabricante; nenhum outro byte muda.
    const int OptimusOffset = 0x62, Direct = 0x02, Hybrid = 0x04, ExpectedSize = 180;
    static readonly byte[] Header = { 0x05, 0x1d, 0x43, 0x12 };

    static void Validate(byte[] d)
    {
        if (d.Length != ExpectedSize || !d.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new InvalidOperationException("Formato da variável UEFI inesperado; por segurança o Optimus não será alterado.");
        if (d[OptimusOffset] is not (Direct or Hybrid))
            throw new InvalidOperationException($"Valor inesperado no campo do Optimus (0x{d[OptimusOffset]:x2}); nada foi alterado.");
    }

    /// <summary>true = "Desativar NVIDIA Optimus" ligado (conexão direta). Vale a partir do próximo boot.</summary>
    public static bool OptimusDisabled
    {
        get { var (d, _) = Read(); Validate(d); return d[OptimusOffset] == Direct; }
        set
        {
            var (d, attr) = Read(); Validate(d);
            byte want = value ? (byte)Direct : (byte)Hybrid;
            if (d[OptimusOffset] == want) return;
            var n = (byte[])d.Clone(); n[OptimusOffset] = want;
            Write(n, attr);
            var (check, _) = Read();
            if (!check.AsSpan().SequenceEqual(n)) throw new InvalidOperationException("A variável UEFI não confirmou a alteração do Optimus.");
        }
    }

    /// <summary>Diagnóstico (só leitura): grava um dump hexadecimal da variável em <paramref name="file"/>.</summary>
    public static void Dump(string file)
    {
        var (d, attr) = Read();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {VarName} {VarGuid} tamanho={d.Length} atributos=0x{attr:x}");
        for (int i = 0; i < d.Length; i += 16)
            sb.AppendLine($"{i:x3}: " + string.Join(' ', d.Skip(i).Take(16).Select(b => b.ToString("x2"))));
        File.WriteAllText(file, sb.ToString());
    }
}
