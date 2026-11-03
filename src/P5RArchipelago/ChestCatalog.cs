using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P5RArchipelago;

internal sealed class ChestCatalog
{
    private const string ResourceName = "P5RArchipelago.chests.json";
    private readonly Dictionary<uint, ChestLocation> _byFlag;

    public int Version { get; }
    public string Hash { get; }
    public IReadOnlyList<ChestLocation> Locations { get; }

    private ChestCatalog(int version, string hash, IReadOnlyList<ChestLocation> locations)
    {
        Version = version;
        Hash = hash;
        Locations = locations;
        _byFlag = locations.ToDictionary(location => location.Flag);
    }

    public static ChestCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded chest catalog '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var document = JsonSerializer.Deserialize<ChestCatalogDocument>(bytes)
            ?? throw new InvalidDataException("The embedded chest catalog is empty.");

        if (document.Locations.Count == 0)
            throw new InvalidDataException("The embedded chest catalog has no locations.");
        if (document.Locations.Select(location => location.Flag).Distinct().Count() != document.Locations.Count)
            throw new InvalidDataException("The embedded chest catalog contains duplicate flags.");
        if (document.Locations.Select(location => location.LocationId).Distinct().Count() != document.Locations.Count)
            throw new InvalidDataException("The embedded chest catalog contains duplicate location IDs.");

        return new ChestCatalog(
            document.CatalogVersion,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            document.Locations);
    }

    public bool TryGetByFlag(uint flag, out ChestLocation location) => _byFlag.TryGetValue(flag, out location!);

    // Use the catalog's explicit field major, not translated names or substring matches.
    public long[] PalaceChestIds(int major) => major <= 0 ? [] : Locations
        .Where(location => location.Major == major)
        .Select(location => location.LocationId)
        .ToArray();
}

internal sealed class ChestCatalogDocument
{
    [JsonPropertyName("catalog_version")]
    public int CatalogVersion { get; set; }

    [JsonPropertyName("locations")]
    public List<ChestLocation> Locations { get; set; } = [];
}

internal sealed class ChestLocation
{
    [JsonPropertyName("major")]
    public int Major { get; set; }

    [JsonPropertyName("flag")]
    public uint Flag { get; set; }

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("slot")]
    public int Slot { get; set; }
}
