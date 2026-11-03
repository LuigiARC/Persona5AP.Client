namespace P5RArchipelago.Configuration;

internal static class LegacyProfileMigration
{
    internal const string PreviousModId = "luigi.p5r.archipelago.minimal";

    public static int Import(string destination)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (parent is null) return 0;
        var previous = Path.Combine(parent, PreviousModId);
        if (!Directory.Exists(previous)) return 0;

        string[] filenames = ["Config.json", "Connection.json", "State.json"];
        if (filenames.Any(name => File.Exists(Path.Combine(destination, name))) ||
            Directory.Exists(Path.Combine(destination, "Connections")))
            return 0;

        var files = filenames.Select(name => Path.Combine(previous, name)).Where(File.Exists).ToList();
        var connections = Path.Combine(previous, "Connections");
        if (Directory.Exists(connections))
            files.AddRange(Directory.EnumerateFiles(connections, "*.json"));
        foreach (var file in files)
        {
            var target = Path.Combine(destination, Path.GetRelativePath(previous, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
        return files.Count;
    }
}
