using System.Text.Json;

namespace LiteDeck;

public sealed class Settings
{
    public int KbR { get; set; } = 255;
    public int KbG { get; set; } = 255;
    public int KbB { get; set; } = 255;
    /// <summary>Nível de brilho 0..4 (0 = apagado).</summary>
    public int KbLevel { get; set; } = 3;
    public bool KbApply { get; set; } = true;
    public FanMode Fan { get; set; } = FanMode.Default;
    public int FanFixed { get; set; } = 60;
    public bool FanBoost { get; set; }
    // opções do EC: null = nunca mexido pelo app (não reaplica na inicialização)
    public bool? WinKeyLock { get; set; }
    public bool? FnLock { get; set; }
    public bool? UsbCharging { get; set; }
    public bool? AcPowerOn { get; set; }
    public bool NumpadLock { get; set; }
    public bool ApplyFansOnStart { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public bool IsNew { get; private set; }

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteDeck");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new Settings { IsNew = true };
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
