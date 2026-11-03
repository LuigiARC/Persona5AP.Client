using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace P5RArchipelago.Configuration;

/// <summary>Native-menu settings; credentials stay in memory, not in the saved profile.</summary>
internal sealed class ConnectionSettings
{
    public string Host { get; set; } = "localhost:38281";
    public string Slot { get; set; } = "Joker";
    public bool AutoConnect { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Password { get; set; } = "";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static ConnectionSettings Load(string path, Config legacy, Action<string> log)
    {
        if (File.Exists(path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(path));
                if (saved is not null && IsValid(saved.Host, saved.Slot)) return saved;
                log("Saved connection settings are invalid; using legacy settings.");
            }
            catch { log("Could not read saved connection settings; using legacy settings."); }
        }
        return new() { Host = legacy.ServerAddress, Slot = legacy.SlotName,
            Password = legacy.ServerPassword, AutoConnect = legacy.ConnectOnStartup };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".new", JsonSerializer.Serialize(this, Json));
        File.Move(path + ".new", path, true);
    }

    public static bool IsValid(string host, string slot)
    {
        if (host.Length is < 1 or > 256 || slot.Length is < 1 or > 16 ||
            host.Any(char.IsWhiteSpace) || host.Any(char.IsControl) || slot.Any(char.IsControl)) return false;
        var url = host.Contains("://", StringComparison.Ordinal) ? host : "ws://" + host;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "ws" or "wss" && !string.IsNullOrEmpty(uri.Host) &&
            uri.Port is > 0 and <= 65535 && string.IsNullOrEmpty(uri.UserInfo);
    }

    public string ProfileKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Host + "\n" + Slot)));
}

internal sealed class ConnectionFieldEditor
{
    public const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .:-_/@[]!?#$%&*()+,=;<>\\\"'{}|~`^";
    public int Field { get; private set; }
    public string Draft { get; private set; } = "";
    public string Display {
        get {
            var text = Field == 3 ? new string('*', Draft.Length) : Draft;
            // Keep the typing cursor end visible within the native menu header.
            return text.Length == 0 ? "(empty)" : text.Length > 48 ? "..." + text[^48..] : text;
        }
    }
    public void Begin(int field, string value) { Field = field; Draft = value; }
    public bool Append(int index)
    {
        var limit = Field == 2 ? 16 : 256;
        if (index < 0 || index >= Alphabet.Length || Draft.Length >= limit) return false;
        Draft += Alphabet[index];
        return true;
    }
    public void Backspace() { if (Draft.Length > 0) Draft = Draft[..^1]; }
    public void Clear() => Draft = "";
}
