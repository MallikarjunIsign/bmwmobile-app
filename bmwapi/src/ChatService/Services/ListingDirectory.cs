using System.Net;
using System.Text.Json;
using ChatService.Data;
using ChatService.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatService.Services;

/// <summary>A listing as AuctionService returns it (GET /api/auctions/{id}, public).</summary>
public record ListingInfo(string Id, string Seller, string? SellerName, string Title, string? ImageUrl, int StartingPrice,
    int? CurrentHighBid, string? Currency, string Status, string SaleType, string? Winner);

/// <summary>Where listings the chat hasn't heard about yet come from (AuctionService over HTTP; a fake in tests).</summary>
public interface IListingSource
{
    Task<ListingInfo?> GetAsync(string listingId, CancellationToken cancellationToken = default);
}

public class HttpListingSource(HttpClient http, ILogger<HttpListingSource> logger) : IListingSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ListingInfo?> GetAsync(string listingId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync($"api/auctions/{Uri.EscapeDataString(listingId)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<AuctionDtoView>(Json, cancellationToken);
            return dto is null ? null : new ListingInfo(dto.Id, dto.Seller, dto.SellerName, dto.Title ?? "", dto.ThumbnailUrl ?? dto.ImageUrl,
                dto.StartingPrice, dto.CurrentHighBid, dto.Currency, dto.Status, dto.SaleType ?? "Auction", dto.Winner);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "Couldn't load listing {ListingId} from AuctionService", listingId);
            throw new ListingUnavailableException();
        }
    }

    private sealed record AuctionDtoView(string Id, string Seller, string? SellerName, string? Title, string? ImageUrl, string? ThumbnailUrl,
        int StartingPrice, int? CurrentHighBid, string? Currency, string Status, string? SaleType, string? Winner);
}

public class ListingUnavailableException() : Exception("The listing can't be loaded right now");

/// <summary>The chat's copy of listings: from the events, or fetched once from AuctionService when unknown.</summary>
public class ListingDirectory(ChatDbContext db, IListingSource source)
{
    /// <summary>The listing (with its buyers), or null when it doesn't exist (or is a private draft).</summary>
    public async Task<ChatListing?> GetAsync(string listingId, CancellationToken cancellationToken = default)
    {
        var listing = await db.Listings.Include(l => l.Buyers).FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        // Known – unless it's only a placeholder from a sale that arrived before any listing event (no title yet).
        if (listing is not null && listing.Title.Length > 0) return listing;

        var info = await source.GetAsync(listingId, cancellationToken);
        if (info is null || info.Status is "Draft") return listing;

        if (listing is null) db.Listings.Add(listing = new ChatListing { Id = info.Id, Seller = info.Seller });
        Apply(listing, info);
        if (!string.IsNullOrEmpty(info.Winner) && listing.Buyers.All(b => b.Buyer != info.Winner))
            listing.Buyers.Add(new ListingBuyer { ListingId = listing.Id, Buyer = info.Winner });
        await db.SaveChangesAsync(cancellationToken);
        return listing;
    }

    public static void Apply(ChatListing listing, ListingInfo info)
    {
        listing.SellerName = info.SellerName ?? listing.SellerName;
        listing.Title = info.Title;
        listing.ImageUrl = info.ImageUrl ?? listing.ImageUrl;
        listing.StartingPrice = info.StartingPrice;
        listing.CurrentHighBid = Max(listing.CurrentHighBid, info.CurrentHighBid);
        listing.Currency = info.Currency ?? listing.Currency;
        listing.Status = info.Status;
        listing.SaleType = info.SaleType;
        listing.Deleted = info.Status == "Cancelled";
        listing.UpdatedAt = DateTime.UtcNow;
    }

    public static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
