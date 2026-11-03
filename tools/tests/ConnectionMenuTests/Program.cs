using P5RArchipelago.Configuration;
using System.Text.Json;

void Require(bool value) { if (!value) throw new Exception("Connection-menu regression failed"); }
foreach (var host in new[] { "localhost:38281", "127.0.0.1:38281", "archipelago.gg:12345", "wss://example.org:443", "[::1]:38281" })
    Require(ConnectionSettings.IsValid(host, "luigiJoker"));
foreach (var host in new[] { "", "http://example.org:123", "localhost:99999", "host name:38281", "ws://password@example.org:38281" })
    Require(!ConnectionSettings.IsValid(host, "Joker"));
Require(!ConnectionSettings.IsValid("localhost:38281", ""));
Require(!ConnectionSettings.IsValid("localhost:38281", new string('a', 17)));
var a = new ConnectionSettings { Host = "localhost:38281", Slot = "Joker", Password = "Secret:DoNotPersist" };
var b = new ConnectionSettings { Host = a.Host, Slot = "Other" };
Require(a.ProfileKey != b.ProfileKey);
Require(a.ProfileKey == new ConnectionSettings { Host = a.Host, Slot = a.Slot }.ProfileKey);
var path = Path.Combine(AppContext.BaseDirectory, "profiles", Guid.NewGuid().ToString("N"), "Connection.json");
a.Save(path);
var json = File.ReadAllText(path);
Require(!json.Contains("Secret") && !json.Contains("Password"));
var loaded = ConnectionSettings.Load(path, new Config { SlotName = "Legacy", ServerPassword = "legacy-secret" }, _ => {});
Require(loaded.Slot == "Joker" && loaded.Password == "");
Require(ConnectionSettings.Load(path + ".missing", new Config { SlotName = "Legacy", ServerPassword = "legacy-secret" }, _ => {}).Password == "legacy-secret");
var editor = new ConnectionFieldEditor();
editor.Begin(3, "Hidden");
Require(editor.Display == "******");
editor.Clear(); Require(editor.Append(ConnectionFieldEditor.Alphabet.IndexOf(':')));
Require(editor.Draft == ":" && editor.Display == "*");
editor.Backspace(); Require(editor.Draft == "");
editor.Begin(2, new string('a', 16)); Require(!editor.Append(0));
editor.Begin(1, new string('a', 256)); Require(!editor.Append(0));
Require(editor.Display.Length == 51 && editor.Draft.Length == 256);
editor.Begin(3, new string('z', 256)); Require(!editor.Display.Contains('z'));
Require(!editor.Append(-1));
Console.WriteLine("Connection menu tests passed: validation, migration, state isolation, password privacy, editing, limits.");

var migrationRoot = Path.Combine(Path.GetTempPath(), "persona5-ap-profile-" + Guid.NewGuid().ToString("N"));
try
{
    var previous = Path.Combine(migrationRoot, LegacyProfileMigration.PreviousModId);
    var destination = Path.Combine(migrationRoot, "luigi.p5r.archipelago");
    Directory.CreateDirectory(Path.Combine(previous, "Connections"));
    Directory.CreateDirectory(Path.Combine(previous, "GameAssets"));
    File.WriteAllText(Path.Combine(previous, "Config.json"), "original-settings");
    File.WriteAllText(Path.Combine(previous, "State.json"), "original-state");
    File.WriteAllText(Path.Combine(previous, "Connection.json"), "original-connection");
    File.WriteAllText(Path.Combine(previous, "Connections", "slot.json"), "slot-progress");
    File.WriteAllText(Path.Combine(previous, "GameAssets", "local-only.BF"), "generated-game-cache");
    Require(LegacyProfileMigration.Import(destination) == 4);
    Require(File.ReadAllText(Path.Combine(destination, "State.json")) == "original-state");
    Require(File.ReadAllText(Path.Combine(destination, "Connections", "slot.json")) == "slot-progress");
    Require(File.ReadAllText(Path.Combine(previous, "State.json")) == "original-state");
    Require(!Directory.Exists(Path.Combine(destination, "GameAssets")));
    File.WriteAllText(Path.Combine(destination, "Config.json"), "new-settings");
    Require(LegacyProfileMigration.Import(destination) == 0);
    Require(File.ReadAllText(Path.Combine(destination, "Config.json")) == "new-settings");
    var existing = Path.Combine(migrationRoot, "existing-profile");
    Directory.CreateDirectory(existing);
    File.WriteAllText(Path.Combine(existing, "State.json"), "new-progress");
    Require(LegacyProfileMigration.Import(existing) == 0);
    Require(File.ReadAllText(Path.Combine(existing, "State.json")) == "new-progress");
    Require(!File.Exists(Path.Combine(existing, "Config.json")));
    Require(LegacyProfileMigration.Import(Path.Combine(migrationRoot, "missing", "profile")) == 0);
    Console.WriteLine("Mod identity migration passed: settings/progress preserved, existing profiles untouched, game cache excluded.");
}
finally
{
    if (Directory.Exists(migrationRoot)) Directory.Delete(migrationRoot, true);
}
