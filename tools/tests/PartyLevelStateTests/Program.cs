using P5RArchipelago;
using P5RArchipelago.Native;

var directory = Path.Combine(AppContext.BaseDirectory, "test-state", Guid.NewGuid().ToString("N"));
var path = Path.Combine(directory, "State.json");
StateStore Open() => new(path, "test-server", "test-slot", "test-catalog", new Log());
var state = Open();
void Equal(int actual, int expected)
{
    if (actual != expected) throw new Exception($"Expected {expected}, got {actual}");
}
Equal(state.RecordPartyLevelItem(1, 1), 1);
Equal(state.PreparePartyLevelTarget(1, 18), 19);
Equal(state.PreparePartyLevelTarget(1, 18), 19); // Failed write: no consumption.
Equal(state.RecordPartyLevelItem(2, 3), 4);
Equal(state.RecordPartyLevelItem(3, 1), 5);
Equal(state.PreparePartyLevelTarget(1, 18), 23); // Extend interrupted batch.
state = Open();
Equal(state.PreparePartyLevelTarget(1, 18), 23); // Durable pending target.
Equal(state.PreparePartyLevelTarget(1, 23), 23); // Verified commit.
Equal(state.PreparePartyLevelTarget(1, 23), 23); // Idempotent.
Equal(state.PreparePartyLevelTarget(6, 21), 26); // Later recruit inherits all credits.
Equal(state.PreparePartyLevelTarget(6, 26), 26);
Equal(state.PreparePartyLevelTarget(2, 98), 99); // Cap.
Equal(state.PreparePartyLevelTarget(2, 99), 99);
Equal(state.RecordPartyLevelItem(4, 5), 10);
Equal(state.PreparePartyLevelTarget(2, 99), 99);
Equal(state.PreparePartyLevelTarget(2, 99), 99);
Equal(state.PreparePartyLevelTarget(1, 23), 28);
Equal(state.PreparePartyLevelTarget(1, 27), 28); // Partial write remains pending.
Equal(state.PreparePartyLevelTarget(1, 28), 28);
state = Open();
Equal(state.PreparePartyLevelTarget(1, 18), 28); // Older save restores absolute floor.
Equal(state.PreparePartyLevelTarget(1, 28), 28);
Equal(state.PreparePartyLevelTarget(1, 28), 28); // No duplicate growth.
Equal(state.RecordPartyLevelItem(5, 3), 13);
Equal(state.PreparePartyLevelTarget(1, 18), 31); // Older save + new rewards, not 18+13.
Equal(state.PreparePartyLevelTarget(1, 31), 31);
Equal(state.PreparePartyLevelTarget(1, 35), 35); // Natural growth never lowered.
Equal(state.PreparePartyLevelTarget(0, 18), 18); // Invalid member.
Equal(state.PreparePartyLevelTarget(3, 0), 0); // Uninitialized recruit.
Console.WriteLine("Party level state tests passed: retries, accumulated credits, reload, recruits, cap, and idempotence.");
Directory.Delete(directory, recursive: true); // Only this test's freshly created state.

Equal(DeathLinkDamageReceiver.TargetHp(100, 400, 30), 1); // Max, not current HP.
Equal(DeathLinkDamageReceiver.TargetHp(267, 412, 30), 144);
Equal(DeathLinkDamageReceiver.TargetHp(1, 400, 30), 1);
Equal(DeathLinkDamageReceiver.TargetHp(0, 400, 30), 0); // Never revive.
Equal(DeathLinkDamageReceiver.TargetHp(100, 400, 0), 100);
var pending = 2;
var damageCalls = 0;
var loaded = false;
DeathLinkDamageReceiver? receiver = null;
receiver = new(() => pending, () => loaded, () => { pending--; return 30; },
    percent => { damageCalls++; receiver!.OnGameplayHpRead(); return 4; },
    () => pending++, new Log());
receiver.OnGameplayHpRead();
Equal(pending, 2); // No consumption before save load.
loaded = true;
receiver.OnGameplayHpRead();
Equal(pending, 1); // Recursive HP reads cannot consume another link.
Equal(damageCalls, 1);
receiver.OnGameplayHpRead();
receiver.OnGameplayHpRead();
Equal(pending, 0);
Equal(damageCalls, 2); // Repeated reads with no pending links do nothing.
receiver.Dispose();
pending = 1;
receiver.OnGameplayHpRead();
Equal(pending, 1);
using var failing = new DeathLinkDamageReceiver(() => pending, () => true,
    () => { pending--; return 30; }, _ => throw new InvalidOperationException("test"),
    () => pending++, new Log());
failing.OnGameplayHpRead();
Equal(pending, 1); // Validation failure retains the link.
Console.WriteLine("DeathLink receiver tests passed: max-HP math, floor, defeated members, queue, recursion, retry, disposal.");

var catalog = ChestCatalog.Load();
var palaceMajors = new[] { 151, 153, 154, 155, 156, 157, 159, 162 };
foreach (var major in palaceMajors)
{
    var ids = catalog.PalaceChestIds(major);
    if (ids.Length == 0) throw new Exception($"Missing Palace chests for {major}");
    var expected = catalog.Locations.Where(chest => chest.Field.StartsWith($"F{major}_"))
        .Select(chest => chest.LocationId).Order().ToArray();
    if (!ids.Order().SequenceEqual(expected)) throw new Exception($"Wrong sweep for {major}");
    Equal(ids.Distinct().Count(), ids.Length);
}
Equal(catalog.PalaceChestIds(0).Length, 0);
Equal(catalog.PalaceChestIds(999).Length, 0);
var bankChestIds = catalog.PalaceChestIds(154);
state = Open();
Equal(state.AddPendingLocations(bankChestIds.Take(1)), 1);
Equal(state.AddPendingLocations(bankChestIds), bankChestIds.Length - 1);
Equal(state.AddPendingLocations(bankChestIds), 0);
state = Open();
Equal(state.AddPendingLocations(bankChestIds), 0); // Durable offline sweep, no repeats.
Directory.Delete(directory, recursive: true);
Console.WriteLine("Palace chest sweep tests passed: all eight Palace mappings, isolation, duplicate/pending suppression, offline persistence.");

namespace P5RArchipelago
{
    internal sealed class Log
    {
        public void Info(string message) { }
        public void Error(string message) { }
    }
}
