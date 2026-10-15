using System.ComponentModel.DataAnnotations;
using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.Errors;
using AuctionService.Services;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

public record ShippingEstimateDto(
    string ListingId,
    // Pickup is always free (when the listing offers it).
    bool PickupOffered,
    bool ShippingOffered,
    ShippingQuote? Shipping);

public record ShippingRateDto(
    string CountryCode, string SizeClass, string Currency, string Unit, decimal BaseFee, decimal RatePerUnit,
    decimal MinFee, decimal MaxFee, int MaxDistance, bool IsPlaceholder, DateTime UpdatedAt, string? UpdatedBy,
    bool IsEstimate = false, string? Source = null);

public class ShippingEstimatesRequest
{
    public const int MaxListings = 50;
    public List<string> ListingIds { get; set; } = [];
    public double Lat { get; set; }
    public double Lon { get; set; }
    /// <summary>The buyer's country (shipping stays within the listing's country).</summary>
    public string? Country { get; set; }
}

public class UpdateShippingRateDto
{
    [Range(0, 10_000_000)] public decimal BaseFee { get; set; }
    [Range(0, 1_000_000)] public decimal RatePerUnit { get; set; }
    [Range(0, 10_000_000)] public decimal MinFee { get; set; }
    [Range(0, 100_000_000)] public decimal MaxFee { get; set; }
    /// <summary>In the country's unit (km, or mi in the USA).</summary>
    [Range(1, 20_000)] public int MaxDistance { get; set; }
    /// <summary>Where the rates come from, e.g. the carrier and contract ("XPO LTL contract 2027").</summary>
    [MaxLength(500)] public string? Source { get; set; }
}

