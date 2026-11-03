using System.Text.Json;

namespace P5RArchipelago;

internal sealed class StateStore
{
    private readonly string _path;
    private readonly string _serverAddress;
    private readonly string _slotName;
    private readonly string _catalogIdentity;
    private readonly Log _log;
    private readonly object _lock = new();
    private ClientState _state;

    public StateStore(string path, string serverAddress, string slotName, string catalogIdentity, Log log)
    {
        _path = path;
        _serverAddress = serverAddress;
        _slotName = slotName;
        _catalogIdentity = catalogIdentity;
        _log = log;
        _state = LoadMatchingState();
    }

    public int NextReceivedItemIndex
    {
        get { lock (_lock) return _state.NextReceivedItemIndex; }
    }

    public long[] PendingLocations
    {
        get { lock (_lock) return [.. _state.PendingLocations]; }
    }

    public bool GoalReported
    {
        get { lock (_lock) return _state.GoalReported; }
    }

    public int ProgressiveAccessCount
    {
        get { lock (_lock) return _state.ProgressiveAccessCount; }
    }

    public int ProgressiveCallingCardCount
    {
        get { lock (_lock) return _state.ProgressiveCallingCardCount; }
    }

    public int ProgressiveMementosAccessCount
    {
        get { lock (_lock) return _state.ProgressiveMementosAccessCount; }
    }

