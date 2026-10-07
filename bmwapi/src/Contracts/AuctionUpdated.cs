namespace Contracts;

/// <summary>
/// Published when an auction's item, pricing, end time, images or container link change.
/// Carries the full snapshot so consumers can apply it without partial-update gaps.
/// </summary>
public class AuctionUpdated : AuctionSnapshot;