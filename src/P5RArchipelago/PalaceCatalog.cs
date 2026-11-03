using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P5RArchipelago;

internal sealed class PalaceCatalog
{
    private const string ResourceName = "P5RArchipelago.palaces.json";
    private readonly Dictionary<uint, PalaceLocation> _byClearFlag;
    private readonly Dictionary<uint, PalaceLocation> _byCallingCardFlag;
    private readonly Dictionary<uint, PalaceLocation> _byAccessSyntheticFeatureId;
    private readonly Dictionary<uint, PalaceLocation> _byCallingCardSyntheticFeatureId;
    private readonly Dictionary<long, PalaceLocation> _byMergedAccessItemId;

    public int Version { get; }
    public string Hash { get; }
    public long ProgressiveAccessItemId { get; }
    public long ProgressiveCallingCardItemId { get; }
    public IReadOnlyList<PalaceLocation> Locations { get; }

    private PalaceCatalog(PalaceDocument document, string hash)
    {
        Version = document.CatalogVersion;
        Hash = hash;
        ProgressiveAccessItemId = document.ProgressiveAccessItemId;
        ProgressiveCallingCardItemId = document.ProgressiveCallingCardItemId;
        Locations = document.Locations;
        _byClearFlag = Locations.ToDictionary(location => location.ClearFlag);
        _byCallingCardFlag = Locations.ToDictionary(location => location.CallingCardFlag);
        _byAccessSyntheticFeatureId = Locations.ToDictionary(location => location.AccessSyntheticFeatureId);
        _byCallingCardSyntheticFeatureId = Locations.ToDictionary(location => location.CallingCardSyntheticFeatureId);
        _byMergedAccessItemId = Locations.ToDictionary(location => location.MergedAccessItemId);
    }

    public bool TryGetByClearFlag(uint flag, out PalaceLocation location) =>
        _byClearFlag.TryGetValue(flag, out location!);

    public bool TryGetByCallingCardFlag(uint flag, out PalaceLocation location) =>
        _byCallingCardFlag.TryGetValue(flag, out location!);

    public bool TryGetByAccessSyntheticFeatureId(uint featureId, out PalaceLocation location) =>
        _byAccessSyntheticFeatureId.TryGetValue(featureId, out location!);

    public bool TryGetByCallingCardSyntheticFeatureId(uint featureId, out PalaceLocation location) =>
        _byCallingCardSyntheticFeatureId.TryGetValue(featureId, out location!);

    public bool TryGetByMergedAccessItemId(long itemId, out PalaceLocation location) =>
        _byMergedAccessItemId.TryGetValue(itemId, out location!);

    public static PalaceCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Palace catalog '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var document = JsonSerializer.Deserialize<PalaceDocument>(bytes)
            ?? throw new InvalidOperationException("Embedded Palace catalog is invalid.");
        return new PalaceCatalog(document, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}

internal sealed class PalaceDocument
{
    [JsonPropertyName("catalog_version")]
    public int CatalogVersion { get; set; }

    [JsonPropertyName("progressive_access_item_id")]
    public long ProgressiveAccessItemId { get; set; }

    [JsonPropertyName("progressive_calling_card_item_id")]
    public long ProgressiveCallingCardItemId { get; set; }

    [JsonPropertyName("locations")]
    public List<PalaceLocation> Locations { get; set; } = [];
}

internal sealed class PalaceLocation
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }

    [JsonPropertyName("major")]
    public int Major { get; set; }

    [JsonPropertyName("entry_minor")]
    public int EntryMinor { get; set; }

    [JsonPropertyName("entry_position")]
    public int EntryPosition { get; set; }

    [JsonPropertyName("calling_card_flag")]
    public uint CallingCardFlag { get; set; }

    [JsonPropertyName("calling_card_name")]
    public string CallingCardName { get; set; } = "";

    [JsonPropertyName("calling_card_location_id")]
    public long CallingCardLocationId { get; set; }

    [JsonPropertyName("clear_flag")]
    public uint ClearFlag { get; set; }

    [JsonPropertyName("access_synthetic_feature_id")]
    public uint AccessSyntheticFeatureId { get; set; }

    [JsonPropertyName("calling_card_synthetic_feature_id")]
    public uint CallingCardSyntheticFeatureId { get; set; }

    /// <summary>1-based story-order tier: granted once the player holds this many copies of the
    /// progressive access (or calling card) item -- see <see cref="PalaceCatalog.ProgressiveAccessItemId"/>.</summary>
    [JsonPropertyName("progressive_index")]
    public int ProgressiveIndex { get; set; }

    /// <summary>Used instead of the progressive items when the apworld's
    /// 'progressive_palace_access' option is off: receiving this single item grants both
    /// entering this Palace and sending its calling card, independent of story order.</summary>
    [JsonPropertyName("merged_access_item_id")]
    public long MergedAccessItemId { get; set; }
}