    public int RecordProgressiveAccessItem(int nextReceivedItemIndex)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.ProgressiveAccessCount++;
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
            return _state.ProgressiveAccessCount;
        }
    }

    public int RecordProgressiveCallingCardItem(int nextReceivedItemIndex)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.ProgressiveCallingCardCount++;
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
            return _state.ProgressiveCallingCardCount;
        }
    }

    public int RecordProgressiveMementosAccessItem(int nextReceivedItemIndex)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.ProgressiveMementosAccessCount++;
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
            return _state.ProgressiveMementosAccessCount;
        }
    }

    public bool HasReceivedPartyRecruitItem(long itemId)
    {
        lock (_lock) return _state.ReceivedPartyRecruitItemIds.Contains(itemId);
    }

    public void RecordPartyRecruitItem(int nextReceivedItemIndex, long itemId)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.ReceivedPartyRecruitItemIds.Add(itemId);
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
        }
    }

    public int RecordPartyLevelItem(int nextReceivedItemIndex, int levels)
    {
        if (levels is not (1 or 3 or 5))
            throw new ArgumentOutOfRangeException(nameof(levels));
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.PartyLevelCreditsReceived += levels;
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
            return _state.PartyLevelCreditsReceived;
        }
    }

    /// <summary>Returns an absolute native level target for one member. A pending target is
    /// committed only after a later hub pass observes that the game reached it, making an
    /// interrupted level write safe to retry without applying the reward twice.</summary>
    public int PreparePartyLevelTarget(int member, int currentLevel)
    {
        if (member is < 1 or > 10 || currentLevel is < 1 or > 99)
            return Math.Clamp(currentLevel, 0, 99);

        lock (_lock)
        {
            var changed = false;
            if (_state.PartyLevelPendingByMember.TryGetValue(member, out var pending))
            {
                if (currentLevel < pending.ExpectedLevel)
                {
                    // Fold newly received rewards into an interrupted target instead of
                    // requiring several hub visits to work through stale pending batches.
                    var extra = _state.PartyLevelCreditsReceived -
                        _state.PartyLevelCreditsAppliedByMember.GetValueOrDefault(member) - pending.Credits;
                    if (extra > 0)
                    {
                        pending.Credits += extra;
                        pending.ExpectedLevel = Math.Min(99, pending.ExpectedLevel + extra);
                        SaveLocked();
                    }
                    return pending.ExpectedLevel;
                }

                _state.PartyLevelCreditsAppliedByMember[member] =
                    _state.PartyLevelCreditsAppliedByMember.GetValueOrDefault(member) + pending.Credits;
                _state.PartyLevelAchievedFloorByMember[member] = Math.Max(
                    _state.PartyLevelAchievedFloorByMember.GetValueOrDefault(member), pending.ExpectedLevel);
                _state.PartyLevelPendingByMember.Remove(member);
                changed = true;
            }

            var applied = _state.PartyLevelCreditsAppliedByMember.GetValueOrDefault(member);
            var outstanding = _state.PartyLevelCreditsReceived - applied;
            var baseline = Math.Max(currentLevel,
                _state.PartyLevelAchievedFloorByMember.GetValueOrDefault(member));
            if (outstanding <= 0 && currentLevel >= baseline)
            {
                if (changed) SaveLocked();
                return currentLevel;
            }

            var target = Math.Min(99, baseline + Math.Max(0, outstanding));
            _state.PartyLevelPendingByMember[member] = new PartyLevelPendingApplication
            {
                Credits = Math.Max(0, outstanding),
                ExpectedLevel = target,
            };
            SaveLocked();
            return target;
        }
    }

    public bool HasReceivedMergedPalaceItem(long itemId)
    {
        lock (_lock) return _state.ReceivedMergedPalaceItemIds.Contains(itemId);
    }

    public void RecordMergedPalaceItem(int nextReceivedItemIndex, long itemId)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.ReceivedMergedPalaceItemIds.Add(itemId);
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
        }
    }

    public int ConfidantHangoutsUsedFor(int cmmId)
    {
        lock (_lock) return _state.ConfidantHangoutsUsedByCmmId.GetValueOrDefault(cmmId);
    }

    public void IncrementConfidantHangoutsUsed(int cmmId)
    {
        lock (_lock)
        {
            _state.ConfidantHangoutsUsedByCmmId[cmmId] =
                _state.ConfidantHangoutsUsedByCmmId.GetValueOrDefault(cmmId) + 1;
            SaveLocked();
        }
    }

    public int IncrementMementosChestsOpened(int path)
    {
        lock (_lock)
        {
            if (path is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(path));
            _state.MementosChestsOpenedByPath[path - 1]++;
            SaveLocked();
            return _state.MementosChestsOpenedByPath[path - 1];
        }
    }

    public int MementosChestsOpenedForPath(int path)
    {
        lock (_lock)
        {
            if (path is < 1 or > 9) return 0;
            return _state.MementosChestsOpenedByPath[path - 1];
        }
    }

    public long[] PendingTvBundleItemIds
    {
        get { lock (_lock) return [.. _state.PendingTvBundleItemIds]; }
    }

    public int BootstrapPreparationVersion
    {
        get { lock (_lock) return _state.BootstrapPreparationVersion; }
    }

    public (int Major, uint? SaveSlot) ActiveApPalace
    {
        get { lock (_lock) return (_state.ActiveApPalaceMajor, _state.ActiveApPalaceSaveSlot); }
    }

    public void SetActiveApPalace(int major, uint? saveSlot)
    {
        lock (_lock)
        {
            _state.ActiveApPalaceMajor = major;
            _state.ActiveApPalaceSaveSlot = saveSlot;
            SaveLocked();
        }
    }

    public void ClearActiveApPalace()
    {
        lock (_lock)
        {
            if (_state.ActiveApPalaceMajor == 0 && _state.ActiveApPalaceSaveSlot is null) return;
            _state.ActiveApPalaceMajor = 0;
            _state.ActiveApPalaceSaveSlot = null;
            SaveLocked();
        }
    }

    public void RecordPendingTvBundle(int nextReceivedItemIndex, long itemId)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.PendingTvBundleItemIds.Add(itemId);
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
        }
    }

    public void RemovePendingTvBundle(long itemId)
    {
        lock (_lock)
        {
            var index = _state.PendingTvBundleItemIds.IndexOf(itemId);
            if (index >= 0)
            {
                _state.PendingTvBundleItemIds.RemoveAt(index);
                SaveLocked();
            }
        }
    }

    public void MarkBootstrapPrepared(int version)
    {
        lock (_lock)
        {
            _state.BootstrapPreparationVersion = version;
            SaveLocked();
        }
    }

    /// <summary>Scopes all durable receive/check state to the actual AP room. Server address,
    /// slot, and catalog can remain identical across regenerated multiworlds, so they are not
    /// sufficient isolation on their own.</summary>
    public void BindSeed(string seedName)
    {
        lock (_lock)
        {
            if (string.Equals(_state.SeedName, seedName, StringComparison.Ordinal)) return;

            if (!string.IsNullOrEmpty(_state.SeedName))
                _log.Info($"AP room changed from '{_state.SeedName}' to '{seedName}'; clearing isolated client state.");
            else
                _log.Info($"Bound client state to AP room '{seedName}'.");

            _state = new ClientState
            {
                ServerAddress = _serverAddress,
                SlotName = _slotName,
                CatalogIdentity = _catalogIdentity,
                SeedName = seedName,
            };
            SaveLocked();
        }
    }

    public int AddPendingLocations(IEnumerable<long> locations)
    {
        lock (_lock)
        {
            var before = _state.PendingLocations.Count;
            _state.PendingLocations.UnionWith(locations);
            var added = _state.PendingLocations.Count - before;
            if (added > 0) SaveLocked();
            return added;
        }
    }

    public void RemovePendingLocations(IEnumerable<long> locations)
    {
        lock (_lock)
        {
            _state.PendingLocations.ExceptWith(locations);
            SaveLocked();
        }
    }

    public void RecordReceivedItem(int nextReceivedItemIndex)
    {
        lock (_lock)
        {
            RequireNextReceivedItemIndex(nextReceivedItemIndex);
            _state.NextReceivedItemIndex = nextReceivedItemIndex;
            SaveLocked();
        }
    }

    private void RequireNextReceivedItemIndex(int nextReceivedItemIndex)
    {
        if (nextReceivedItemIndex != _state.NextReceivedItemIndex + 1)
            throw new InvalidOperationException(
                $"Received-item index must advance from {_state.NextReceivedItemIndex} to {_state.NextReceivedItemIndex + 1}, not {nextReceivedItemIndex}.");
    }

    public void MarkGoalReported()
    {
        lock (_lock)
        {
            _state.GoalReported = true;
            SaveLocked();
        }
    }

    private ClientState LoadMatchingState()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<ClientState>(File.ReadAllText(_path));
                if (loaded is not null && loaded.FormatVersion is 15 or 16 &&
                    loaded.ServerAddress == _serverAddress && loaded.SlotName == _slotName &&
                    loaded.CatalogIdentity == _catalogIdentity)
                {
                    loaded.MementosChestsOpenedByPath ??= new int[9];
                    if (loaded.MementosChestsOpenedByPath.Length != 9)
                        loaded.MementosChestsOpenedByPath = new int[9];
                    loaded.PendingTvBundleItemIds ??= [];
                    loaded.PartyLevelCreditsAppliedByMember ??= [];
                    loaded.PartyLevelPendingByMember ??= [];
                    loaded.PartyLevelAchievedFloorByMember ??= [];
                    if (loaded.FormatVersion == 15)
                    {
                        loaded.FormatVersion = 16;
                        _log.Info("Migrated AP state format 15 to 16 without resetting the active room.");
                    }
                    _log.Info($"Loaded AP state from {_path}.");
                    return loaded;
                }

                _log.Info("State.json belongs to another server, slot, or world catalog; starting isolated state.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Could not load State.json; starting clean. {ex.Message}");
        }

        return new ClientState
        {
            ServerAddress = _serverAddress,
            SlotName = _slotName,
            CatalogIdentity = _catalogIdentity,
        };
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_state, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        File.Move(temporaryPath, _path, true);
    }
}
