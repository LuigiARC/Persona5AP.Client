using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace P5RArchipelago;

internal sealed class TvBundleCatalog
{
    private const string ResourceName = "P5RArchipelago.tv_bundles.json";
    private readonly Dictionary<long, TvBundle> _byItemId;

    public IReadOnlyList<TvBundle> Bundles { get; }
    public IReadOnlyList<TvBundle> NativeItems { get; }
    public int Version { get; }
    public string Hash { get; }
    public string StateIdentityHash { get; }

    private TvBundleCatalog(int version, string hash, string stateIdentityHash, IReadOnlyList<TvBundle> bundles,
        IReadOnlyList<TvBundle> nativeItems)
    {
        Version = version;
        Hash = hash;
        StateIdentityHash = stateIdentityHash;
        Bundles = bundles;
        NativeItems = nativeItems;
        _byItemId = bundles.Concat(nativeItems).ToDictionary(bundle => bundle.ItemId);
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
            document.Bundles.Concat(document.NativeItems).Select(bundle => bundle.ItemId).Distinct().Count()
                != document.Bundles.Count + document.NativeItems.Count ||
            document.NativeItems.Any(item => item.Contents.Count != 1 ||
                item.Contents[0].Count is < 1 or > 99))
            throw new InvalidDataException("The embedded inventory catalog is empty, contains duplicate item IDs, or has invalid starting-item contents.");
        // This correction changes labels only. Retain the previous name's exact
        // fingerprint for existing seed compatibility and durable receipt profiles.
        var legacyBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
            .Replace("\"name\": \"Perma-Pick\"", "\"name\": \"Eternal Lockpick\"")
            .Replace("\"native_name\": \"Perma-Pick\"", "\"native_name\": \"Eternal Lockpick\""));
        return new TvBundleCatalog(document.CatalogVersion,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(legacyBytes)).ToLowerInvariant(), document.Bundles, document.NativeItems);
    }

    public bool TryGet(long itemId, out TvBundle bundle) => _byItemId.TryGetValue(itemId, out bundle!);
    public bool AcceptsHash(string? hash) => string.Equals(hash, Hash, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(hash, StateIdentityHash, StringComparison.OrdinalIgnoreCase);
}

internal sealed class TvBundleDocument
{
    [JsonPropertyName("catalog_version")]
    public int CatalogVersion { get; set; }

    [JsonPropertyName("bundles")]
    public List<TvBundle> Bundles { get; set; } = [];

    [JsonPropertyName("native_items")]
    public List<TvBundle> NativeItems { get; set; } = [];
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