/// <summary>
/// Shipping charges: calculated by the backend from the item's size class, the distance from the item to the buyer's
/// delivery location and the country's rates (see <see cref="ShippingCalculator"/>). The app never sends a price.
/// Estimates here; the exact charge is fixed on the order when the buyer chooses Shipping (OrdersController).
/// </summary>
[ApiController]
[Authorize]
[Route("api/shipping")]
public class ShippingController(AuctionDbContext context, ShippingService shipping, IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// "Shipping to Kukatpally: ~₹350 (estimate)" for a listing and the buyer's location (selected location or GPS).
    /// country = the buyer's country (shipping stays within the listing's country).
    /// </summary>
    [HttpGet("estimate")]
    public async Task<ActionResult<ShippingEstimateDto>> Estimate([FromQuery, Required] string listingId,
        [FromQuery] double lat, [FromQuery] double lon, [FromQuery] string? country)
    {
        if (!GeoMath.IsValidCoordinate(lat, lon)) return BadRequest(new { message = "lat and lon must be valid coordinates" });
        var item = await context.Set<Item>().Include(i => i.PartType).AsNoTracking().FirstOrDefaultAsync(i => i.Id == listingId);
        if (item is null) return NotFound();

        var options = OrderTracking.Normalize(item.DeliveryOptions);
        var shipped = options.HasFlag(DeliveryOptions.Shipping);
        var quote = shipped ? await shipping.QuoteAsync(item, country ?? item.CountryCode, lat, lon) : null;
        return new ShippingEstimateDto(listingId, options.HasFlag(DeliveryOptions.Pickup), shipped, quote);
    }

    /// <summary>
    /// Estimates for a page of search results (the cards' "🚚 Ships to you ~$12"), one call instead of one per card.
    /// Listings that don't ship come back with Shipping = null; unknown ids are left out.
    /// </summary>
    [HttpPost("estimates")]
    public async Task<ActionResult<List<ShippingEstimateDto>>> Estimates(ShippingEstimatesRequest request)
    {
        if (!GeoMath.IsValidCoordinate(request.Lat, request.Lon)) return BadRequest(new { message = "lat and lon must be valid coordinates" });
        var ids = request.ListingIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count > ShippingEstimatesRequest.MaxListings)
            return BadRequest(new { message = $"At most {ShippingEstimatesRequest.MaxListings} listings at a time" });

        var items = await context.Set<Item>().Include(i => i.PartType).AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync();
        var result = new List<ShippingEstimateDto>();
        foreach (var item in items.OrderBy(i => ids.IndexOf(i.Id)))
        {
            var options = OrderTracking.Normalize(item.DeliveryOptions);
            var shipped = options.HasFlag(DeliveryOptions.Shipping);
            var quote = shipped ? await shipping.QuoteAsync(item, request.Country ?? item.CountryCode, request.Lat, request.Lon) : null;
            result.Add(new ShippingEstimateDto(item.Id, options.HasFlag(DeliveryOptions.Pickup), shipped, quote));
        }
        return result;
    }

    /// <summary>The rate table (a country, or all). Placeholder rows are marked.</summary>
    [HttpGet("rates")]
    public async Task<ActionResult<List<ShippingRateDto>>> Rates([FromQuery] string? country)
    {
        var query = context.ShippingRates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(r => r.CountryCode == country.ToUpper());
        var rates = await query.OrderBy(r => r.CountryCode).ThenBy(r => r.SizeClass).ToListAsync();
        return rates.Select(ToDto).ToList();
    }

    /// <summary>Admin: set the real values for one country and size class (replaces the placeholder).</summary>
    [HttpPut("rates/{country}/{sizeClass}")]
    public async Task<ActionResult<ShippingRateDto>> UpdateRate(string country, string sizeClass, UpdateShippingRateDto dto)
    {
        if (!IsAdmin()) return Forbidden.With(AppRoles.AdminOnlyMessage, AppRoles.AdminOnlyCode);
        var code = country.ToUpperInvariant();
        if (!RegionRules.IsSupported(code)) return BadRequest(new { message = "country must be IN, US, NG or IE" });
        if (!Enum.TryParse<SizeClass>(sizeClass, true, out var size) || !Enum.IsDefined(size))
            return BadRequest(new { message = "sizeClass must be Small, Medium, Large, Freight or Vehicle" });
        if (dto.MaxFee < dto.MinFee) return BadRequest(new { message = "maxFee must be at least minFee" });

        var rate = await context.ShippingRates.FirstOrDefaultAsync(r => r.CountryCode == code && r.SizeClass == size);
        if (rate is null)
        {
            rate = new ShippingRate { CountryCode = code, SizeClass = size, Currency = RegionRules.CurrencyFor(code), Unit = RegionRules.DistanceUnit(code) };
            context.ShippingRates.Add(rate);
        }
        rate.BaseFee = dto.BaseFee;
        rate.RatePerUnit = dto.RatePerUnit;
        rate.MinFee = dto.MinFee;
        rate.MaxFee = dto.MaxFee;
        rate.MaxDistance = dto.MaxDistance;
        rate.IsPlaceholder = false;
        // Real rates from the admin: no longer an estimate.
        rate.IsEstimate = false;
        rate.Source = dto.Source?.Trim() is { Length: > 0 } source ? source : null;
        rate.UpdatedAt = DateTime.UtcNow;
        rate.UpdatedBy = User.Identity?.Name;
        await context.SaveChangesAsync();
        // Existing orders keep the charge they were given.
        return ToDto(rate);
    }

    /// <summary>Admins: token role "admin", or listed in the "Admin:Usernames" setting.</summary>
    private bool IsAdmin() => AppRoles.IsAdmin(User) ||
        User.Identity?.Name is { } user &&
        (configuration.GetSection("Admin:Usernames").Get<string[]>() ?? []).Contains(user, StringComparer.OrdinalIgnoreCase);

    private static ShippingRateDto ToDto(ShippingRate r) => new(r.CountryCode, r.SizeClass.ToString(), r.Currency, r.Unit,
        r.BaseFee, r.RatePerUnit, r.MinFee, r.MaxFee, r.MaxDistance, r.IsPlaceholder, r.UpdatedAt, r.UpdatedBy, r.IsEstimate, r.Source);
}
