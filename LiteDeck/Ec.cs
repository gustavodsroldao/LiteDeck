using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LiteDeck;

/// <summary>Acesso ao Embedded Controller via driver UWACPIDriver (\\.\ACPIDriver), mesmos IOCTLs usados pelo app do fabricante.</summary>
public sealed class Ec : IDisposable
{
    const uint IoctlEcRead = 0x9C40A488;
    const uint IoctlEcWrite = 0x9C40A48C;

    readonly SafeFileHandle _h;
    readonly object _lock = new();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint sec, uint disp, uint flags, nint tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int ret, nint ov);

    static SafeFileHandle Open() => CreateFileW(@"\\.\ACPIDriver", 0xC0000000, 3, 0, 3, 0, 0);

    public Ec()
    {
        _h = Open();
        if (_h.IsInvalid)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", "start UWACPIDriver") { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit(5000);
            }
            catch { }
            _h = Open();
            if (_h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível abrir o driver ACPI da Uniwill (precisa de administrador e do driver instalado).");
        }
    }

    public byte Read(int addr)
    {
        lock (_lock)
        {
            var o = new byte[4];
            if (!DeviceIoControl(_h, IoctlEcRead, BitConverter.GetBytes(addr), 4, o, 4, out _, 0)) throw new Win32Exception();
            return o[0];
        }
    }

    public void Write(int addr, int value)
    {
        lock (_lock)
        {
            var i = new byte[8];
            BitConverter.GetBytes(addr).CopyTo(i, 0);
            BitConverter.GetBytes(value).CopyTo(i, 4);
            if (!DeviceIoControl(_h, IoctlEcWrite, i, 8, new byte[4], 4, out _, 0)) throw new Win32Exception();
        }
    }

    public void Dispose() => _h.Dispose();
}
