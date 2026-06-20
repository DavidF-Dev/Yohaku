using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yohaku;

/// <summary>User configuration — the per-edge margins, persisted as JSON.</summary>
public sealed class Config
{
    /// <summary>Margin reserved on each edge of every monitor, in logical (96-DPI) pixels.</summary>
    public int InsetTop { get; set; } = 12;
    public int InsetRight { get; set; } = 12;
    public int InsetBottom { get; set; } = 12;
    public int InsetLeft { get; set; } = 12;

    // ---- Persistence ---------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yohaku");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = Deserialize(File.ReadAllText(ConfigPath));
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to load config, using defaults: {ex.Message}");
        }

        var fresh = new Config();
        fresh.Save(); // materialise a default file so the user can edit it
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, Serialize(this));
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to save config: {ex.Message}");
        }
    }

    // Pure (de)serialization, separated from file I/O so it can be unit-tested.
    internal static string Serialize(Config cfg) => JsonSerializer.Serialize(cfg, JsonOpts);

    internal static Config? Deserialize(string json) => JsonSerializer.Deserialize<Config>(json, JsonOpts);
}
