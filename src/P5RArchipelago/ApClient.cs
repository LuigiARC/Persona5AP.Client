using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using P5RArchipelago.Configuration;
using System.Collections.Concurrent;

namespace P5RArchipelago;

internal sealed class ApClient : IDisposable
{
    public const string GameName = "Persona 5 Royal (luigiarc)";
    public const long VictoryItemId = 0x5052_1003;
    public const long PalaceClearTokenItemId = 0x5052_1004;
    public const long ProgressiveMementosAccessItemId = 0x5052_1007;
    public const long CompendiumEntryTokenItemId = 0x5052_1008;
    public const long PartyLevelUp1ItemId = 0x5052_1018;
    public const long PartyLevelUp3ItemId = 0x5052_1019;
    public const long PartyLevelUp5ItemId = 0x5052_101A;
    public static readonly IReadOnlyDictionary<long, int> PartyLevelItems =
        new Dictionary<long, int>
        {
            [PartyLevelUp1ItemId] = 1,
            [PartyLevelUp3ItemId] = 3,
            [PartyLevelUp5ItemId] = 5,
        };
    public const int MementosPathCount = 9;
    private bool _iwelethChestsEnabled = true;
    public int DepthsAccessTier { get; private set; } = 4;
    public bool HasDepthsAccess => DepthsAccessTier == 7 && !_progressivePalaceAccess
        ? _palaces.Locations.Any(palace => palace.Key == "shido" &&
            _state.HasReceivedMergedPalaceItem(palace.MergedAccessItemId))
        : PalaceProgressCount >= DepthsAccessTier;
    public const int MementosChestsPerPathMax = 20;

    public static readonly IReadOnlyDictionary<long, string> PartyRecruitItems =
        new Dictionary<long, string>
        {
            [0x5052_1010] = "Yusuke",
            [0x5052_1011] = "Makoto",
            [0x5052_1012] = "Haru",
            [0x5052_1013] = "Futaba",
            [0x5052_1014] = "Akechi",
            [0x5052_1015] = "Sumire",
        };

    // Must mirror world.py's MEMENTOS_GENERIC_CHEST_BASE_ID and 20-id per-Path blocks.
    public const long MementosGenericChestBaseId = 0x5052_0400;
    public const long ShopLocationBaseId = 0x5052_0C00;
    public bool NumberedShopEnabled { get; private set; }
    public int ShopItemCount { get; private set; }
    public int ConnectionGeneration { get; private set; }
    private readonly Dictionary<long, int> _shopPrices = [];
    private readonly ConcurrentDictionary<long, string> _shopDescriptions = new();
    public int ShopPrice(long id) => _shopPrices.GetValueOrDefault(id, 1);
    public string ShopDescription(long id) => _shopDescriptions.GetValueOrDefault(id, "Connect to identify this AP item.");

    public long[] MissingNumberedShopLocations() => Enumerable.Range(0, ShopItemCount)
        .Select(i => ShopLocationBaseId + i)
        .Where(id => !_session.Locations.AllLocationsChecked.Contains(id) && !_state.PendingLocations.Contains(id))
        .ToArray();

    public string ShopRewardName(long id) => _locationRewardNames.GetValueOrDefault(id, "AP Item");

    public void AnnounceNumberedShopHints()
    {
        var ids = MissingNumberedShopLocations().Where(id => _requestedShopHints.TryAdd(id, 0)).ToArray();
        if (ids.Length > 0) _ = AnnounceShopHintsAsync(ids);
    }

    private readonly Config _config;
    private readonly StateStore _state;
    private readonly ChestCatalog _catalog;
    private readonly PalaceCatalog _palaces;
    private readonly ConfidantAbilityCatalog _confidants;
    private readonly PersonaCatalog _personas;
    private readonly TvBundleCatalog _tvBundles;
    private readonly Log _log;
    private readonly ArchipelagoSession _session;
    private readonly DeathLinkService _deathLinkService;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _itemProcessingLock = new(1, 1);
    private readonly SemaphoreSlim _locationFlushLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, string> _chestRewardNames = new();
    private readonly ConcurrentDictionary<long, string> _locationRewardNames = new();
    private readonly ConcurrentDictionary<long, byte> _requestedShopHints = new();
    private Task? _connectionTask;
    private readonly object _stopLock = new();
    private Task? _stopTask;
    public string ConnectionStatus { get; private set; } = "Disconnected";

