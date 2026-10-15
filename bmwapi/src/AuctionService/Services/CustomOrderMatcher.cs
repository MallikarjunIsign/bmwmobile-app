using System.Text.RegularExpressions;
using AuctionService.Data;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Services;

/// <summary>
/// When a listing is published, finds the buyers' open custom orders it satisfies, marks them Matched and sends
/// CustomOrderMatched (in the same transaction, through the outbox) so the buyer gets "A part matching your
/// request is now available".
///
/// A listing matches an order when
///  - it isn't the buyer's own listing, and
///  - the normalised part numbers are equal, OR every word of the ordered part type appears in the listing's part
///    name/description and the vehicle fits (make, model and year when the order gives them), and
///  - the buyer would see it (Contracts.ListingVisibility): a pickup-only listing within the buyer's radius, a listing
///    that ships anywhere in the buyer's country – never one from another country.
/// </summary>
public partial class CustomOrderMatcher(AuctionDbContext context)
{
    /// <summary>Marks matching open orders and enlists the notifications; the caller saves and flushes the outbox.</summary>
    public async Task<List<PartRequest>> MatchNewListingAsync(Auction auction, IDbContextOutbox<AuctionDbContext> outbox)
    {
        if (auction.Item.ItemType != ItemType.Part) return [];

        var open = await context.PartRequests
            .Where(x => x.Status == CustomOrderStatus.Open && x.User != auction.Seller)
            .ToListAsync();

        var matched = new List<PartRequest>();
        foreach (var order in open)
        {
            if (!Matches(order, auction, out var distanceKm)) continue;

            order.Status = CustomOrderStatus.Matched;
            order.MatchedAuctionId = auction.Id;
            order.MatchedAt = DateTime.UtcNow;
            order.UpdatedAt = order.MatchedAt.Value;
            matched.Add(order);

            await outbox.PublishAsync(new CustomOrderMatched
            {
                OrderId = order.Id,
                User = order.User,
                AuctionId = auction.Id,
                Title = Title(auction.Item),
                DistanceKm = distanceKm is null ? null : Math.Round(distanceKm.Value, 1)
            });
        }

        return matched;
    }

    public static bool Matches(PartRequest order, Auction auction, out double? distanceKm)
    {
        distanceKm = null;
        var item = auction.Item;
        if (order.Status != CustomOrderStatus.Open || order.User == auction.Seller || item.ItemType != ItemType.Part)
            return false;

        if (!Visible(order, item, out distanceKm)) return false;

        var orderNumber = order.PartNumberNormalized ?? PartNumbers.Normalize(order.PartNumber);
        if (orderNumber != null && orderNumber == PartNumbers.Normalize(item.PartNumber)) return true;

        return PartTypeMatches(order, item) && VehicleFits(order, item);
    }

    private static bool Visible(PartRequest order, Item item, out double? distanceKm)
    {
        distanceKm = order.Latitude is { } lat && order.Longitude is { } lon && item.Latitude is { } iLat && item.Longitude is { } iLon
            ? GeoMath.HaversineKm(lat, lon, iLat, iLon)
            : null;
        // Neither the buyer's location nor their country is known: anywhere.
        if (order.Latitude is null && ListingVisibility.CountryOf(order.CountryCode, null, null) is null) return true;

        var ships = OrderTracking.Normalize(item.DeliveryOptions).HasFlag(DeliveryOptions.Shipping);
        return ListingVisibility.IsVisible(order.CountryCode, order.Latitude, order.Longitude,
            order.RadiusKm is > 0 ? order.RadiusKm.Value : RegionRules.DefaultSearchRadius(order.CountryCode).Km,
            item.CountryCode, item.Latitude, item.Longitude, ships);
    }

    /// <summary>
    /// Every word of the ordered part type ("front bumper", "hood") appears in the listing's title, part type and its
    /// synonyms, position or description.
    /// </summary>
    private static bool PartTypeMatches(PartRequest order, Item item)
    {
        var wanted = Words(order.PartName);
        if (wanted.Count == 0) return false;
        var synonyms = item.PartType is null ? "" : string.Join(' ', item.PartType.Synonyms);
        var listed = Words($"{item.Title} {item.PartName} {item.PartType?.Name} {synonyms} {item.PartDetails?.Position} {item.PartDetails?.Side} {item.Description}");
        return wanted.All(listed.Contains);
    }

    /// <summary>Universal / size-based parts fit any vehicle; otherwise one fitment row must cover the ordered vehicle.</summary>
    private static bool VehicleFits(PartRequest order, Item item)
    {
        if (item.FitmentMode is FitmentMode.Universal or FitmentMode.SizeBased) return true;

        var rows = item.Fitments.Count > 0
            ? item.Fitments.Select(f => (f.Make, f.Model, From: f.YearFrom, To: f.YearTo))
            : [(item.Make, item.Model, From: item.Year, To: item.YearTo ?? item.Year)];

        return rows.Any(r =>
            (string.IsNullOrWhiteSpace(order.Make) || Same(order.Make, r.Make)) &&
            (string.IsNullOrWhiteSpace(order.Model) || Same(order.Model, r.Model) || Words(r.Model).IsSupersetOf(Words(order.Model))) &&
            (order.Year <= 0 || order.Year >= r.From && order.Year <= r.To));
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> StopWords = ["the", "and", "for", "with", "a", "an", "of", "part", "parts"];

    private static HashSet<string> Words(string? text) =>
        WordRegex().Matches(text?.ToLowerInvariant() ?? "")
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .ToHashSet();

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();

    private static string Title(Item item) =>
        item.ItemType == ItemType.Part && !string.IsNullOrWhiteSpace(item.PartName)
            ? $"{item.PartName} – {item.Make} {item.Model} {item.Year}"
            : $"{item.Year} {item.Make} {item.Model}";
}
