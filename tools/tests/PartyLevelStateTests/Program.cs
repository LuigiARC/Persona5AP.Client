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

var inventoryCatalog = TvBundleCatalog.Load();
Equal(inventoryCatalog.NativeItems.Count, 2512);
foreach (var nativeItem in inventoryCatalog.NativeItems)
{
    if (!inventoryCatalog.TryGet(nativeItem.ItemId, out var mapped) || mapped != nativeItem)
        throw new Exception($"Native inventory item is not receivable: {nativeItem.Name}");
    Equal(nativeItem.Contents.Count, 1);
    Equal(nativeItem.Contents[0].Count, 1);
}
var starter = inventoryCatalog.NativeItems.Single(item => item.Name == "Perma-Pick");
if (inventoryCatalog.StateIdentityHash != "166c4516d4396ad89ab5041e60f6c73153f4186b8d923bfb290223fb02cf82d8" ||
    !inventoryCatalog.AcceptsHash(inventoryCatalog.StateIdentityHash) ||
    !inventoryCatalog.AcceptsHash(inventoryCatalog.Hash) || inventoryCatalog.AcceptsHash("unrelated catalog"))
    throw new Exception("Perma-Pick rename broke old/new seed compatibility or durable profile identity.");
if (!inventoryCatalog.TryGet(starter.ItemId, out var receivedStarter) || receivedStarter != starter)
    throw new Exception("Starting inventory is not recognized by the normal received-item catalog.");
if (inventoryCatalog.Bundles.Contains(starter))
    throw new Exception("Starting-only item entered the randomized filler catalog.");
Equal(starter.Contents.Single().ItemId, 0x3151);
Equal(starter.Contents.Single().Count, 1);
state = Open();
state.BindSeed("starting-inventory-test");
state.RecordPendingTvBundle(1, starter.ItemId);
state = Open();
state.BindSeed("starting-inventory-test");
Equal(state.PendingTvBundleItemIds.Length, 1); // Restart before delivery retains the reward.
state.RemovePendingTvBundle(starter.ItemId);
state = Open();
state.BindSeed("starting-inventory-test");
Equal(state.PendingTvBundleItemIds.Length, 0); // Delivered/consumed items are not replenished.
Equal(state.NextReceivedItemIndex, 1); // Reconnection resumes after this starting receipt.
state.RecordPendingTvBundle(2, starter.ItemId);
state.RecordPendingTvBundle(3, starter.ItemId);
state.RemovePendingTvBundle(starter.ItemId);
Equal(state.PendingTvBundleItemIds.Length, 1); // Each configured copy is consumed separately.
state.RemovePendingTvBundle(starter.ItemId);
state = Open();
Equal(state.PendingTvBundleItemIds.Length, 0);
Equal(state.NextReceivedItemIndex, 3);
state.BindSeed("a-new-run");
Equal(state.NextReceivedItemIndex, 0); // A new room can deliver its own starting inventory.
state.RecordPendingTvBundle(1, starter.ItemId);
Equal(state.PendingTvBundleItemIds.Length, 1);
Directory.Delete(directory, recursive: true);
Console.WriteLine("Starting inventory tests passed: catalog mapping, filler isolation, durable queue, acknowledgment, quantities, reconnect, and room isolation.");

if (!FieldRewardSafety.IsReady(true, 0, 0))
    throw new Exception("Unlocked field should accept rewards without a sequence-bit requirement.");
if (FieldRewardSafety.IsReady(false, 0, 0) ||
    FieldRewardSafety.IsReady(true, 0x400000000L, 0) ||
    FieldRewardSafety.IsReady(true, 0x800000000L, 0) ||
    FieldRewardSafety.IsReady(true, 0, 1))
    throw new Exception("Missing field, pad/movement locks or active field event allowed rewards.");
if (!FieldRewardSafety.IsReady(true, 0x100, 2))
    throw new Exception("Unrelated field/event flags should not block rewards.");
Console.WriteLine("Field reward safety tests passed: unlocked field, absent field, pad/movement locks, active events and unrelated flags.");

long clock = 100;
var safe = false;
var jobs = 0;
var pendingInventory = false;
object source = new();
long generation = 1, version = 1;
using var dispatcher = new AutomaticRewardDispatcher(() => new RewardStamp(source, generation, version),
    () => safe, () => pendingInventory, () => jobs++, new Log(), () => clock);