    private bool _worldCompatible;
    private int _requiredPalaceClearTokens;
    private int _requiredPalaceGoalCount;
    private int _slotMementosChestsPerPath;
    private bool _progressivePalaceAccess = true;
    private bool _compendiumSanity;
    private int _requiredCompendiumEntryTokens;
    private bool _deathLinkEnabled;
    private int _deathLinkDamagePercent;
    private int _pendingDeathLinks;
    private bool _partyLevelItemsEnabled;
    public ApClient(Config config, StateStore state, ChestCatalog catalog, PalaceCatalog palaces,
        ConfidantAbilityCatalog confidants, PersonaCatalog personas, TvBundleCatalog tvBundles, Log log)
    {
        _config = config;
        _state = state;
        _catalog = catalog;
        _palaces = palaces;
        _confidants = confidants;
        _personas = personas;
        _tvBundles = tvBundles;
        _log = log;
        _session = ArchipelagoSessionFactory.CreateSession(config.ServerAddress);
        _session.MessageLog.OnMessageReceived += message => _log.Info("[AP] " + message);
        _session.Items.ItemReceived += OnItemReceived;
        _deathLinkService = _session.CreateDeathLinkService();
        _deathLinkService.OnDeathLinkReceived += OnDeathLinkReceived;
    }

    public void Start()
    {
        _connectionTask ??= Task.Run(ConnectionLoopAsync);
    }

    public int QueueLocation(long locationId)
    {
        return QueueLocations([locationId]);
    }

    public int ProgressiveAccessCount => _state.ProgressiveAccessCount;
    public int ProgressiveCallingCardCount => _state.ProgressiveCallingCardCount;
    public int ProgressiveMementosAccessCount => _state.ProgressiveMementosAccessCount;
    public bool ConnectionReady => _session.Socket.Connected && _worldCompatible;
    public int CompletedLocationCount => ConnectionReady
        ? _session.Locations.AllLocationsChecked.Count
        : -1;
    public bool HasReceivedPartyRecruitItem(long itemId) => _state.HasReceivedPartyRecruitItem(itemId);
    public int PreparePartyLevelTarget(int member, int currentLevel) =>
        _partyLevelItemsEnabled ? _state.PreparePartyLevelTarget(member, currentLevel) : currentLevel;

    // Which item scheme this seed picked (options.ProgressivePalaceAccess) -- see
    // ArchipelagoMod.SyntheticFeatureOverride, which branches Palace access/calling-card checks on
    // this instead of always reading the two progressive counters above.
    public bool ProgressivePalaceAccess => _progressivePalaceAccess;

    /// <summary>The cross-Palace progress measure used by protocol 8/9 world rules.</summary>
    public int PalaceProgressCount => _progressivePalaceAccess
        ? ProgressiveAccessCount
        : _palaces.Locations.Count(palace => _state.HasReceivedMergedPalaceItem(palace.MergedAccessItemId));

    public bool HasReceivedMergedPalaceItem(long itemId) => _state.HasReceivedMergedPalaceItem(itemId);
    public long[] PendingTvBundleItemIds => _state.PendingTvBundleItemIds;
    public int BootstrapPreparationVersion => _state.BootstrapPreparationVersion;
    public void AcknowledgeTvBundle(long itemId) => _state.RemovePendingTvBundle(itemId);
    public void MarkBootstrapPrepared(int version) => _state.MarkBootstrapPrepared(version);
    public bool CompendiumSanity => _compendiumSanity;
    public (int Major, uint? SaveSlot) ActiveApPalace => _state.ActiveApPalace;
    public void SetActiveApPalace(int major, uint? saveSlot) => _state.SetActiveApPalace(major, saveSlot);
    public void ClearActiveApPalace() => _state.ClearActiveApPalace();
    public int PendingDeathLinkCount => Volatile.Read(ref _pendingDeathLinks);

    /// <summary>Consumes one queued DeathLink and returns its configured max-HP damage.
    /// Returning zero can mean either an empty queue or a deliberately send-only setting;
    /// callers only invoke this after observing PendingDeathLinkCount.</summary>
    public int ConsumePendingDeathLinkDamagePercent()
    {
        while (true)
        {
            var pending = Volatile.Read(ref _pendingDeathLinks);
            if (pending <= 0) return 0;
            if (Interlocked.CompareExchange(ref _pendingDeathLinks, pending - 1, pending) == pending)
                return _deathLinkDamagePercent;
        }
    }

