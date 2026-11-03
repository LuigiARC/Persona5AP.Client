using System.Diagnostics;
using System.Text.Json.Nodes;
using CriFs.V2.Hook.Interfaces;
using CriFsV2Lib;
using P5RArchipelago.GameAssets;

if (args.Length < 2)
    throw new ArgumentException("Usage: GameAssetSmoke <mod-folder> <game-CPK-folder> [private-expected-CPK-folder]");
var mod = Path.GetFullPath(args[0]);
var archives = Directory.GetFiles(Path.GetFullPath(args[1])).Where(p =>
    Path.GetFileName(p).Equals("BASE.CPK", StringComparison.OrdinalIgnoreCase) ||
    Path.GetFileName(p).Equals("EN.CPK", StringComparison.OrdinalIgnoreCase)).ToArray();
var originalStats = archives.Select(p => (p, new FileInfo(p).Length, File.GetLastWriteTimeUtc(p))).ToArray();
var temp = Path.Combine(Path.GetTempPath(), "persona5-ap-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
void ExpectFailure(Action action, string message)
{
    try { action(); }
    catch (InvalidDataException) { Check(true, message); return; }
    throw new Exception("Expected failure: " + message);
}
string CopyPatchSet(string name)
{
    var target = Path.Combine(temp, name);
    Directory.CreateDirectory(Path.Combine(target, "patches"));
    foreach (var file in Directory.GetFiles(Path.Combine(mod, "patches")))
        File.Copy(file, Path.Combine(target, "patches", Path.GetFileName(file)));
    return target;
}
try
{
    var watch = Stopwatch.StartNew();
    var cache = GameAssetCache.Prepare(mod, Path.Combine(temp, "user"), CriFsLib.Instance, archives);
    Console.WriteLine($"Cold preparation from original CPKs: {watch.ElapsedMilliseconds} ms");
    Check(!cache.Reused && File.Exists(cache.HubPath), "first launch prepares the hub directly from installed CPKs");
    var entries = JsonNode.Parse(File.ReadAllText(Path.Combine(mod, "patches/manifest.json")))!["files"]!.AsArray();
    if (args.Length > 2)
    {
        foreach (var entry in entries)
        {
            var relative = entry!["path"]!.GetValue<string>();
            Check(File.ReadAllBytes(Path.Combine(cache.DirectoryPath, relative)).SequenceEqual(
                File.ReadAllBytes(Path.Combine(args[2], relative))), "matches known working override: " + relative);
        }
    }
    watch.Restart();
    var warm = GameAssetCache.Prepare(mod, Path.Combine(temp, "user"), CriFsLib.Instance, archives);
    Console.WriteLine($"Warm cache verification: {watch.ElapsedMilliseconds} ms");
    Check(warm.Reused && warm.DirectoryPath == cache.DirectoryPath, "subsequent launch reuses verified cache");
    File.WriteAllBytes(cache.HubPath, [1, 2, 3]);
    var repaired = GameAssetCache.Prepare(mod, Path.Combine(temp, "user"), CriFsLib.Instance, archives);
    Check(!repaired.Reused && File.ReadAllBytes(repaired.HubPath).Length > 3, "corrupted generated cache is rebuilt automatically");

    var map = new Dictionary<string, List<ICriFsRedirectorApi.BindFileInfo>>(StringComparer.OrdinalIgnoreCase);
    var hubRoute = Path.Combine("R2", "script", "field", "FSCR8000_002_000.BF");
    map[hubRoute] = [new() { FullPath = "earlier-mod" }];
    repaired.Bind(new() { BindDirectory = temp, RelativePathToFileMap = map }, "persona5-ap-smoke");
    Check(map.Count == entries.Count && map[hubRoute].Last().FullPath == repaired.HubPath,
        "CRI bind callback registers all 25 cache files with the expected route and priority");
    Check(map.Values.SelectMany(v => v).Where(v => v.ModId == "persona5-ap-smoke")
        .All(v => v.FullPath.StartsWith(Path.Combine(temp, "user"))), "generated files stay outside distributable mod folder");

    var corrupt = CopyPatchSet("bad-patch");
    var firstPatch = entries[0]!["patch"]!.GetValue<string>();
    File.WriteAllBytes(Path.Combine(corrupt, "patches", firstPatch), [0]);
    ExpectFailure(() => GameAssetCache.Prepare(corrupt, Path.Combine(temp, "bad-patch-user"), CriFsLib.Instance, archives),
        "damaged distributed patch is rejected");
    Check(!Directory.Exists(Path.Combine(temp, "bad-patch-user")), "damaged patches publish no generated files");

    var wrongVersion = CopyPatchSet("wrong-version");
    var manifestPath = Path.Combine(wrongVersion, "patches/manifest.json");
    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
    manifest["files"]![0]!["original_sha256"] = new string('0', 64);
    File.WriteAllText(manifestPath, manifest.ToJsonString());
    ExpectFailure(() => GameAssetCache.Prepare(wrongVersion, Path.Combine(temp, "wrong-version-user"), CriFsLib.Instance, archives),
        "unsupported original file checksum is rejected");
    Check(!Directory.Exists(Path.Combine(temp, "wrong-version-user")), "unsupported game versions publish no generated files");
    ExpectFailure(() => GameAssetCache.Prepare(mod, Path.Combine(temp, "missing-user"), CriFsLib.Instance, []),
        "missing CPKs fail before native AP hooks are initialized");

    var unsafeManifest = CopyPatchSet("unsafe-manifest");
    manifestPath = Path.Combine(unsafeManifest, "patches/manifest.json");
    manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
    manifest["files"]![0]!["path"] = "base.cpk/../../outside";
    File.WriteAllText(manifestPath, manifest.ToJsonString());
    ExpectFailure(() => GameAssetCache.Prepare(unsafeManifest, Path.Combine(temp, "unsafe-user"), CriFsLib.Instance, archives),
        "unsafe manifest paths are rejected");
    ExpectFailure(() => BinaryDelta.Apply([], [1, 2, 3]), "malformed delta header is rejected");
    foreach (var (path, length, writeTime) in originalStats)
        Check(new FileInfo(path).Length == length && File.GetLastWriteTimeUtc(path) == writeTime,
            "original archive is unchanged: " + Path.GetFileName(path));
    Console.WriteLine($"All {checks} integration checks passed.");
}
finally
{
    Directory.Delete(temp, true);
}
