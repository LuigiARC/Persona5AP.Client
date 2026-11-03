using P5RArchipelago.Configuration;
using P5RArchipelago.Native;
using Reloaded.Hooks.ReloadedII.Interfaces;

namespace P5RArchipelago;

internal sealed partial class ArchipelagoMod : IDisposable
{
    // Encodes (cmm id * 16 + current rank) and returns the next rank containing at least one
    // AP ability check. Querying an offer also creates a public, one-time AP hint for its item.
    private const uint ConfidantShopOfferFeatureIdBase = 910000;
    private const uint ConfidantShopOfferFeatureIdRange = 64 * 16;
    private const uint PartyRecruitFeatureIdBase = 900110;
    // One boolean feature per received Progressive Mementos Access tier. The hub uses these
    // to expose direct checkpoints without depending on P5R's calendar-driven native map list.
    private const uint MementosAccessFeatureIdBase = 900120;
    private const uint MementosAccessFeatureIdCount = 8;
    private const uint ApplyPendingTvBundlesFeatureId = 900130;
    private const uint BaseSave13NeedsPreparationFeatureId = 900131;
    private const uint BaseSave13PreparedFeatureId = 900132;
    private const uint ConnectionReadyFeatureId = 900133;
    private const uint HubMenuOpenedFeatureId = 900134;
    private const uint HubMenuClosedFeatureId = 900135;
    private const int BootstrapPreparationVersion = 2;
    private const uint ClearActiveApPalaceFeatureId = 900136;
    private const uint ConsumeDeathLinkDamageFeatureId = 900137;
    private const uint SetActiveApPalaceFeatureIdBase = 900140;
    private const uint SetActiveApPalaceFeatureIdCount = 8;
    private const uint BeginConfidantShopFeatureId = 900150;
    private const uint EndConfidantShopFeatureId = 900151;
    private const uint ConsumeConfidantShopReturnFeatureId = 900152;
    private const uint SetMementosPathFeatureIdBase = 900160;
    // Protocol12 uses post-Shido access; legacy worlds retain their tier4 server rule.
    // Do not pretend the player owns any additional Mementos Access tiers.
    private const uint DepthsAccessFeatureId = 900170;
    // Read-only, save-slot-scoped guard for Bank reception script overrides.
    private const uint ActiveApBankFeatureId = 900171;
    private const uint ActiveApLaboratoryFeatureId = 900172;
    // Encodes member * 100 + current level and returns an absolute level target. Keeping the
    // current level in the query lets the durable state machine confirm interrupted writes.
    private const uint PartyLevelTargetFeatureIdBase = 920000;
    private const uint PartyLevelTargetFeatureIdRange = 11 * 100;