    public void RequeuePendingDeathLink() => Interlocked.Increment(ref _pendingDeathLinks);

    public void SendDeathLinkForPartyWipe()
    {
        if (!ConnectionReady || !_deathLinkEnabled) return;
        try
        {
            var source = string.IsNullOrWhiteSpace(_config.SlotName) ? "Joker" : _config.SlotName;
            _deathLinkService.SendDeathLink(new DeathLink(source,
                $"{source}'s party was wiped out in Persona 5 Royal."));
            _log.Info("Party wipe detected; sent DeathLink.");
        }
        catch (Exception ex)
        {
            _log.Error($"Could not send DeathLink: {ex.Message}");
        }
    }

    public string? ChestRewardName(uint flag)
    {
        if (!_catalog.TryGetByFlag(flag, out _)) return null;
        return _chestRewardNames.TryGetValue(flag, out var name)
            ? name : "AP Item (connect to identify)";
    }

    public string? NextMementosChestRewardName(int path)
    {
        if (path == 8 && !_iwelethChestsEnabled) return null;
        if (path is < 1 or > MementosPathCount || _slotMementosChestsPerPath <= 0)
            return null;
        var next = _state.MementosChestsOpenedForPath(path) + 1;
        if (next > _slotMementosChestsPerPath)
            return null;
        var locationId = MementosGenericChestBaseId
            + (path - 1) * MementosChestsPerPathMax + next - 1;
        return _locationRewardNames.TryGetValue(locationId, out var name)
            ? name : "AP Item (connect to identify)";
    }

    public ConfidantShopOfferDetails? HintNextConfidantOffer(ushort cmmId, short currentRank,
        bool announceHint)
    {
        // Treat the AP server as authoritative. The reusable save can have either lower ranks
        // than its completed AP checks or (after an older buggy shop session) higher ranks than
        // checks which are still missing. Sell the lowest unchecked AP rank in both cases.
        // Purchase code never lowers the native rank and submits these exact location IDs.
        var checkedLocations = ConnectionReady
            ? _session.Locations.AllLocationsChecked.ToHashSet()
            : [];
        var pendingByRank = _confidants.Locations
            .Where(location => location.TriggerCmmIds.Contains(cmmId))
            .Where(location => !checkedLocations.Contains(location.LocationId))
            .GroupBy(location => location.Rank)
            .OrderBy(group => group.Key)
            .FirstOrDefault();
        if (pendingByRank is null) return null;

        var targetRank = pendingByRank.Key;
        var offers = pendingByRank.ToArray();
        foreach (var offer in offers)
        {
            var reward = _locationRewardNames.GetValueOrDefault(offer.LocationId,
                "AP Item (connect to identify)");
            _log.Info($"Confidant shop offer: {offer.Name} -> {reward}.");
        }

        if (ConnectionReady && announceHint)
        {
            var unhinted = offers
                .Select(offer => offer.LocationId)
                .Where(id => _requestedShopHints.TryAdd(id, 0))
                .ToArray();
            if (unhinted.Length > 0)
                _ = AnnounceShopHintsAsync(unhinted);
        }
        var displayName = offers.Length == 1
            ? offers[0].Name
            : $"{offers[0].Name} (+{offers.Length - 1} checks)";
        _log.Info($"Confidant shop cmm {cmmId}: server has {checkedLocations.Count} checked locations; displaying '{displayName}'.");
        return new ConfidantShopOfferDetails(targetRank, displayName,
            offers.Select(offer => offer.LocationId).ToArray());
    }

    /// <summary>
    /// Mementos's ordinary regenerating-Path chests have no stable per-chest identity (see
    /// ChestMonitor.ChestOpenedCompleted and options.MementosChestsPerPath). A validated
    /// FLD_GET_DNG_NO hook supplies the current Path; each Path has an independent counter and
    /// 20-id location block. Opens outside a recognized Path are ignored.
    /// </summary>
    public void OnUncatalogedChestOpened(int path)
    {
        if (path == 8 && !_iwelethChestsEnabled) return;
        if (path is < 1 or > MementosPathCount)
        {
            _log.Info("Uncatalogued chest opened before a valid Mementos Path was observed; no AP check sent.");
            return;
        }

        var count = _state.IncrementMementosChestsOpened(path);
        if (count > _slotMementosChestsPerPath)
        {
            _log.Info($"Mementos Path {path} chest opened ({count}); its {_slotMementosChestsPerPath} AP checks are exhausted.");
            return;
        }

        var locationId = MementosGenericChestBaseId
            + (path - 1) * MementosChestsPerPathMax + count - 1;
        if (QueueLocation(locationId) > 0)
            _log.Info($"Mementos Path {path} chest opened ({count}/{_slotMementosChestsPerPath}); queued AP location {locationId}.");
    }

