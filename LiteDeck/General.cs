using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace LiteDeck;

/// <summary>Opções gerais que não passam pelo EC: rádios, dispositivos, teclas e tela.</summary>
static class General
{
    // ================= Wi-Fi (rádio via wlanapi) =================
    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint ver, nint res, out uint negotiated, out nint h);
    [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(nint h, nint res);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(nint h, nint res, out nint list);
    [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(nint h, ref Guid g, int opcode, nint res, out int size, out nint data, nint type);
    [DllImport("wlanapi.dll")] static extern int WlanSetInterface(nint h, ref Guid g, int opcode, int size, ref PhyRadioState data, nint res);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(nint p);

    [StructLayout(LayoutKind.Sequential)] struct PhyRadioState { public int PhyIndex, Software, Hardware; }
    const int OpRadioState = 4, RadioOn = 1, RadioOff = 2;

    static void ForEachWlan(Action<nint, Guid> act)
    {
        if (WlanOpenHandle(2, 0, out _, out var h) != 0) return;
        try
        {
            if (WlanEnumInterfaces(h, 0, out var list) != 0) return;
            try
            {
                int n = Marshal.ReadInt32(list);
                for (int i = 0; i < n; i++)
                {
                    var guid = Marshal.PtrToStructure<Guid>(list + 8 + i * 532);
                    act(h, guid);
                }
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(h, 0); }
    }

    /// <summary>null = sem Wi-Fi.</summary>
    public static bool? WifiOn()
    {
        bool? on = null;
        ForEachWlan((h, g) =>
        {
            if (WlanQueryInterface(h, ref g, OpRadioState, 0, out _, out var data, 0) != 0) return;
            try
            {
                int phys = Marshal.ReadInt32(data);
                bool any = false;
                for (int i = 0; i < phys; i++) if (Marshal.ReadInt32(data + 4 + i * 12 + 4) == RadioOn) any = true;
                on = (on ?? false) || any;
            }
            finally { WlanFreeMemory(data); }
        });
        return on;
    }

    public static void SetWifi(bool on)
    {
        ForEachWlan((h, g) =>
        {
            if (WlanQueryInterface(h, ref g, OpRadioState, 0, out _, out var data, 0) != 0) return;
            int phys; try { phys = Marshal.ReadInt32(data); } finally { WlanFreeMemory(data); }
            for (int i = 0; i < phys; i++)
            {
                var st = new PhyRadioState { PhyIndex = i, Software = on ? RadioOn : RadioOff };
                WlanSetInterface(h, ref g, OpRadioState, Marshal.SizeOf<PhyRadioState>(), ref st, 0);
            }
        });
    }

    // ================= Dispositivos PnP (SetupAPI) =================
    [StructLayout(LayoutKind.Sequential)] struct DevInfo { public int Size; public Guid ClassGuid; public uint DevInst; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct PropChange { public int HeaderSize, InstallFunction, StateChange, Scope, HwProfile; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint SetupDiGetClassDevsW(nint cls, string? enumerator, nint hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(nint set, int idx, ref DevInfo d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceIdW(nint set, ref DevInfo d, StringBuilder id, int size, out int req);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceRegistryPropertyW(nint set, ref DevInfo d, int prop, out int type, byte[]? buf, int size, out int req);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiSetClassInstallParamsW(nint set, ref DevInfo d, ref PropChange p, int size);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiCallClassInstaller(int fn, nint set, ref DevInfo d);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);

    const int DigcfPresent = 2, DigcfAllClasses = 4, DifPropertyChange = 0x12, SpdrpHardwareId = 1;
    static readonly Guid ClsBluetooth = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
    static readonly Guid ClsCamera = new("ca3e7ab9-b4c3-4ae6-8251-579ef933890f");
    static readonly Guid ClsImage = new("6bdd1fc6-810f-11d0-bec7-08002be2092f");

    public enum Device { Bluetooth, Webcam, Touchpad }

    sealed record Dev(uint DevInst, Guid Class, string Id, string[] HwIds);

    static List<Dev> AllDevices()
    {
        var res = new List<Dev>();
        nint set = SetupDiGetClassDevsW(0, null, 0, DigcfPresent | DigcfAllClasses);
        if (set == -1) return res;
        try
        {
            var d = new DevInfo { Size = Marshal.SizeOf<DevInfo>() };
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                var sb = new StringBuilder(512);
                SetupDiGetDeviceInstanceIdW(set, ref d, sb, sb.Capacity, out _);
                var buf = new byte[2048];
                string[] hw = SetupDiGetDeviceRegistryPropertyW(set, ref d, SpdrpHardwareId, out _, buf, buf.Length, out int req)
                    ? Encoding.Unicode.GetString(buf, 0, Math.Max(0, req)).Split('\0', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                res.Add(new Dev(d.DevInst, d.ClassGuid, sb.ToString(), hw));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return res;
    }

    /// <summary>Dispositivos físicos que representam cada opção.</summary>
    static List<uint> Targets(Device dev)
    {
        var all = AllDevices();
        switch (dev)
        {
            case Device.Bluetooth:
                return all.Where(d => d.Class == ClsBluetooth && (d.Id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) || d.Id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))).Select(d => d.DevInst).ToList();
            case Device.Webcam:
                return all.Where(d => d.Class == ClsCamera || (d.Class == ClsImage && d.Id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))).Select(d => d.DevInst).ToList();
            default:
                // touchpad de precisão: a coleção HID "touch pad" (UP:000D U:0005); desativa o dispositivo HID I2C pai
                var tp = all.Where(d => d.HwIds.Any(h => h.Equals("HID_DEVICE_UP:000D_U:0005", StringComparison.OrdinalIgnoreCase))).ToList();
                var parents = new List<uint>();
                foreach (var t in tp) if (CM_Get_Parent(out var p, t.DevInst, 0) == 0 && !parents.Contains(p)) parents.Add(p);
                return parents;
        }
    }

    /// <summary>null = dispositivo não encontrado.</summary>
    public static bool? DeviceOn(Device dev)
    {
        var t = Targets(dev);
        if (t.Count == 0) return null;
        return t.Any(i => CM_Get_DevNode_Status(out var st, out var prob, i, 0) == 0 && prob != 22 /* CM_PROB_DISABLED */);
    }

    public static void SetDevice(Device dev, bool on)
    {
        var targets = Targets(dev);
        nint set = SetupDiGetClassDevsW(0, null, 0, DigcfPresent | DigcfAllClasses);
        if (set == -1) return;
        try
        {
            var d = new DevInfo { Size = Marshal.SizeOf<DevInfo>() };
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                if (!targets.Contains(d.DevInst)) continue;
                var p = new PropChange { HeaderSize = 8, InstallFunction = DifPropertyChange, StateChange = on ? 1 : 2, Scope = 1, HwProfile = 0 };
                if (SetupDiSetClassInstallParamsW(set, ref d, ref p, Marshal.SizeOf<PropChange>()))
                    SetupDiCallClassInstaller(DifPropertyChange, set, ref d);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    // ================= OSD do serviço do fabricante =================
    const string OemKey = @"SOFTWARE\OEM\GamingCenter2\MySetting";

    public static bool OsdHidden
    {
        get { using var k = Registry.LocalMachine.OpenSubKey(OemKey); return (k?.GetValue("OSDHidden") as int? ?? 0) != 0; }
        set { using var k = Registry.LocalMachine.CreateSubKey(OemKey); k.SetValue("OSDHidden", value ? 1 : 0, RegistryValueKind.DWord); }
    }

    // ================= Taxa de atualização =================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra; public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels; public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string? dev, int mode, ref DevMode dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string? dev, ref DevMode dm, nint hwnd, int flags, nint param);

    static string Primary => Screen.PrimaryScreen!.DeviceName;

    static DevMode Current()
    {
        var dm = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        EnumDisplaySettingsW(Primary, -1, ref dm);
        return dm;
    }

    public static int CurrentHz => Current().DisplayFrequency;

    /// <summary>Taxas disponíveis na resolução atual da tela principal.</summary>
    public static int[] AvailableHz()
    {
        var cur = Current(); var set = new SortedSet<int>();
        var dm = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        for (int i = 0; EnumDisplaySettingsW(Primary, i, ref dm); i++)
            if (dm.PelsWidth == cur.PelsWidth && dm.PelsHeight == cur.PelsHeight && dm.BitsPerPel == cur.BitsPerPel && dm.DisplayFrequency > 1) set.Add(dm.DisplayFrequency);
        return set.ToArray();
    }

    public static bool SetHz(int hz)
    {
        var dm = Current();
        dm.DisplayFrequency = hz; dm.Fields = 0x400000; // DM_DISPLAYFREQUENCY
        return ChangeDisplaySettingsExW(Primary, ref dm, 0, 1 /* CDS_UPDATEREGISTRY */, 0) == 0;
    }
}

/// <summary>Hook de teclado de baixo nível para bloquear o teclado numérico (a tecla Windows é bloqueada no EC).</summary>
sealed class KeyBlocker : IDisposable
{
    delegate nint HookProc(int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern nint SetWindowsHookExW(int id, HookProc proc, nint mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint h, int code, nint w, nint l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandleW(string? name);

    readonly HookProc _proc;
    nint _hook;
    bool _num;

    public KeyBlocker() { _proc = Proc; }

    public bool BlockNumpad { get => _num; set { _num = value; Update(); } }

    void Update()
    {
        bool need = _num;
        if (need && _hook == 0) _hook = SetWindowsHookExW(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandleW(null), 0);
        else if (!need && _hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
    }

    nint Proc(int code, nint w, nint l)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(l), sc = Marshal.ReadInt32(l + 4), flags = Marshal.ReadInt32(l + 8);
            bool ext = (flags & 1) != 0;
            if (_num && IsNumpad(vk, sc, ext)) return 1;
        }
        return CallNextHookEx(_hook, code, w, l);
    }

    // Teclas do bloco numérico: scancodes 0x47–0x53 sem flag "estendida" (as setas/Home do bloco central são estendidas),
    // mais * (0x37), - (0x4A), + (0x4E), Num Lock, / e Enter do numérico (estendidos).
    static bool IsNumpad(int vk, int sc, bool ext) =>
        (!ext && sc is >= 0x47 and <= 0x53) || (!ext && sc == 0x37) || vk == 0x90 || (ext && sc == 0x35) || (ext && sc == 0x1C);

    public void Dispose() { if (_hook != 0) UnhookWindowsHookEx(_hook); _hook = 0; }
}
