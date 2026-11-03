namespace P5RArchipelago;

internal sealed record ConfidantShopOffer(
    ushort CmmId,
    short CurrentRank,
    short TargetRank,
    string DisplayName,
    int Price,
    long[] LocationIds,
    string Description = "");

internal sealed record ConfidantShopOfferDetails(
    short TargetRank,
    string DisplayName,
    long[] LocationIds);