    public int QueueLocations(IEnumerable<long> locationIds)
    {
        var candidates = locationIds;
        if (_session.Socket.Connected && _worldCompatible)
        {
            var alreadyChecked = _session.Locations.AllLocationsChecked.ToHashSet();
            candidates = candidates.Where(locationId => !alreadyChecked.Contains(locationId));
        }

        var added = _state.AddPendingLocations(candidates);
        if (added > 0) _ = FlushPendingLocationsAsync();
        return added;
    }

    private async Task ConnectionLoopAsync()
    {
        var failures = 0;
        while (!_cancellation.IsCancellationRequested)
        {
            if (_session.Socket.Connected)
            {
                await DelaySafely(TimeSpan.FromSeconds(1));
                continue;
            }

            var wait = failures == 0 ? TimeSpan.Zero :
                TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(failures, 6))));
            await DelaySafely(wait);
            if (_cancellation.IsCancellationRequested) break;

            try
            {
                _log.Info($"Connecting to {_config.ServerAddress} as {_config.SlotName}...");
                ConnectionStatus = "Connecting";
                await _session.ConnectAsync();
                if (_cancellation.IsCancellationRequested)
                {
                    await _session.Socket.DisconnectAsync();
                    break;
                }
                var result = await _session.LoginAsync(GameName, _config.SlotName, ItemsHandlingFlags.AllItems,
                    version: new Version(0, 6, 8), tags: null, uuid: null, password: _config.ServerPassword,
                    requestSlotData: true);

                if (!result.Successful)
                {
                    var failure = (LoginFailure)result;
                    _log.Error("AP login refused: " + string.Join("; ", failure.Errors));
                    ConnectionStatus = "Login refused - check slot/password";
                    failures++;
                    await _session.Socket.DisconnectAsync();
                    continue;
                }

                failures = 0;
                _worldCompatible = ApplySlotData((LoginSuccessful)result);
                if (_cancellation.IsCancellationRequested)
                {
                    _worldCompatible = false;
                    await _session.Socket.DisconnectAsync();
                    break;
                }
                if (!_worldCompatible)
                {
                    _log.Error("Connected, but this slot was generated with an incompatible chest catalog. No checks or items will be processed.");
                    ConnectionStatus = "Incompatible APWorld";
                    continue;
                }

                _log.Info("Connected and authenticated with a compatible chest catalog.");
                ConnectionStatus = "Connected";
                await ScoutLocationRewardsAsync();
                await FlushPendingLocationsAsync();
                await ProcessReceivedItemsAsync();
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                _log.Error($"Connection attempt failed: {ex.Message}");
                ConnectionStatus = "Connection failed - retrying";
                if (_session.Socket.Connected)
                    await _session.Socket.DisconnectAsync();
            }
        }
    }

    private bool ApplySlotData(LoginSuccessful login)
    {
        // Protocol12 omits Iweleth chest locations and corrects finale access. Older
        // protocols keep their world-specific rules; reserved location IDs do not shift.
        if (!login.SlotData.TryGetValue("client_version", out var clientVersionRaw) ||
            !int.TryParse(Convert.ToString(clientVersionRaw), out var clientVersion) ||
            clientVersion is not (8 or 9 or 10 or 11 or 12))
        {
            _log.Error($"Slot-data mismatch for 'client_version': expected compatible protocol 8–12, got '{clientVersionRaw ?? "<missing>"}'.");
            return false;
        }

        _partyLevelItemsEnabled = clientVersion >= 10;
        _iwelethChestsEnabled = clientVersion < 12;
        DepthsAccessTier = clientVersion >= 12 ? 7 : 4;
        NumberedShopEnabled = clientVersion >= 11;
        ShopItemCount = 0;
        if (NumberedShopEnabled)
        {
            if (!login.SlotData.TryGetValue("shop_item_count", out var shopCount) ||
                !int.TryParse(Convert.ToString(shopCount), out var count) || count is < 0 or > 500 ||
                !login.SlotData.TryGetValue("shop_location_base_id", out var shopBase) ||
                Convert.ToInt64(shopBase) != ShopLocationBaseId)
            {
                _log.Error("Numbered shop slot data is invalid.");
                return false;
            }
            ShopItemCount = count;
            _shopPrices.Clear();
            if (!login.SlotData.TryGetValue("shop_prices", out var pricesRaw)) return false;
            var prices = Newtonsoft.Json.Linq.JObject.FromObject(pricesRaw);
            for (var index = 0; index < count; index++)
            {
                var id = ShopLocationBaseId + index;
                var price = prices[id.ToString()]?.ToObject<int>();
                if (price is null or < 1 or > 9999999)
                {
                    _log.Error($"Invalid generated shop price for location {id}.");
                    return false;
                }
                _shopPrices[id] = price.Value;
            }
        }

        var expected = new Dictionary<string, string>
        {
            ["chest_catalog_version"] = _catalog.Version.ToString(),
            ["chest_count"] = _catalog.Locations.Count.ToString(),
            ["chest_catalog_hash"] = _catalog.Hash,
            ["palace_catalog_version"] = _palaces.Version.ToString(),
            ["palace_clear_count"] = _palaces.Locations.Count.ToString(),
            ["calling_card_count"] = _palaces.Locations.Count.ToString(),
            ["palace_catalog_hash"] = _palaces.Hash,
            ["confidant_catalog_version"] = _confidants.Version.ToString(),
            ["confidant_ability_count"] = _confidants.Locations.Count.ToString(),
            ["confidant_catalog_hash"] = _confidants.Hash,
            ["persona_catalog_version"] = _personas.Version.ToString(),
            ["persona_catalog_hash"] = _personas.Hash,
            ["tv_bundle_catalog_version"] = _tvBundles.Version.ToString(),
            ["tv_bundle_catalog_hash"] = _tvBundles.Hash,
            ["tv_bundle_count"] = _tvBundles.Bundles.Count.ToString(),
            ["victory_item_id"] = VictoryItemId.ToString(),
            ["palace_clear_token_item_id"] = PalaceClearTokenItemId.ToString(),
            ["compendium_entry_token_item_id"] = CompendiumEntryTokenItemId.ToString(),
        };

        foreach (var pair in expected)
        {
            if (!login.SlotData.TryGetValue(pair.Key, out var rawValue) ||
                !string.Equals(Convert.ToString(rawValue), pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                _log.Error($"Slot-data mismatch for '{pair.Key}': expected '{pair.Value}', got '{rawValue ?? "<missing>"}'. Generate a new seed with the packaged APWorld.");
                return false;
            }
        }

        if (_partyLevelItemsEnabled)
        {
            var levelItemIds = new Dictionary<string, long>
            {
                ["party_level_up_1_item_id"] = PartyLevelUp1ItemId,
                ["party_level_up_3_item_id"] = PartyLevelUp3ItemId,
                ["party_level_up_5_item_id"] = PartyLevelUp5ItemId,
            };
            foreach (var pair in levelItemIds)
            {
                if (!login.SlotData.TryGetValue(pair.Key, out var rawValue) ||
                    !long.TryParse(Convert.ToString(rawValue), out var itemId) || itemId != pair.Value)
                {
                    _log.Error($"Slot-data mismatch for '{pair.Key}': expected '{pair.Value}', got '{rawValue ?? "<missing>"}'. Generate a new seed with the packaged APWorld.");
                    return false;
                }
            }
        }

        _compendiumSanity = login.SlotData.TryGetValue("compendium_sanity", out var compendiumRaw) &&
            bool.TryParse(Convert.ToString(compendiumRaw), out var compendiumSanity) && compendiumSanity;
        var expectedPersonaChecks = _compendiumSanity ? _personas.Locations.Count : 0;
        if (!login.SlotData.TryGetValue("persona_check_count", out var personaCountRaw) ||
            !int.TryParse(Convert.ToString(personaCountRaw), out var personaCheckCount) ||
            personaCheckCount != expectedPersonaChecks)
        {
            _log.Error($"Invalid persona_check_count slot data: expected {expectedPersonaChecks}, got '{personaCountRaw ?? "<missing>"}'.");
            return false;
        }

        if (!login.SlotData.TryGetValue("required_compendium_entry_tokens", out var compendiumGoalRaw) ||
            !int.TryParse(Convert.ToString(compendiumGoalRaw), out _requiredCompendiumEntryTokens) ||
            _requiredCompendiumEntryTokens is not (0 or 232))
        {
            _log.Error($"Invalid required_compendium_entry_tokens slot data: '{compendiumGoalRaw ?? "<missing>"}'.");
            return false;
        }

        if (!login.SlotData.TryGetValue("required_palace_clear_tokens", out var requiredRaw) ||
            !int.TryParse(Convert.ToString(requiredRaw), out _requiredPalaceClearTokens) ||
            _requiredPalaceClearTokens is < 0 or > 8)
        {
            _log.Error($"Invalid required_palace_clear_tokens slot data: '{requiredRaw ?? "<missing>"}'.");
            return false;
        }

        if (!login.SlotData.TryGetValue("required_palace_goal_count", out var palaceGoalRaw) ||
            !int.TryParse(Convert.ToString(palaceGoalRaw), out _requiredPalaceGoalCount) ||
            _requiredPalaceGoalCount is < 0 or > 8)
        {
            _log.Error($"Invalid required_palace_goal_count slot data: '{palaceGoalRaw ?? "<missing>"}'.");
            return false;
        }

        // Option-dependent, not identity-hash-dependent: read permissively with a safe
        // fallback rather than rejecting the whole slot over a seed generated before these
        // existed (see the Default* constants above).
        _slotMementosChestsPerPath = login.SlotData.TryGetValue("mementos_chests_per_path", out var mementosRaw) &&
            int.TryParse(Convert.ToString(mementosRaw), out var mementosChestsPerPath) &&
            mementosChestsPerPath is >= 1 and <= MementosChestsPerPathMax
            ? mementosChestsPerPath : 0;

        if (_slotMementosChestsPerPath == 0 ||
            !login.SlotData.TryGetValue("mementos_path_count", out var pathCountRaw) ||
            Convert.ToInt32(pathCountRaw) != MementosPathCount ||
            !login.SlotData.TryGetValue("mementos_chests_per_path_max", out var maxRaw) ||
            Convert.ToInt32(maxRaw) != MementosChestsPerPathMax)
        {
            _log.Error("Mementos per-Path chest slot data is missing or incompatible.");
            return false;
        }

        if (login.SlotData.TryGetValue("mementos_generic_chest_base_id", out var baseIdRaw) &&
            long.TryParse(Convert.ToString(baseIdRaw), out var baseId) && baseId != MementosGenericChestBaseId)
        {
            _log.Error($"mementos_generic_chest_base_id mismatch: expected {MementosGenericChestBaseId}, got {baseId}. This client build is out of sync with the connected apworld's protocol constants.");
            return false;
        }

        // Option-dependent, not identity-hash-dependent (predating seeds default to the only
        // scheme that used to exist: progressive).
        _progressivePalaceAccess = !login.SlotData.TryGetValue("progressive_palace_access", out var progressiveRaw) ||
            !bool.TryParse(Convert.ToString(progressiveRaw), out var progressive) || progressive;

        if (!login.SlotData.TryGetValue("death_link", out var deathLinkRaw) ||
            !bool.TryParse(Convert.ToString(deathLinkRaw), out _deathLinkEnabled) ||
            !login.SlotData.TryGetValue("death_link_damage_percent", out var damageRaw) ||
            !int.TryParse(Convert.ToString(damageRaw), out _deathLinkDamagePercent) ||
            _deathLinkDamagePercent is < 0 or > 100)
        {
            _log.Error("DeathLink slot data is missing or invalid.");
            return false;
        }

        if (_deathLinkEnabled)
            _deathLinkService.EnableDeathLink();
        else
            _deathLinkService.DisableDeathLink();

        _state.BindSeed(_session.RoomState.Seed);

        _log.Info($"Slot options: compatible protocol {clientVersion}, money-priced Confidant shop, {_slotMementosChestsPerPath} chest(s) per Mementos Path, Compendium Sanity: {_compendiumSanity}, progressive palace access: {_progressivePalaceAccess}, DeathLink: {_deathLinkEnabled} ({_deathLinkDamagePercent}% received damage).");
        return true;
    }

    private void OnDeathLinkReceived(DeathLink deathLink)
    {
        if (!_worldCompatible || !_deathLinkEnabled) return;
        var pending = Interlocked.Increment(ref _pendingDeathLinks);
        var cause = string.IsNullOrWhiteSpace(deathLink.Cause)
            ? $"DeathLink from {deathLink.Source}"
            : deathLink.Cause;
        _log.Info($"Received {cause}; queued {_deathLinkDamagePercent}% max-HP party damage ({pending} pending).");
    }

    private async Task ScoutLocationRewardsAsync()
    {
        var byLocationId = _catalog.Locations.ToDictionary(location => location.LocationId);
        var ids = new HashSet<long>(byLocationId.Keys);
        for (var path = 1; path <= MementosPathCount; path++)
            if (path != 8 || _iwelethChestsEnabled)
                for (var index = 0; index < _slotMementosChestsPerPath; index++)
                    ids.Add(MementosGenericChestBaseId + (path - 1) * MementosChestsPerPathMax + index);
        if (NumberedShopEnabled)
            for (var index = 0; index < ShopItemCount; index++) ids.Add(ShopLocationBaseId + index);
        else
            foreach (var confidant in _confidants.Locations) ids.Add(confidant.LocationId);

        var result = await _session.Locations.ScoutLocationsAsync([.. ids]);
        if (_cancellation.IsCancellationRequested) return;
        foreach (var pair in result)
        {
            var name = $"AP - {pair.Value.Player.Alias}: {pair.Value.ItemName}";
            _locationRewardNames[pair.Key] = name;
            var flags = (int)pair.Value.Flags;
            var importance = (flags & 1) != 0 ? "very important" : (flags & 2) != 0 ? "useful" :
                (flags & 4) != 0 ? "a trap" : "a supply item";
            _shopDescriptions[pair.Key] = $"{pair.Value.ItemName}\nThis item is {importance}\nfor {pair.Value.Player.Alias}.";
            if (byLocationId.TryGetValue(pair.Key, out var chest))
                _chestRewardNames[chest.Flag] = name;
        }
        _log.Info($"Scouted {_locationRewardNames.Count}/{ids.Count} AP reward names ({_chestRewardNames.Count} fixed chests)." );
        ConnectionGeneration++;
    }

    private async Task AnnounceShopHintsAsync(long[] locationIds)
    {
        try
        {
            await _session.Locations.ScoutLocationsAsync(
                HintCreationPolicy.CreateAndAnnounceOnce, locationIds);
            _log.Info($"Announced {locationIds.Length} currently available Confidant shop hint(s)." );
        }
        catch (Exception ex)
        {
            foreach (var id in locationIds) _requestedShopHints.TryRemove(id, out _);
            _log.Error($"Could not announce Confidant shop hints: {ex.Message}");
        }
    }

    private async Task FlushPendingLocationsAsync()
    {
        await _locationFlushLock.WaitAsync();
        try
        {
            if (!_session.Socket.Connected || !_worldCompatible) return;
            var pending = _state.PendingLocations;
            if (pending.Length == 0) return;

            await _session.Locations.CompleteLocationChecksAsync(pending);
            _state.RemovePendingLocations(pending);
            _log.Info($"Reported {pending.Length} queued location check(s).");
        }
        catch (Exception ex)
        {
            _log.Error($"Location report remains queued for reconnect: {ex.Message}");
        }
        finally
        {
            _locationFlushLock.Release();
        }
    }

    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        helper.DequeueItem();
        _ = ProcessReceivedItemsAsync();
    }

    private async Task ProcessReceivedItemsAsync()
    {
        if (!_worldCompatible) return;
        await _itemProcessingLock.WaitAsync(_cancellation.Token);
        try
        {
            if (_cancellation.IsCancellationRequested || !_worldCompatible) return;
            var index = _state.NextReceivedItemIndex;
            while (index < _session.Items.AllItemsReceived.Count)
            {
                var item = _session.Items.AllItemsReceived[index];
                _log.Info($"Received item #{index}: {item.ItemName} from {item.Player.Alias} (0x{item.ItemId:X}).");

                var nextIndex = index + 1;

                if (item.ItemId == _palaces.ProgressiveAccessItemId)
                {
                    var tier = _state.RecordProgressiveAccessItem(nextIndex);
                    _log.Info($"Progressive Palace Access received (tier {tier}/{_palaces.Locations.Count}).");
                }
                else if (item.ItemId == _palaces.ProgressiveCallingCardItemId)
                {
                    var tier = _state.RecordProgressiveCallingCardItem(nextIndex);
                    _log.Info($"Progressive Calling Card received (tier {tier}/{_palaces.Locations.Count}).");
                }
                else if (item.ItemId == ProgressiveMementosAccessItemId)
                {
                    var tier = _state.RecordProgressiveMementosAccessItem(nextIndex);
                    _log.Info($"Progressive Mementos Access received (tier {tier}/8).");
                }
                else if (PartyRecruitItems.TryGetValue(item.ItemId, out var partyMember))
                {
                    _state.RecordPartyRecruitItem(nextIndex, item.ItemId);
                    _log.Info($"Recruit {partyMember} received; roster flags will reconcile at the Mementos hub.");
                }
                else if (_partyLevelItemsEnabled && PartyLevelItems.TryGetValue(item.ItemId, out var levels))
                {
                    var total = _state.RecordPartyLevelItem(nextIndex, levels);
                    _log.Info($"Party Level Up +{levels} received ({total} shared levels total); application will reconcile at the Mementos hub.");
                }
                else if (_palaces.TryGetByMergedAccessItemId(item.ItemId, out var mergedPalace))
                {
                    _state.RecordMergedPalaceItem(nextIndex, item.ItemId);
                    _log.Info($"{mergedPalace.Name.Split(" - ")[0]} Palace access received (merged item).");
                }
                else if (_tvBundles.TryGet(item.ItemId, out var bundle))
                {
                    _state.RecordPendingTvBundle(nextIndex, item.ItemId);
                    _log.Info($"{bundle.Name} queued; its inventory contents will be applied at the Mementos hub.");
                }
                else
                {
                    _state.RecordReceivedItem(nextIndex);
                }

                index = nextIndex;
            }

            ReportGoalIfSatisfied();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _itemProcessingLock.Release();
        }
    }

    private void ReportGoalIfSatisfied()
    {
        if (_state.GoalReported) return;

        var received = _session.Items.AllItemsReceived;
        var hasVictory = received.Any(item => item.ItemId == VictoryItemId);
        var clearTokens = received.Count(item => item.ItemId == PalaceClearTokenItemId);
        var compendiumTokens = received.Count(item => item.ItemId == CompendiumEntryTokenItemId);
        var palaceSatisfied = _requiredPalaceGoalCount == 0 ||
            (_requiredPalaceGoalCount == 1 && hasVictory) ||
            (_requiredPalaceGoalCount > 1 && clearTokens >= _requiredPalaceClearTokens);
        var compendiumSatisfied = _requiredCompendiumEntryTokens == 0 ||
            compendiumTokens >= _requiredCompendiumEntryTokens;
        if (!palaceSatisfied || !compendiumSatisfied)
            return;

        _session.SetGoalAchieved();
        _state.MarkGoalReported();
        _log.Info("All configured Palace and Compendium goals are satisfied; reported goal completion.");
    }

    private async Task DelaySafely(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) return;
        try
        {
            await Task.Delay(delay, _cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    public Task StopAsync()
    {
        lock (_stopLock)
        {
            if (_stopTask is null || _stopTask.IsFaulted) _stopTask = StopCoreAsync();
            return _stopTask;
        }
    }

    private async Task StopCoreAsync()
    {
        _cancellation.Cancel();
        _worldCompatible = false;
        ConnectionStatus = "Disconnected";
        _session.Items.ItemReceived -= OnItemReceived;
        _deathLinkService.OnDeathLinkReceived -= OnDeathLinkReceived;
        try { _deathLinkService.DisableDeathLink(); } catch { }
        try { await _session.Socket.DisconnectAsync(); } catch { }
        if (_connectionTask is not null)
            await _connectionTask.WaitAsync(TimeSpan.FromSeconds(15));
        // Drain active state writers before another client can open the same profile.
        await _itemProcessingLock.WaitAsync();
        _itemProcessingLock.Release();
        await _locationFlushLock.WaitAsync();
        _locationFlushLock.Release();
        ConnectionStatus = "Disconnected";
        // Event tasks may still be unwinding. Do not dispose their synchronization objects.
    }
    public void Dispose() => _ = StopAsync();
}