dispatcher.Poll(); Equal(jobs, 0); // Never enqueue before loaded/unlocked gameplay.
safe = true;
dispatcher.Poll(); dispatcher.Poll(); Equal(jobs, 1);
if (!dispatcher.TryBegin() || dispatcher.TryBegin()) throw new Exception("Flow begin is not reentry-safe.");
dispatcher.Poll(); Equal(jobs, 1);
version++; // Receipt arrives during the running flow.
dispatcher.Complete(); dispatcher.Poll(); Equal(jobs, 1);
clock += 16; dispatcher.Poll(); Equal(jobs, 2);
if (!dispatcher.TryBegin()) throw new Exception("New receipt was lost during delivery.");
dispatcher.Complete(); dispatcher.Poll(); Equal(jobs, 2);
pendingInventory = true;
clock += 16;
dispatcher.Poll(); Equal(jobs, 3); // A bounded partial batch continues without a new receipt.
safe = false;
if (dispatcher.TryBegin()) throw new Exception("Began flow after gameplay became unsafe.");
safe = true;
dispatcher.Poll(); Equal(jobs, 4);
generation++;
if (dispatcher.TryBegin()) throw new Exception("Stale save job was accepted.");
dispatcher.Poll(); Equal(jobs, 5);
source = new();
if (dispatcher.TryBegin()) throw new Exception("Stale connection job was accepted.");
pendingInventory = false;
dispatcher.Poll(); Equal(jobs, 6);
if (!dispatcher.TryBegin()) throw new Exception("New save/connection failed to synchronize.");
dispatcher.Complete();
clock += 5001;
dispatcher.Poll(); Equal(jobs, 7); // Newly initialized party records can be revisited quietly.
safe = false; clock += 2001;
if (dispatcher.Busy) throw new Exception("Canceled queued flow blocked F9 indefinitely.");
dispatcher.Dispose(); safe = true;
dispatcher.Poll(); Equal(jobs, 7);
if (dispatcher.TryBegin()) throw new Exception("Disposed dispatcher began a job.");

var attempts = 0;
using var retryDispatcher = new AutomaticRewardDispatcher(() => new RewardStamp(source, 1, 1),
    () => true, () => false, () => { attempts++; throw new InvalidOperationException("test"); }, new Log(), () => clock);
retryDispatcher.Poll(); retryDispatcher.Poll(); Equal(attempts, 1);
clock += 2001; retryDispatcher.Poll(); Equal(attempts, 2);
Console.WriteLine("Automatic reward dispatcher tests passed: gameplay guards, queue/reentry, concurrent receipts, partial batches, reload/profile isolation, cancellation, retry, refresh, disposal.");

state = Open(); state.BindSeed("automatic-inventory-test");
var shopping = inventoryCatalog.Bundles.Single(item => item.Name == "Spring Awake Set");
state.RecordPendingTvBundle(1, shopping.ItemId);
var amounts = new Dictionary<ushort, int>();
var failSecond = true;
InventoryRewardReceiver? receiverUnderTest = null;
bool Grant(ushort itemId, byte count)
{
    if (failSecond && itemId == shopping.Contents[1].ItemId) return false;
    receiverUnderTest?.Apply(); // Native inventory writes may reenter a gameplay callback.
    amounts[itemId] = amounts.GetValueOrDefault(itemId) + count;
    return true;
}
InventoryRewardReceiver Receiver() => new(inventoryCatalog, () => state.PendingInventoryReward,
    state.AdvanceInventoryReward, state.RemovePendingTvBundle, Grant, new Log(), () => clock);
