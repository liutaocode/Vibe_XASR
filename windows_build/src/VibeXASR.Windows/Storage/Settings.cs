using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeXASR.Windows.Storage;

/// <summary>Dictation insertion behaviour. Mirrors the macOS app's three modes.</summary>
public enum DictationMode
{
    /// <summary>Insert the whole recognized result once, on hotkey release.</summary>
    Paste,

    /// <summary>Stream characters to the caret as they are recognized (with backspace diffing).</summary>
    Type,

    /// <summary>Always-on, VAD-segmented; overlay shows live text, user copies manually.</summary>
    OnCall,
}

/// <summary>Streaming model tier (chunk size in ms). Larger = more accurate, more latency.</summary>
public enum ModelTier
{
    Ms160 = 160,
    Ms480 = 480,
    Ms960 = 960,
    Ms1920 = 1920,
}

/// <summary>VAD backend choice.</summary>
public enum VadKind
{
    Silero,
    FireRed,
}

/// <summary>
/// Persisted user settings. Serialized to %APPDATA%/VibeXASR/settings.json.
/// Keep this a plain DTO so System.Text.Json round-trips it cleanly.
/// </summary>
public sealed class Settings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DictationMode Mode { get; set; } = DictationMode.Paste;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModelTier Tier { get; set; } = ModelTier.Ms960; // 960ms default, matches macOS.

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VadKind Vad { get; set; } = VadKind.Silero;

    /// <summary>
    /// Push-to-talk key. Stored as a Win32 virtual-key code (VK_*).
    /// Default = Right Ctrl (VK_RCONTROL = 0xA3). TODO(win): confirm the default
    /// feels right on a real keyboard; some users prefer a function key (e.g. F8 = 0x77).
    /// </summary>
    public int HotkeyVk { get; set; } = 0xA3;

    /// <summary>If true, the OnCall overlay starts automatically at launch.</summary>
    public bool OnCallAutoStart { get; set; } = false;

    /// <summary>UI language code (en, zh, ja, ko). Skeleton ships "en" only.</summary>
    public string Language { get; set; } = "en";

    // ---- persistence ----

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string FilePath =>
        Path.Combine(AppPaths.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (s is not null) return s;
            }
        }
        catch
        {
            // Corrupt settings -> fall back to defaults rather than crash the tray.
            // TODO(win): log to %APPDATA%/VibeXASR/log.txt for diagnosis.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var json = JsonSerializer.Serialize(this, JsonOpts);
        // Write-then-rename for crash safety.
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        File.Delete(tmp);
    }
}

/// <summary>Shared %APPDATA%/VibeXASR resolution.</summary>
public static class AppPaths
{
    public static string DataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VibeXASR");
}