    private ApClient _apClient;
    private readonly ChestCatalog _catalog;
    private readonly ChestMonitor _chestMonitor;
    private readonly PalaceCatalog _palaces;
    private readonly PalaceClearMonitor? _palaceClearMonitor = null;
    private readonly ConfidantAbilityCatalog _confidants;
    private readonly ConfidantAbilityMonitor? _confidantMonitor = null;
    private readonly PersonaCatalog _personas;
    private readonly TvBundleCatalog _tvBundles;
    private readonly PersonaRegistrationMonitor? _personaMonitor = null;
    private readonly MementosTraverseMonitor? _mementosTraverseMonitor = null;
    private readonly MementosPathMonitor? _mementosPathMonitor = null;
    private readonly BitFlagReader _bitFlags;
    private readonly SaveLoadMonitor? _saveLoadMonitor;
    private readonly HubMenuHotkey? _hubMenuHotkey = null;
    private readonly HubFlowRunner? _hubFlowRunner = null;
    private readonly MessageWindowGuard? _messageWindowGuard = null;
    private readonly DeathLinkDamageReceiver? _deathLinkDamageReceiver = null;
    private readonly PartyHpManipulator? _partyHpManipulator = null;
    private readonly JokerLevelManipulator? _jokerLevelManipulator = null;
    private readonly BattleResultMonitor? _battleResultMonitor = null;
    private readonly PalaceCalendarReturnMonitor? _palaceCalendarReturnMonitor = null;
    private readonly bool _reconcileOpenedChests;
    private readonly Log _log;
    private uint? _loadedSaveSlot;
    private int _hubMenuOpen;
    private bool _bootstrapPreparedForCurrentSession;
    private int _confidantShopActive;
    private int _confidantShopReturnRequested;
    private int _confidantShopHintPassAvailable = 1;
    private int _confidantShopHintsThisOpen;
    private long[] _numberedShopPage = [];
    private static readonly ushort[] ShopRows =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 33, 35];
    private int _selectedMementosPath;
    private readonly object _confidantShopLock = new();
    private readonly Dictionary<ushort, ConfidantShopOffer> _confidantShopOffers = [];

    private static readonly Dictionary<ushort, string> ConfidantArcanaNames = new()
    {
        [1] = "Fool", [2] = "Magician", [3] = "Priestess", [4] = "Empress",
        [5] = "Emperor", [6] = "Hierophant", [7] = "Lovers", [8] = "Chariot",
        [9] = "Justice", [10] = "Hermit", [11] = "Fortune", [12] = "Strength",
        [13] = "Hanged", [14] = "Death", [15] = "Temperance", [16] = "Devil",
        [17] = "Tower", [18] = "Star", [19] = "Moon", [20] = "Sun",
        [21] = "Judgement", [33] = "Faith", [35] = "Councillor"
    };

    public ArchipelagoMod(IReloadedHooks hooks, Config config, string configDirectory, Log log, string hubPath)
    {
        _log = log;
        _catalog = ChestCatalog.Load();
        _palaces = PalaceCatalog.Load();
        _confidants = ConfidantAbilityCatalog.Load();
        _personas = PersonaCatalog.Load();
        _tvBundles = TvBundleCatalog.Load();
        _reconcileOpenedChests = config.ReconcileOpenedChests;
        var catalogIdentity = $"{_catalog.Hash}:{_palaces.Hash}:{_confidants.Hash}:{_personas.Hash}:{_tvBundles.Hash}";
        _configDirectory = configDirectory;
        _runtimeConfig = config;
        _catalogIdentity = catalogIdentity;
        _connectionSettings = ConnectionSettings.Load(Path.Combine(configDirectory, "Connection.json"), config, log.Info);
        _apClient = CreateConnectionClient();

        var scanner = new AddressScanner(log);
        var flow = new TboxFlagReader(hooks, scanner, log);
        _chestMonitor = new ChestMonitor(hooks, scanner, flow, flag => _apClient.ChestRewardName(flag),
            () => _apClient.NextMementosChestRewardName(GetCurrentMementosPath()),
            () => Volatile.Read(ref _confidantShopActive) != 0,
            ResolveConfidantShopOffer, PurchaseConfidantShopOffer,
            () => Interlocked.Exchange(ref _confidantShopReturnRequested, 1),
            config.LogAllChestFlags, log);
        _chestMonitor.ChestOpenedCompleted += OnChestOpenedCompleted;
        _chestMonitor.ConnectionMenuText = GetConnectionMenuText;
        try
        {
            _connectionKeyboard = new ConnectionKeyboard(hooks, scanner,
                character => _connectionEditor.Append(ConnectionFieldEditor.Alphabet.IndexOf(character)),
                _connectionEditor.Backspace, _connectionEditor.Clear,
                () => Volatile.Read(ref _connectionMenuActive) != 0 && Volatile.Read(ref _hubMenuOpen) != 0);
        }
        catch (Exception ex)
        {
            log.Error($"Native connection keyboard input unavailable; character picker retained: {ex.Message}");
        }

        _bitFlags = new BitFlagReader(hooks, scanner);
        try
        {
            _messageWindowGuard = new MessageWindowGuard(hooks, scanner,
                () => Volatile.Read(ref _hubMenuOpen) != 0 || GetActiveApPalace() is not null, log);
        }
        catch (Exception ex)
        {
            log.Error($"Native AP message-window guard unavailable: {ex.Message}");
        }

        // Always track the loaded slot so Save 13's one-time bootstrap cleanup does not
        // accidentally depend on the optional offline-check reconciliation setting.
        _saveLoadMonitor = new SaveLoadMonitor(hooks, scanner, log);
        _saveLoadMonitor.SaveLoaded += OnSaveLoaded;

        try
        {
            _hubFlowRunner = new HubFlowRunner(hooks, scanner, hubPath);
        }
        catch (Exception ex)
        {
            log.Error($"Persona 5 AP hub runner was disabled safely: {ex.Message}");
        }

        if (_hubFlowRunner is not null && !string.IsNullOrWhiteSpace(config.HubMenuHotkey))
        {
            try
            {
                _hubMenuHotkey = new HubMenuHotkey(scanner, _hubFlowRunner, config.HubMenuHotkey,
                    () => _loadedSaveSlot.HasValue && Interlocked.CompareExchange(ref _hubMenuOpen, 1, 0) == 0,
                    () => Interlocked.Exchange(ref _hubMenuOpen, 0),
                    () => GetActiveApPalace() is not null ||
                        (_mementosPathMonitor?.CachedPath ?? Volatile.Read(ref _selectedMementosPath))
                            is >= 1 and <= ApClient.MementosPathCount,
                    log);
            }
            catch (Exception ex)
            {
                log.Error($"Archipelago hub hotkey was disabled safely: {ex.Message}");
            }
        }

        try
        {
            _jokerLevelManipulator = new JokerLevelManipulator(hooks, scanner);
        }
        catch (Exception ex)
        {
            log.Error($"Joker level rewards disabled safely (credits retained): {ex.Message}");
        }

        {
            try
            {
                _partyHpManipulator = new PartyHpManipulator(hooks, scanner, log);
                _deathLinkDamageReceiver = new DeathLinkDamageReceiver(
                    () => _apClient.PendingDeathLinkCount,
                    () => _loadedSaveSlot.HasValue,
                    () => _apClient.ConsumePendingDeathLinkDamagePercent(),
                    _partyHpManipulator.ApplyMaxHpPercentageDamage,
                    () => _apClient.RequeuePendingDeathLink(),
                    log);
                _partyHpManipulator.GameplayHpRead += _deathLinkDamageReceiver.OnGameplayHpRead;
            }
            catch (Exception ex)
            {
                log.Error($"DeathLink receive-damage dispatcher was disabled safely: {ex.Message}");
            }
        }

        try
        {
            _battleResultMonitor = new BattleResultMonitor(hooks, scanner, log);
            _battleResultMonitor.PartyWiped += SendCurrentConnectionDeathLink;
        }
        catch (Exception ex)
        {
            log.Error($"Party-wipe DeathLink hook was disabled safely: {ex.Message}");
        }

        if (_hubFlowRunner is not null)
        {
            try
            {
                _palaceCalendarReturnMonitor = new PalaceCalendarReturnMonitor(hooks, scanner,
                    GetActiveApPalace, palace => _bitFlags.IsSet(palace.ClearFlag),
                    OnPalaceCleared, ClearActiveApPalace, _hubFlowRunner, log);
            }
            catch (Exception ex)
            {
                log.Error($"Post-boss hub return hook was disabled safely: {ex.Message}");
            }
        }

        try
        {
            _palaceClearMonitor = new PalaceClearMonitor(hooks, scanner, _palaces,
                HasCallingCardAccess, MayReportPalaceClear, log);
            _palaceClearMonitor.PalaceCleared += OnPalaceCleared;
            _palaceClearMonitor.CallingCardSent += OnCallingCardSent;
        }
        catch (Exception ex)
        {
            log.Error($"Palace-clear live hook was disabled safely: {ex.Message}");
        }

        try
        {
            // Always active: the hub's "Choose a Palace" menu checks each Palace's
            // access_synthetic_feature_id via CMM_CHECK_ENABLE_FUNC regardless of
            // ReconcileOpenedChests, which only controls the separate offline-reconciliation
            // feature below.
            _confidantMonitor = new ConfidantAbilityMonitor(hooks, scanner, _confidants, SyntheticFeatureOverride);
            _confidantMonitor.AbilityAcquired += OnConfidantAbilityAcquired;
        }
        catch (Exception ex)
        {
            log.Error($"Confidant-ability hooks were disabled safely: {ex.Message}");
        }

        try
        {
            _personaMonitor = new PersonaRegistrationMonitor(hooks, scanner, _personas, log);
            _personaMonitor.PersonaRegistered += OnPersonaRegistered;
        }
        catch (Exception ex)
        {
            log.Error($"Compendium registration hook was disabled safely: {ex.Message}");
        }

        try
        {
            _mementosTraverseMonitor = new MementosTraverseMonitor(hooks, scanner,
                () => _apClient.ProgressiveMementosAccessCount, log);
        }
        catch (Exception ex)
        {
            log.Error($"Mementos traverse virtualization was disabled safely: {ex.Message}");
        }

        try
        {
            _mementosPathMonitor = new MementosPathMonitor(hooks, scanner, log);
        }
        catch (Exception ex)
        {
            log.Error($"Mementos Path tracking was disabled safely: {ex.Message}");
        }

        log.Info($"Hooks active for {_catalog.Locations.Count} Palace chest checks (catalog v{_catalog.Version}).");
        log.Info("Catalogued AP chests suppress their vanilla reward and display the scouted AP item. Will Seeds are not chest checks.");
        log.Info($"Mementos hub asset includes {_palaces.Locations.Count} experimental Palace destinations and clear checks.");
        log.Info($"Calling-card flags are mapped to {_palaces.Locations.Count} additional AP locations.");
        log.Info($"Confidant catalog includes {_confidants.Locations.Count} ability checks generated from CMM.BIN.");
        log.Info($"Compendium catalog includes {_personas.Locations.Count} Persona registration checks.");

        if (_connectionSettings.AutoConnect && !_offlineMenuTest)
            _apClient.Start();
        else
            log.Info("Networking is idle; open F9 > Connection to connect in-game.");
    }

    private void OnChestOpenedCompleted(uint flag)
    {
        if (!_catalog.TryGetByFlag(flag, out var location))
        {
            // ChestMonitor fires for every chest in the game, not just the curated catalog.
            // Palace, Depths of Mementos, and Qliphoth World chests are all covered by name, so
            // an uncatalogued flag here is presumed to be one of Mementos's ordinary
            // regenerating-Path chests, which have no stable per-chest identity to catalog at
            // all -- see ApClient.OnUncatalogedChestOpened.
            _log.Info($"Chest 0x{flag:X8} is not in the verified AP chest catalog; presumed Mementos.");
            _apClient.OnUncatalogedChestOpened(GetCurrentMementosPath());
            return;
        }

        _log.Info($"Completed '{location.Name}' (0x{flag:X8}); queuing AP location {location.LocationId}.");
        _apClient.QueueLocation(location.LocationId);
    }

    private void OnSaveLoaded(uint fileIndex)
    {
        _chestMonitor.SetShopDescriptionsEnabled(false);
        Interlocked.Exchange(ref _confidantShopActive, 0);
        _connectionKeyboard?.End();
        _loadedSaveSlot = fileIndex;
        if (_apClient.NumberedShopEnabled)
            _log.Info("Automatic Confidant grants disabled; existing save ranks and abilities are unchanged.");
        Interlocked.Exchange(ref _hubMenuOpen, 0);
        _bootstrapPreparedForCurrentSession = false;
        if (IsBootstrapSave(fileIndex) && _apClient.CompletedLocationCount <= 0)
        {
            _log.Info($"Deferred save-slot {fileIndex} reconciliation until the current AP room is connected and its bootstrap state is known.");
            return;
        }
        ReconcileOpenChests($"save slot {fileIndex} load");
    }

    private static bool IsBootstrapSave(uint fileIndex) => fileIndex is 13 or 16;

    private bool HasCallingCardAccess(PalaceLocation palace) =>
        _apClient.ProgressivePalaceAccess
            ? _apClient.ProgressiveCallingCardCount >= palace.ProgressiveIndex
            : _apClient.HasReceivedMergedPalaceItem(palace.MergedAccessItemId);

    private bool MayReportPalaceClear(PalaceLocation palace)
    {
        // Save 13 deliberately carries Kamoshida's vanilla clear bit so Mementos is open.
        // The hub also restores it after every Kamoshida visit. Only a BIT_ON(200) that occurs
        // during synthetic raid state is an actual boss clear and therefore an AP check.
        return palace.Major != 151 || _bitFlags.IsSet(0x20000000 + 80);
    }

    private PalaceLocation? GetActiveApPalace()
    {
        var (major, saveSlot) = _apClient.ActiveApPalace;
        if (major == 0 || saveSlot != _loadedSaveSlot) return null;
        return _palaces.Locations.FirstOrDefault(location => location.Major == major);
    }

    private void ClearActiveApPalace() => _apClient.ClearActiveApPalace();

    private int GetCurrentMementosPath()
    {
        // A live zero means this is not a regenerating Path; never substitute a stale
        // checkpoint selection from another field. Fallback only if native probing failed.
        return _mementosPathMonitor is not null ? _mementosPathMonitor.CurrentPath
            : Volatile.Read(ref _selectedMementosPath);
    }

    private long? SyntheticFeatureOverride(uint featureId)
    {
        if (ConnectionMenuFeature(featureId) is { } connectionResult) return connectionResult;
        if (featureId == ActiveApLaboratoryFeatureId)
            return GetActiveApPalace()?.Major == 162 ? 1 : 0;

        if (featureId == ActiveApBankFeatureId)
            return GetActiveApPalace()?.Major == 154 ? 1 : 0;

        if (featureId == 900138)
        {
            // Reserved old procedure34 callback: deliberately inert for compatibility.
            return 0;
        }
        if (featureId >= PartyLevelTargetFeatureIdBase &&
            featureId < PartyLevelTargetFeatureIdBase + PartyLevelTargetFeatureIdRange)
        {
            var encoded = (int)(featureId - PartyLevelTargetFeatureIdBase);
            var member = encoded / 100;
            var currentLevel = encoded % 100;
            var target = _apClient.PreparePartyLevelTarget(member, currentLevel);
            if (target > currentLevel)
            {
                _log.Info($"Attempting shared AP level backlog for member {member}: Lv {currentLevel} -> {target}.");
                if (member == 1)
                {
                    try
                    {
                        if (_jokerLevelManipulator is null) return currentLevel;
                        _jokerLevelManipulator.ApplyTarget(target);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Joker level reward remains pending: {ex.Message}");
                        return currentLevel;
                    }
                }
            }
            return target;
        }
        if (featureId >= 930000 && featureId < 931100)
        {
            var encoded = (int)(featureId - 930000);
            var member = encoded / 100;
            var actualLevel = encoded % 100;
            var remainingTarget = _apClient.PreparePartyLevelTarget(member, actualLevel);
            if (remainingTarget == actualLevel)
                _log.Info($"AP level reconciliation verified member {member} at Lv {actualLevel}.");
            else
                _log.Info($"AP level reward still pending for member {member}: actual Lv {actualLevel}, target {remainingTarget}.");
            return 0;
        }

        if (featureId == DepthsAccessFeatureId)
        {
            var have = _apClient.PalaceProgressCount;
            var tier = _apClient.DepthsAccessTier;
            var granted = _apClient.HasDepthsAccess;
            _log.Info($"Depths access check: Palace progress tier {tier}, have {have} -> {(granted ? "granted" : "locked")}.");
            return granted ? 1 : 0;
        }

        if (featureId >= SetMementosPathFeatureIdBase &&
            featureId <= SetMementosPathFeatureIdBase + ApClient.MementosPathCount)
        {
            var path = (int)(featureId - SetMementosPathFeatureIdBase);
            Volatile.Write(ref _selectedMementosPath, path);
            _mementosPathMonitor?.SetCurrentPath(path);
            if (path > 0)
                _log.Info($"AP checkpoint selected Mementos Path {path}; primed chest classification before field load.");
            return path;
        }

        if (featureId == ConnectionReadyFeatureId)
            return _apClient.ConnectionReady ? 1 : 0;

        if (featureId == HubMenuOpenedFeatureId)
        {
            Interlocked.Exchange(ref _hubMenuOpen, 1);
            return 1;
        }

        if (featureId == HubMenuClosedFeatureId)
        {
            Interlocked.Exchange(ref _hubMenuOpen, 0);
            return 1;
        }

        if (featureId == ClearActiveApPalaceFeatureId)
        {
            ClearActiveApPalace();
            return 1;
        }

        if (featureId == ConsumeDeathLinkDamageFeatureId)
        {
            // Legacy BF procedure 28 remains index-compatible but no longer consumes links.
            // Damage is driven by the native HP-reader callback, not a separate flow task.
            return 0;
        }

        if (featureId == BeginConfidantShopFeatureId)
        {
            _chestMonitor.SetShopDescriptionsEnabled(false);
            lock (_confidantShopLock) _confidantShopOffers.Clear();
            _chestMonitor.ClearConfidantShopNames();
            Interlocked.Exchange(ref _confidantShopReturnRequested, 0);
            var announceHints = _apClient.ConnectionReady &&
                Interlocked.CompareExchange(ref _confidantShopHintPassAvailable, 0, 1) == 1;
            Interlocked.Exchange(ref _confidantShopHintsThisOpen, announceHints ? 1 : 0);
            if (_apClient.NumberedShopEnabled)
            {
                _numberedShopPage = _apClient.MissingNumberedShopLocations()
                    .Where(id => NumberedShopTier(id) <= _apClient.PalaceProgressCount)
                    .Take(ShopRows.Length).ToArray();
                if (announceHints) _apClient.AnnounceNumberedShopHints();
            }
            Interlocked.Exchange(ref _confidantShopActive, 1);
            _chestMonitor.SetShopDescriptionsEnabled(true);
            _log.Info(announceHints
                ? "Opened the native Archipelago Confidant shop controller (first automatic hint pass)."
                : "Opened the native Archipelago Confidant shop controller (automatic hints already sent)." );
            return 1;
        }

        if (featureId == EndConfidantShopFeatureId)
        {
            _chestMonitor.SetShopDescriptionsEnabled(false);
            Interlocked.Exchange(ref _confidantShopActive, 0);
            _chestMonitor.ClearConfidantShopNames();
            _log.Info("Closed the native Archipelago Confidant shop controller.");
            return 1;
        }

        if (featureId == ConsumeConfidantShopReturnFeatureId)
            return Interlocked.Exchange(ref _confidantShopReturnRequested, 0);

        if (featureId >= SetActiveApPalaceFeatureIdBase &&
            featureId < SetActiveApPalaceFeatureIdBase + SetActiveApPalaceFeatureIdCount)
        {
            var progressiveIndex = (int)(featureId - SetActiveApPalaceFeatureIdBase) + 1;
            var palace = _palaces.Locations.FirstOrDefault(location => location.ProgressiveIndex == progressiveIndex);
            if (palace is null)
            {
                _log.Error($"Could not start AP Palace session for unknown tier {progressiveIndex}.");
                return 0;
            }

            _apClient.SetActiveApPalace(palace.Major, _loadedSaveSlot);
            _log.Info($"Started durable AP Palace session for '{palace.Name}' (save {_loadedSaveSlot?.ToString() ?? "unknown"}).");
            return 1;
        }

        if (featureId == ApplyPendingTvBundlesFeatureId)
        {
            var applied = 0;
            foreach (var itemId in _apClient.PendingTvBundleItemIds)
            {
                if (!_tvBundles.TryGet(itemId, out var bundle))
                {
                    _log.Error($"Pending TV bundle 0x{itemId:X} is absent from the embedded catalog; leaving it queued.");
                    continue;
                }

                foreach (var content in bundle.Contents)
                    _chestMonitor.GrantItem(content.ItemId, content.Count);
                _apClient.AcknowledgeTvBundle(itemId);
                applied++;
                _log.Info($"Applied AP filler bundle '{bundle.Name}' at the Mementos hub.");
            }
            return applied;
        }

        if (featureId == BaseSave13NeedsPreparationFeatureId)
        {
            if (!_loadedSaveSlot.HasValue || !IsBootstrapSave(_loadedSaveSlot.Value) ||
                _bootstrapPreparedForCurrentSession)
                return 0;

            // Never reopen chests merely because State.json was deleted or its format changed.
            // Wait until slot data and checked locations have synchronized. A genuinely fresh
            // AP slot has zero completed locations and receives the one-time game-flag reset;
            // an established slot keeps its in-game flags so already-sent checks still look
            // opened. Marking it prepared also releases the deferred save reconciliation.
            var completedLocations = _apClient.CompletedLocationCount;
            if (completedLocations < 0)
            {
                _log.Info("Bootstrap chest cleanup is waiting for the first compatible Archipelago connection.");
                return 0;
            }

            if (completedLocations > 0)
            {
                _bootstrapPreparedForCurrentSession = true;
                _apClient.MarkBootstrapPrepared(BootstrapPreparationVersion);
                _log.Info($"Preserved bootstrap chest flags because the connected AP slot already has {completedLocations} completed location(s); reconciling without reopening chests.");
                ReconcileOpenChests($"existing AP slot with {completedLocations} completed location(s)");
                return 0;
            }

            _log.Info("Connected to a fresh AP slot with zero completed locations; enabling one-time bootstrap chest/card cleanup.");
            return 1;
        }

        if (featureId == BaseSave13PreparedFeatureId)
        {
            _bootstrapPreparedForCurrentSession = true;
            _apClient.MarkBootstrapPrepared(BootstrapPreparationVersion);
            // The fresh-room cleanup has just cleared every chest and card flag. Do not run
            // general reconciliation here: Save 13 has a few unavoidable story-granted CMM
            // abilities, and treating those as newly-earned checks is what caused Fool,
            // Magician, and Moon locations to fire on first login.
            _log.Info($"AP bootstrap cleanup v{BootstrapPreparationVersion} acknowledged for save {_loadedSaveSlot}; fresh-room reconciliation intentionally skipped.");
            return 1;
        }

        if (featureId >= MementosAccessFeatureIdBase &&
            featureId < MementosAccessFeatureIdBase + MementosAccessFeatureIdCount)
        {
            var requiredTier = (int)(featureId - MementosAccessFeatureIdBase) + 1;
            var access = _apClient.ProgressiveMementosAccessCount >= requiredTier;
            _log.Info($"Mementos checkpoint access check: tier {requiredTier}, have {_apClient.ProgressiveMementosAccessCount} -> {(access ? "granted" : "locked")}.");
            return access ? 1 : 0;
        }

        if (featureId >= PartyRecruitFeatureIdBase &&
            featureId < PartyRecruitFeatureIdBase + ApClient.PartyRecruitItems.Count)
        {
            var itemId = 0x5052_1010L + featureId - PartyRecruitFeatureIdBase;
            return _apClient.HasReceivedPartyRecruitItem(itemId) ? 1 : 0;
        }

        if (_palaces.TryGetByAccessSyntheticFeatureId(featureId, out var accessPalace))
        {
            bool access;
            if (_apClient.ProgressivePalaceAccess)
            {
                access = _apClient.ProgressiveAccessCount >= accessPalace.ProgressiveIndex;
                _log.Info($"Palace access check: '{accessPalace.Name}' (tier {accessPalace.ProgressiveIndex}, have {_apClient.ProgressiveAccessCount}) -> {(access ? "granted" : "locked")}.");
            }
            else
            {
                access = _apClient.HasReceivedMergedPalaceItem(accessPalace.MergedAccessItemId);
                _log.Info($"Palace access check: '{accessPalace.Name}' (merged item) -> {(access ? "granted" : "locked")}.");
            }
            return access ? 1 : 0;
        }

        if (_palaces.TryGetByCallingCardSyntheticFeatureId(featureId, out var cardPalace))
        {
            var hasCard = HasCallingCardAccess(cardPalace);
            if (_apClient.ProgressivePalaceAccess)
            {
                _log.Info($"Calling card check: '{cardPalace.CallingCardName}' (tier {cardPalace.ProgressiveIndex}, have {_apClient.ProgressiveCallingCardCount}) -> {(hasCard ? "granted" : "locked")}.");
            }
            else
            {
                _log.Info($"Calling card check: '{cardPalace.CallingCardName}' (merged item) -> {(hasCard ? "granted" : "locked")}.");
            }
            return hasCard ? 1 : 0;
        }

        if (featureId >= ConfidantShopOfferFeatureIdBase &&
            featureId < ConfidantShopOfferFeatureIdBase + ConfidantShopOfferFeatureIdRange)
        {
            var encoded = featureId - ConfidantShopOfferFeatureIdBase;
            var cmmId = (ushort)(encoded / 16);
            var currentRank = (short)(encoded % 16);
            if (_apClient.NumberedShopEnabled)
            {
                var index = Array.IndexOf(ShopRows, cmmId);
                var id = index >= 0 && index < _numberedShopPage.Length ? _numberedShopPage[index] : 0;
                var number = id > 0 ? checked((int)(id - ApClient.ShopLocationBaseId + 1)) : 0;
                var reward = id > 0 ? _apClient.ShopRewardName(id) : "";
                _log.Info($"Numbered shop Item #{number}: {reward}");
                lock (_confidantShopLock)
                    _confidantShopOffers[cmmId] = new ConfidantShopOffer(cmmId, 0,
                        id > 0 ? (short)1 : (short)0,
                        id > 0 ? $"Item #{number}" : "Unavailable",
                        id > 0 ? _apClient.ShopPrice(id) : 1,
                        id > 0 ? [id] : [],
                        id > 0 ? _apClient.ShopDescription(id) : "No available shop checks.");
                return id > 0 ? 1 : 0;
            }
            var details = _apClient.HintNextConfidantOffer(cmmId, currentRank,
                Volatile.Read(ref _confidantShopHintsThisOpen) != 0);
            var targetRank = details?.TargetRank ?? 0;
            var arcana = ConfidantArcanaNames.GetValueOrDefault(cmmId, $"Confidant {cmmId}");
            var displayName = details is null
                ? $"{arcana} - Complete"
                : details.DisplayName;
            lock (_confidantShopLock)
                _confidantShopOffers[cmmId] = new ConfidantShopOffer(cmmId, currentRank,
                    targetRank, displayName, targetRank * 5000,
                    details?.LocationIds ?? [],
                    details is null ? "No remaining checks."
                        : string.Join("\n", details.LocationIds.Select(_apClient.ShopDescription)));
            _log.Info($"Confidant shop query (cmm {cmmId}, rank {currentRank}) -> target rank {targetRank}.");
            return targetRank;
        }

        return null;
    }

    private ConfidantShopOffer? ResolveConfidantShopOffer(ushort cmmId)
    {
        lock (_confidantShopLock)
            return _confidantShopOffers.GetValueOrDefault(cmmId);
    }

    private int NumberedShopTier(long id) => 1 +
        checked((int)(id - ApClient.ShopLocationBaseId)) * 7 / Math.Max(1, _apClient.ShopItemCount - 1);

    private void PurchaseConfidantShopOffer(ushort cmmId)
    {
        ConfidantShopOffer? offer;
        lock (_confidantShopLock)
            offer = _confidantShopOffers.GetValueOrDefault(cmmId);
        if (offer is null || offer.TargetRank <= 0 || offer.LocationIds.Length == 0)
        {
            _log.Info($"Ignored unavailable Confidant shop purchase for cmm {cmmId}.");
            return;
        }
        if (_apClient.NumberedShopEnabled)
        {
            foreach (var id in offer.LocationIds) _apClient.QueueLocation(id);
            _log.Info($"Purchased {offer.DisplayName} for {offer.Price} yen.");
            lock (_confidantShopLock)
                _confidantShopOffers[cmmId] = offer with { TargetRank = 0,
                    DisplayName = $"{offer.DisplayName} - Sold", LocationIds = [] };
            return;
        }
        if (_confidantMonitor is null)
        {
            _log.Error("Could not apply Confidant shop purchase because CMM hooks are unavailable.");
            return;
        }

        // A previous buggy client may already have advanced the native rank past an AP check
        // that remains missing. Never lower that rank. Queue the exact purchased AP locations
        // directly; the ordinary CMM mutation hook may queue them too, which is idempotent.
        if (offer.TargetRank > offer.CurrentRank)
            _confidantMonitor.AdvanceToRank(cmmId, offer.CurrentRank, offer.TargetRank);
        var queued = 0;
        foreach (var locationId in offer.LocationIds)
            queued += _apClient.QueueLocation(locationId);
        _log.Info($"Purchased Confidant shop offer for cmm {cmmId}: native rank {offer.CurrentRank}, AP rank {offer.TargetRank}, queued {queued}/{offer.LocationIds.Length} location(s) for {offer.Price} yen.");

        var arcana = ConfidantArcanaNames.GetValueOrDefault(cmmId, $"Confidant {cmmId}");
        // Keep the purchased row's price stable until the public-shop controller has
        // finished its transaction. Advancing this to the next rank from inside the
        // SetItemNum hook can make a later price query deduct the next rank's price.
        // Mark it sold out for this shop session; reopening F9 rebuilds all offers from
        // the newly committed CMM rank and exposes the next check safely.
        lock (_confidantShopLock)
            _confidantShopOffers[cmmId] = new ConfidantShopOffer(cmmId,
                Math.Max(offer.CurrentRank, offer.TargetRank), 0,
                $"{arcana} - Purchased", offer.Price, []);
    }

    private void ReconcileOpenChests(string reason)
    {
        if (!_reconcileOpenedChests) return;

        try
        {
            var opened = _catalog.Locations
                .Where(location => _bitFlags.IsSet(location.Flag))
                .Select(location => location.LocationId)
                .ToArray();
            var clearedPalaces = _palaces.Locations
                // The reusable slot-13 save must retain Kamoshida's clear bit to expose
                // Mementos. It is base-save infrastructure, not proof that this AP world's
                // Kamoshida boss was defeated; live raid-state BIT_ON monitoring reports that.
                .Where(location => !_loadedSaveSlot.HasValue ||
                    !IsBootstrapSave(_loadedSaveSlot.Value) || location.Major != 151)
                .Where(location => _bitFlags.IsSet(location.ClearFlag))
                .ToArray();
            var sweptChestIds = clearedPalaces
                .SelectMany(palace => _catalog.PalaceChestIds(palace.Major))
                .ToArray();
            var sentCallingCards = _palaces.Locations
                // A stale vanilla/base-save card flag must never manufacture an AP check
                // before this world has actually supplied the corresponding permission item.
                .Where(HasCallingCardAccess)
                .Where(location => _bitFlags.IsSet(location.CallingCardFlag))
                .Select(location => location.CallingCardLocationId)
                .ToArray();
            var confidantAbilities = _apClient.NumberedShopEnabled ? [] : _confidantMonitor?.ReadCurrentlyEnabled()
                .Select(location => location.LocationId)
                .ToArray() ?? [];
            var added = _apClient.QueueLocations(opened
                .Concat(clearedPalaces.Select(palace => palace.LocationId))
                .Concat(sweptChestIds).Concat(sentCallingCards).Concat(confidantAbilities));
            _log.Info($"Reconciliation after {reason}: {opened.Length} chests open, {clearedPalaces.Length} Palaces clear ({sweptChestIds.Length} chest sweep candidates), {sentCallingCards.Length} calling cards sent, {confidantAbilities.Length} confidant abilities enabled, {added} newly queued.");
        }
        catch (Exception ex)
        {
            _log.Error($"State reconciliation after {reason} failed: {ex.Message}");
        }
    }

    private void OnPalaceCleared(PalaceLocation palace)
    {
        var chestIds = _catalog.PalaceChestIds(palace.Major);
        // One durable batch, with server-checked and locally pending IDs deduplicated by
        // the existing queue. This never opens native chests or grants their vanilla items.
        var queued = _apClient.QueueLocations(new[] { palace.LocationId }.Concat(chestIds));
        if (queued > 0)
            _log.Info($"Completed '{palace.Name}' via clear flag 0x{palace.ClearFlag:X8}; queued {queued} remaining AP location(s), including the clear and a sweep of {chestIds.Length} Palace chests.");
    }

    private void OnCallingCardSent(PalaceLocation palace)
    {
        if (_apClient.QueueLocation(palace.CallingCardLocationId) > 0)
            _log.Info($"Completed '{palace.CallingCardName}' via flag 0x{palace.CallingCardFlag:X8}; queued AP location {palace.CallingCardLocationId}.");
    }

    private void OnPersonaRegistered(PersonaLocation persona)
    {
        if (!_apClient.CompendiumSanity) return;
        if (_apClient.QueueLocation(persona.LocationId) > 0)
            _log.Info($"Compendium check '{persona.Name}' registered (Persona {persona.PersonaId}); queued AP location {persona.LocationId}.");
    }

    private void OnConfidantAbilityAcquired(ConfidantAbilityLocation ability)
    {
        if (_apClient.NumberedShopEnabled) return;
        if (_apClient.QueueLocation(ability.LocationId) > 0)
            _log.Info($"Confidant check '{ability.Name}' enabled (feature 0x{ability.FeatureId:X}); queued AP location {ability.LocationId}.");
    }

    public void Dispose()
    {
        _chestMonitor.ChestOpenedCompleted -= OnChestOpenedCompleted;
        _chestMonitor.Dispose();
        if (_palaceClearMonitor is not null)
        {
            _palaceClearMonitor.PalaceCleared -= OnPalaceCleared;
            _palaceClearMonitor.CallingCardSent -= OnCallingCardSent;
        }
        if (_confidantMonitor is not null)
        {
            _confidantMonitor.AbilityAcquired -= OnConfidantAbilityAcquired;
        }
        if (_personaMonitor is not null)
            _personaMonitor.PersonaRegistered -= OnPersonaRegistered;
        if (_saveLoadMonitor is not null)
            _saveLoadMonitor.SaveLoaded -= OnSaveLoaded;
        if (_battleResultMonitor is not null)
            _battleResultMonitor.PartyWiped -= SendCurrentConnectionDeathLink;
        _deathLinkDamageReceiver?.Dispose();
        _partyHpManipulator?.Dispose();
        _hubMenuHotkey?.Dispose();
        _messageWindowGuard?.Dispose();
        _connectionKeyboard?.Dispose();
        _hubFlowRunner?.Dispose();
        _apClient.Dispose();
    }
}
