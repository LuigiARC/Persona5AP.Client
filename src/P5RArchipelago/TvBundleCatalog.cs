using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P5RArchipelago;

internal sealed class TvBundleCatalog
{
    private const string ResourceName = "P5RArchipelago.tv_bundles.json";
    private readonly Dictionary<long, TvBundle> _byItemId;

    public IReadOnlyList<TvBundle> Bundles { get; }
    public int Version { get; }
    public string Hash { get; }

    private TvBundleCatalog(int version, string hash, IReadOnlyList<TvBundle> bundles)
    {
        Version = version;
        Hash = hash;
        Bundles = bundles;
        _byItemId = bundles.ToDictionary(bundle => bundle.ItemId);
    }

    public static TvBundleCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded TV bundle catalog '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var document = JsonSerializer.Deserialize<TvBundleDocument>(bytes)
            ?? throw new InvalidDataException("The embedded TV bundle catalog is empty.");
        if (document.Bundles.Count == 0 ||
            document.Bundles.Select(bundle => bundle.ItemId).Distinct().Count() != document.Bundles.Count)
            throw new InvalidDataException("The embedded TV bundle catalog is empty or contains duplicate item IDs.");
        return new TvBundleCatalog(document.CatalogVersion,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), document.Bundles);
    }

    public bool TryGet(long itemId, out TvBundle bundle) => _byItemId.TryGetValue(itemId, out bundle!);
}

internal sealed class TvBundleDocument
{
    [JsonPropertyName("catalog_version")]
    public int CatalogVersion { get; set; }

    [JsonPropertyName("bundles")]
    public List<TvBundle> Bundles { get; set; } = [];
}

internal sealed class TvBundle
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }

    [JsonPropertyName("contents")]
    public List<TvBundleContent> Contents { get; set; } = [];
}

internal sealed class TvBundleContent
{
    [JsonPropertyName("item_id")]
    public ushort ItemId { get; set; }

    [JsonPropertyName("count")]
    public byte Count { get; set; }
}
