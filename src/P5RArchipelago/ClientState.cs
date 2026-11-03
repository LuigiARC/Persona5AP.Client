namespace P5RArchipelago;

internal sealed class ClientState
{
    public int FormatVersion { get; set; } = 16;
    public string ServerAddress { get; set; } = "";
    public string SlotName { get; set; } = "";
    public string CatalogIdentity { get; set; } = "";
    public string SeedName { get; set; } = "";
    public int NextReceivedItemIndex { get; set; }
    public HashSet<long> PendingLocations { get; set; } = [];
    public bool GoalReported { get; set; }

    public int ProgressiveAccessCount { get; set; }
    public int ProgressiveCallingCardCount { get; set; }
    public int ProgressiveMementosAccessCount { get; set; }

    public HashSet<long> ReceivedPartyRecruitItemIds { get; set; } = [];

    public int PartyLevelCreditsReceived { get; set; }
    public Dictionary<int, int> PartyLevelCreditsAppliedByMember { get; set; } = [];
    public Dictionary<int, PartyLevelPendingApplication> PartyLevelPendingByMember { get; set; } = [];
    public Dictionary<int, int> PartyLevelAchievedFloorByMember { get; set; } = [];

    public HashSet<long> ReceivedMergedPalaceItemIds { get; set; } = [];

    public Dictionary<int, int> ConfidantHangoutsUsedByCmmId { get; set; } = [];

    public int[] MementosChestsOpenedByPath { get; set; } = new int[9];

    public List<long> PendingTvBundleItemIds { get; set; } = [];

    public int BootstrapPreparationVersion { get; set; }

    public int ActiveApPalaceMajor { get; set; }
    public uint? ActiveApPalaceSaveSlot { get; set; }
}

internal sealed class PartyLevelPendingApplication
{
    public int Credits { get; set; }
    public int ExpectedLevel { get; set; }
}
