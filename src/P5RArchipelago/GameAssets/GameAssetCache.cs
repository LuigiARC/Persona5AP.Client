using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CriFs.V2.Hook.Interfaces;
using CriFsV2Lib.Definitions;

namespace P5RArchipelago.GameAssets;

internal sealed class GameAssetCache
{
    internal const string HubRelativePath = "en.cpk/script/field/FSCR8000_002_000.BF";
    public string DirectoryPath { get; }
    public string HubPath => LocalPath(DirectoryPath, HubRelativePath);
    public bool Reused { get; }
    private readonly PatchEntry[] _entries;

    private GameAssetCache(string directory, PatchEntry[] entries, bool reused)
    {
        DirectoryPath = directory;
        _entries = entries;
        Reused = reused;
    }

    public static GameAssetCache Prepare(string modDirectory, string userDirectory, ICriFsLib library,
        IEnumerable<string> cpkPaths)
    {
        var patchDirectory = Path.Combine(modDirectory, "patches");
        var manifestBytes = File.ReadAllBytes(Path.Combine(patchDirectory, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<PatchManifest>(manifestBytes)
            ?? throw new InvalidDataException("Missing Persona 5 AP patch manifest.");
        if (manifest.Format != 2 || manifest.Files.Length == 0)
            throw new InvalidDataException("Unsupported Persona 5 AP patch manifest.");
        var entries = manifest.Files;
        var paths = cpkPaths.ToArray();
        var archives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var patches = new Dictionary<string, byte[]>();
        var stamp = new StringBuilder(Hash(manifestBytes));
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ValidateRelativePath(entry.Path);
            if (!routes.Add(entry.Path) || Path.GetFileName(entry.Patch) != entry.Patch ||
                entry.Patch.Contains('\\') || entry.Patch.Contains(':') ||
                !entry.Patch.EndsWith(".p5apdiff", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid or duplicate asset patch path.");
            var patch = File.ReadAllBytes(Path.Combine(patchDirectory, entry.Patch));
            Verify(patch, entry.PatchSha256, $"Patch {entry.Patch}");
            patches[entry.Path] = patch;
            var archive = entry.Path.Split('/')[0];
            if (archives.ContainsKey(archive)) continue;
            var matches = paths.Where(p => Path.GetFileName(p).Equals(archive, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Cannot uniquely locate {archive} in the installed game.");
            archives.Add(archive, matches[0]);
            var info = new FileInfo(matches[0]);
            stamp.Append('|').Append(info.FullName).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
        }
        if (!routes.Contains(HubRelativePath))
            throw new InvalidDataException("Persona 5 AP hub patch is missing.");
        var cacheRoot = Path.Combine(userDirectory, "GameAssets");
        var cacheDirectory = Path.Combine(cacheRoot, Hash(Encoding.UTF8.GetBytes(stamp.ToString())));
        if (entries.All(e => IsValid(LocalPath(cacheDirectory, e.Path), e.ModifiedSha256)))
            return new GameAssetCache(cacheDirectory, entries, true);

        // Read only the required entries. Keep every original CPK open read-only.
        // Validate all inputs/outputs before publishing anything to the usable cache.
        var pending = new Dictionary<string, byte[]>();
        foreach (var group in entries.GroupBy(e => e.Path.Split('/')[0]))
        {
            using var stream = File.OpenRead(archives[group.Key]);
            using var reader = library.CreateCpkReader(stream, false,
                library.GetKnownDecryptionFunction(KnownDecryptionFunction.P5R));
            var files = reader.GetFiles().ToDictionary(
                f => Normalize($"{f.Directory}/{f.FileName}").TrimStart('/'), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
            {
                var relative = entry.Path[(entry.Path.IndexOf('/') + 1)..];
                if (!files.TryGetValue(relative, out var file))
                    throw new InvalidDataException($"Required game file was not found: {entry.Path}");
                using var extracted = reader.ExtractFile(file);
                var original = extracted.Span.ToArray();
                Verify(original, entry.OriginalSha256, $"Game file {entry.Path}; unsupported or modified game version");
                var modified = BinaryDelta.Apply(original, patches[entry.Path]);
                Verify(modified, entry.ModifiedSha256, $"Prepared game file {entry.Path}");
                pending.Add(entry.Path, modified);
            }
        }
        Directory.CreateDirectory(cacheRoot);
        var staging = Path.Combine(cacheRoot, ".preparing-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (relative, bytes) in pending)
            {
                var path = LocalPath(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            foreach (var relative in pending.Keys)
            {
                var destination = LocalPath(cacheDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(LocalPath(staging, relative), destination, true);
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        return new GameAssetCache(cacheDirectory, entries, false);
    }

    public void Bind(ICriFsRedirectorApi.BindContext context, string modId)
    {
        foreach (var entry in _entries)
        {
            var relative = entry.Path[(entry.Path.IndexOf('/') + 1)..];
            var route = Path.Combine("R2", relative.Replace('/', Path.DirectorySeparatorChar));
            var path = LocalPath(DirectoryPath, entry.Path);
            // The CRI hook uses the last candidate as the replacement file.
            if (!context.RelativePathToFileMap.TryGetValue(route, out var candidates))
                context.RelativePathToFileMap[route] = candidates = [];
            candidates.Add(new ICriFsRedirectorApi.BindFileInfo
            {
                FullPath = path, LastWriteTime = File.GetLastWriteTime(path), ModId = modId,
            });
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
    private static string LocalPath(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static void Verify(byte[] data, string expected, string description)
    {
        if (!Hash(data).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{description}: checksum mismatch.");
    }
    private static bool IsValid(string path, string expected) => File.Exists(path) &&
        Hash(File.ReadAllBytes(path)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static void ValidateRelativePath(string path)
    {
        var parts = path.Split('/');
        if (parts.Length < 2 || (parts[0] != "base.cpk" && parts[0] != "en.cpk") ||
            parts.Any(p => p.Length == 0 || p is "." or ".." || p.Contains('\\') || p.Contains(':')))
            throw new InvalidDataException("Invalid game asset path.");
    }

    private sealed class PatchManifest
    {
        [JsonPropertyName("format")] public int Format { get; set; }
        [JsonPropertyName("files")] public PatchEntry[] Files { get; set; } = [];
    }
    private sealed class PatchEntry
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("patch")] public string Patch { get; set; } = "";
        [JsonPropertyName("original_sha256")] public string OriginalSha256 { get; set; } = "";
        [JsonPropertyName("modified_sha256")] public string ModifiedSha256 { get; set; } = "";
        [JsonPropertyName("patch_sha256")] public string PatchSha256 { get; set; } = "";
    }
}
