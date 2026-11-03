using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;

const string game = "Persona 5 Royal";
const string slot = "Joker";
const long armoryLocationId = 0x5052_0001;
const long shidoClearLocationId = 0x5052_0106;
const long victoryId = 0x5052_1003;

var address = args.Length > 0 ? args[0] : "localhost:38281";
var session = ArchipelagoSessionFactory.CreateSession(address);
var victoryReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

void InspectItems()
{
    foreach (var item in session.Items.AllItemsReceived)
    {
        if (item.ItemId == victoryId && victoryReceived.TrySetResult())
            Console.WriteLine($"Received {item.ItemName} (0x{item.ItemId:X}) from {item.Player.Alias}.");
    }
}

session.Items.ItemReceived += (ReceivedItemsHelper helper) =>
{
    helper.DequeueItem();
    InspectItems();
};

await session.ConnectAsync();
var login = await session.LoginAsync(game, slot, ItemsHandlingFlags.AllItems,
    version: new Version(0, 6, 8), tags: null, uuid: null, password: null, requestSlotData: true);
if (!login.Successful)
{
    var failure = (LoginFailure)login;
    throw new InvalidOperationException("Login failed: " + string.Join("; ", failure.Errors));
}

var successful = (LoginSuccessful)login;
if (Convert.ToInt32(successful.SlotData["client_version"]) != 12 ||
    Convert.ToInt32(successful.SlotData["chest_count"]) != 155 ||
    Convert.ToInt32(successful.SlotData["palace_clear_count"]) != 8 ||
    Convert.ToInt32(successful.SlotData["calling_card_count"]) != 8 ||
    Convert.ToInt32(successful.SlotData["confidant_ability_count"]) != 157 ||
    Convert.ToInt32(successful.SlotData["shop_item_count"]) != 20 ||
    !Convert.ToBoolean(successful.SlotData["grant_confidant_abilities"]) ||
    Convert.ToInt32(successful.SlotData["persona_check_count"]) != 0 ||
    Convert.ToBoolean(successful.SlotData["compendium_sanity"]) ||
    Convert.ToInt32(successful.SlotData["mementos_chests_per_path"]) != 5 ||
    Convert.ToInt32(successful.SlotData["mementos_path_count"]) != 9 ||
    Convert.ToInt32(successful.SlotData["total_location_count"]) != 231 ||
    !Convert.ToBoolean(successful.SlotData["death_link"]) ||
    Convert.ToInt32(successful.SlotData["death_link_damage_percent"]) != 30)
    throw new InvalidOperationException("Server slot data is not the expected 231-location default test world.");
if (Convert.ToString(successful.SlotData["goal_name"]) != "shido")
    throw new InvalidOperationException("Smoke seed must use the default goal_palaces: [shido].");
Console.WriteLine("Connected. Default seed exposes 231 locations, with Compendium Sanity off and 5 checks in each of 8 chest-bearing Mementos Paths.");
const long shopFirstId = 0x50520C00;
var scout = await session.Locations.ScoutLocationsAsync([armoryLocationId, shidoClearLocationId, shopFirstId]);
if (scout[shopFirstId].LocationName != "Shop Item #1")
    throw new InvalidOperationException("Numbered shop location identity is incorrect.");
Console.WriteLine($"Shop scout: {scout[shopFirstId].LocationName} -> {scout[shopFirstId].ItemName} ({scout[shopFirstId].Flags}).");
Console.WriteLine($"Scout: {scout[armoryLocationId].LocationName} -> {scout[armoryLocationId].ItemName}.");
Console.WriteLine($"Scout: {scout[shidoClearLocationId].LocationName} -> {scout[shidoClearLocationId].ItemName}.");

InspectItems();
await session.Locations.CompleteLocationChecksAsync(armoryLocationId);
Console.WriteLine("Randomized Armory chest check sent.");
if (!victoryReceived.Task.IsCompleted)
{
    await session.Locations.CompleteLocationChecksAsync(shidoClearLocationId);
    Console.WriteLine("Shido Palace-clear check sent.");
}

await victoryReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
session.SetGoalAchieved();
Console.WriteLine("Protocol smoke test passed; Shido Victory received and goal status sent.");
await session.Socket.DisconnectAsync();
