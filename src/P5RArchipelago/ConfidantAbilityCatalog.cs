using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P5RArchipelago;

internal sealed class ConfidantAbilityCatalog
{
    private const string ResourceName = "P5RArchipelago.confidant_abilities.json";
    private readonly Dictionary<(ushort CmmId, short Rank), List<ConfidantAbilityLocation>> _byRank;

    public int Version { get; }
    public string Hash { get; }
    public IReadOnlyList<ConfidantAbilityLocation> Locations { get; }

    private ConfidantAbilityCatalog(ConfidantAbilityDocument document, string hash)
    {
        Version = document.CatalogVersion;
        Hash = hash;
        Locations = document.Locations;
        _byRank = [];
        foreach (var location in Locations)
        foreach (var cmmId in location.TriggerCmmIds)
        {
            var key = (cmmId, location.Rank);
            if (!_byRank.TryGetValue(key, out var matches))
                _byRank[key] = matches = [];
            matches.Add(location);
        }
    }

    public IReadOnlyList<ConfidantAbilityLocation> AtRank(ushort cmmId, short rank) =>
        _byRank.TryGetValue((cmmId, rank), out var matches) ? matches : [];

    public short NextCheckRank(ushort cmmId, short currentRank) =>
        _byRank.Keys
            .Where(key => key.CmmId == cmmId && key.Rank > currentRank)
            .Select(key => key.Rank)
            .DefaultIfEmpty((short)0)
            .Min();

    public static ConfidantAbilityCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded confidant catalog '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var document = JsonSerializer.Deserialize<ConfidantAbilityDocument>(bytes)
            ?? throw new InvalidOperationException("Embedded confidant catalog is invalid.");
        return new ConfidantAbilityCatalog(document,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}

internal sealed class ConfidantAbilityDocument
{
    [JsonPropertyName("catalog_version")]
    public int CatalogVersion { get; set; }

    [JsonPropertyName("locations")]
    public List<ConfidantAbilityLocation> Locations { get; set; } = [];
}

internal sealed class ConfidantAbilityLocation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arcana")]
    public string Arcana { get; set; } = "";

    [JsonPropertyName("rank")]
    public short Rank { get; set; }

    [JsonPropertyName("feature_id")]
    public uint FeatureId { get; set; }

    [JsonPropertyName("trigger_cmm_ids")]
    public List<ushort> TriggerCmmIds { get; set; } = [];

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }
}