receiverUnderTest = Receiver();
Equal(receiverUnderTest.Apply(), 0);
Equal(state.PendingInventoryReward!.Value.ContentIndex, 1);
if (receiverUnderTest.CanApply) throw new Exception("Failed native grants should back off.");
state = Open(); // The first component remains acknowledged across a client-state reload.
failSecond = false; receiverUnderTest = Receiver();
Equal(receiverUnderTest.Apply(), 1);
if (state.PendingInventoryReward.HasValue) throw new Exception("Completed inventory reward remained queued.");
foreach (var content in shopping.Contents) Equal(amounts[content.ItemId], content.Count);
Equal(receiverUnderTest.Apply(), 0); // No replay/replenishment after acknowledgment.
state.RecordPendingTvBundle(2, shopping.ItemId);
state.RecordPendingTvBundle(3, shopping.ItemId);
Equal(receiverUnderTest.Apply(1), 0); // Only one component per requested budget.
Equal(state.PendingInventoryReward!.Value.ContentIndex, 1);
while (state.PendingInventoryReward.HasValue) receiverUnderTest.Apply();
foreach (var content in shopping.Contents) Equal(amounts[content.ItemId], 3 * content.Count);
for (var index = 4; index <= 11; index++) state.RecordPendingTvBundle(index, starter.ItemId);
Equal(receiverUnderTest.Apply(), 4);
Equal(state.PendingTvBundleItemIds.Length, 4);
Equal(receiverUnderTest.Apply(), 4);
Equal(amounts[starter.Contents[0].ItemId], 8);
Equal(receiverUnderTest.Apply(), 0);
state = Open(); Equal(state.PendingTvBundleItemIds.Length, 0); Equal(state.NextReceivedItemIndex, 11);
receiverUnderTest = Receiver();
state.RecordPendingTvBundle(12, shopping.ItemId);
Equal(receiverUnderTest.Apply(1), 0);
var firstAfterPartial = amounts[shopping.Contents[0].ItemId];
state.RecordPendingTvBundle(13, starter.ItemId);
state.RecordPendingTvBundle(14, shopping.ItemId);
if (state.PrioritizePendingInventoryReward(1, shopping.ItemId))
    throw new Exception("Stale selected reward should be rejected.");
if (!state.PrioritizePendingInventoryReward(1, starter.ItemId))
    throw new Exception("Could not select a non-head reward.");
Equal(receiverUnderTest.Apply(int.MaxValue, maximumRewards: 1), 1);
Equal(state.PendingTvBundleItemIds.Length, 2); // Single claim never drains the rest.
Equal(state.PendingInventoryReward!.Value.ContentIndex, 1); // Old head retained its cursor.
if (!state.PrioritizePendingInventoryReward(1, shopping.ItemId))
    throw new Exception("Could not select second receipt of the same item.");
Equal(state.PendingInventoryReward!.Value.ContentIndex, 0);
Equal(receiverUnderTest.Apply(int.MaxValue, maximumRewards: 1), 1);
Equal(state.PendingTvBundleItemIds.Length, 1);
Equal(state.PendingInventoryReward!.Value.ContentIndex, 1);
Equal(amounts[shopping.Contents[0].ItemId], firstAfterPartial + shopping.Contents[0].Count);
// Simulate a pre-menu client state: migrate the existing head cursor, never replay it.
var legacyState = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
legacyState.Remove("PendingTvBundleContentIndices");
File.WriteAllText(path, legacyState.ToJsonString());
state = Open();
receiverUnderTest = Receiver();
Equal(state.PendingInventoryReward!.Value.ContentIndex, 1);
Equal(receiverUnderTest.Apply(int.MaxValue), 1);
Equal(amounts[shopping.Contents[0].ItemId], firstAfterPartial + shopping.Contents[0].Count);
Equal(state.PendingTvBundleItemIds.Length, 0);
Directory.Delete(directory, recursive: true);
Console.WriteLine("Automatic inventory tests passed: readback failures, backoff, partial-bundle cursor persistence, bounded grants, reentry, duplicate receipts, and no replay.");
Console.WriteLine("Pending item claim tests passed: selected non-head receipt, one-reward limit, duplicate receipts, stale selection, preserved partial cursors, legacy migration and claim all.");

using var canceledFlow = new AutomaticRewardDispatcher(() => new RewardStamp(source, 1, 1),
    () => true, () => false, () => { }, new Log(), () => clock);
canceledFlow.Poll();
if (!canceledFlow.TryBegin()) throw new Exception("Could not begin canceled-flow test.");
clock += 10001;
if (canceledFlow.Busy) throw new Exception("Aborted game-flow job blocked the hub permanently.");
if (args.Length == 3)
{
    var rebuilt = P5RArchipelago.GameAssets.BinaryDelta.Apply(File.ReadAllBytes(args[0]), File.ReadAllBytes(args[2]));
    if (!rebuilt.SequenceEqual(File.ReadAllBytes(args[1]))) throw new Exception("Runtime hub patch does not match the compiled automatic reward script.");
    Console.WriteLine("Runtime BinaryDelta verified: original game hub -> exact compiled automatic reward script.");
}

namespace P5RArchipelago
{
    internal sealed class Log
    {
        public void Info(string message) { }
        public void Error(string message) { }
    }
}
