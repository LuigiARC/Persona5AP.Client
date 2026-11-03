using System.Text.Json;

namespace P5RArchipelago.Configuration;

public sealed class Config
{
    public string ServerAddress { get; set; } = "localhost:38281";
    public string ServerPassword { get; set; } = "";
    public string SlotName { get; set; } = "Joker";
    public bool ConnectOnStartup { get; set; } = false;
    public bool LogAllChestFlags { get; set; } = true;
    public bool ReconcileOpenedChests { get; set; } = true;
    /// <summary>
    /// Opens the Archipelago hub from any safe field after a save has been loaded. Supported
    /// names are F1-F12, Insert, Home, End, PageUp, PageDown, and Pause. Set this to an empty
    /// string to disable the shortcut.
    /// </summary>
    public string HubMenuHotkey { get; set; } = "F9";

    public static Config LoadOrCreate(string path, Action<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var created = new Config();
            File.WriteAllText(path, JsonSerializer.Serialize(created, JsonOptions));
            log($"Created configuration: {path}");
            return created;
        }

        try
        {
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions) ?? new Config();
        }
        catch (Exception ex)
        {
            log($"Could not read Config.json; defaults will be used. {ex.Message}");
            return new Config();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
}
