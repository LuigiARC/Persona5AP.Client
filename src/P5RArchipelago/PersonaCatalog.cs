using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P5RArchipelago;

internal sealed class PersonaCatalog
{
    public int Version { get; }
    public string Hash { get; }
    public IReadOnlyList<PersonaLocation> Locations { get; }
    private readonly Dictionary<int, PersonaLocation> _byPersonaId;

    private PersonaCatalog(PersonaCatalogDocument document, string hash)
    {
        Version = document.CatalogVersion;
        Hash = hash;
        Locations = document.Locations;
        _byPersonaId = Locations.ToDictionary(location => location.PersonaId);
    }

    public bool TryGetByPersonaId(int personaId, out PersonaLocation location) =>
        _byPersonaId.TryGetValue(personaId, out location!);

    public static PersonaCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("P5RArchipelago.personas.json")
            ?? throw new InvalidOperationException("Embedded personas.json is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var document = JsonSerializer.Deserialize<PersonaCatalogDocument>(bytes)
            ?? throw new InvalidOperationException("personas.json could not be parsed.");
        if (document.CatalogVersion != 1 || document.Locations.Count != 232)
            throw new InvalidOperationException("Unsupported Persona catalog shape.");
        return new PersonaCatalog(document, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}

internal sealed class PersonaCatalogDocument
{
    [JsonPropertyName("catalog_version")]
    public int CatalogVersion { get; set; }

    [JsonPropertyName("locations")]
    public List<PersonaLocation> Locations { get; set; } = [];
}

internal sealed class PersonaLocation
{
    [JsonPropertyName("persona_id")]
    public int PersonaId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("location_id")]
    public long LocationId { get; set; }
}
