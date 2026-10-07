namespace Contracts;

/// <summary>
/// Published by AuctionService when a container's details change, so the auctions linked to it
/// can be refreshed in the search index.
/// </summary>
public class ContainerUpdated : ContainerInfo
{
    public List<string> AuctionIds { get; set; } = [];
}