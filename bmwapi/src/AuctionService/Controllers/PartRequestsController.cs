using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Errors;
using Contracts;
using Wolverine.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// Custom orders: "There are no parts available for this search – create a custom order and get notified when the
/// part is available". Each buyer sees only their own orders. A new request is announced to every seller
/// (PartRequestCreated → live + push); sellers can list the open requests ("Buyer requests"). When a seller publishes
/// a matching part, the buyer is told (CustomOrderMatcher → CustomOrderMatched).
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class PartRequestsController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox) : ControllerBase
{
    private string Username => User.Identity?.Name ?? throw new UnauthorizedAccessException();

    /// <summary>The signed-in user's custom orders, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<PartRequestDto>>> GetPartRequests()
    {
        var requests = await context.PartRequests
            .AsNoTracking()
            .Where(x => x.User == Username)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return requests.Select(MapToDto).ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PartRequestDto>> GetPartRequest(string id)
    {
        var req = await context.PartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (req == null || req.User != Username) return NotFound();
        return MapToDto(req);
    }

    [HttpPost]
    public async Task<ActionResult<PartRequestDto>> CreatePartRequest(CreatePartRequestDto dto)
    {
        var partName = Clean(dto.PartName, 100);
        var searchText = Clean(dto.SearchText, 200);
        var partNumber = Clean(dto.PartNumber, 100);
        if (partName.Length == 0 && partNumber.Length == 0 && searchText.Length == 0)
        {
            ModelState.AddModelError("partName", "Tell us which part you need");
            return ValidationProblem(ModelState);
        }

        if (dto.Year != 0 && (dto.Year < 1900 || dto.Year > DateTime.UtcNow.Year + 1))
            ModelState.AddModelError("year", "Enter a valid year");
        var hasLat = dto.Latitude is not null;
        if (hasLat != (dto.Longitude is not null) ||
            (hasLat && !GeoMath.IsValidCoordinate(dto.Latitude!.Value, dto.Longitude!.Value)))
            ModelState.AddModelError("latitude", "Latitude and longitude must both be valid");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var req = new PartRequest
        {
            User = Username,
            SearchText = NullIfEmpty(searchText),
            Make = Clean(dto.Make, 50),
            Model = Clean(dto.Model, 50),
            Year = dto.Year,
            // Only a search text: use it as the part type so matching has words to look for.
            PartName = partName.Length > 0 ? partName : partNumber.Length > 0 ? "" : Clean(searchText, 100),
            PartNumber = NullIfEmpty(partNumber),
            PartNumberNormalized = PartNumbers.Normalize(partNumber),
            Vin = NullIfEmpty(Clean(dto.Vin, 20)),
            Specs = NullIfEmpty(Clean(dto.Specs, 500)),
            MaxBudget = Math.Max(dto.MaxBudget, 0),
            LocationName = NullIfEmpty(Clean(dto.LocationName, 120)),
            CountryCode = NullIfEmpty(Clean(dto.CountryCode, 2).ToUpperInvariant()),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            RadiusKm = dto.RadiusKm is > 0 ? Math.Min(dto.RadiusKm.Value, 500) : null,
            PreferredPort = Clean(dto.PreferredPort, 100),
            Status = CustomOrderStatus.Open,
        };

        outbox.DbContext.PartRequests.Add(req);
        // Every seller hears about it (live in the app and as a push notification).
        await outbox.PublishAsync(new PartRequestCreated
        {
            RequestId = req.Id,
            Buyer = req.User,
            Title = RequestTitle(req),
            LocationName = CityOf(req.LocationName),
            CountryCode = req.CountryCode,
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            RadiusKm = req.RadiusKm,
            CreatedAt = req.CreatedAt,
        });
        await outbox.SaveChangesAndFlushMessagesAsync();

        return CreatedAtAction(nameof(GetPartRequest), new { id = req.Id }, MapToDto(req));
    }

    /// <summary>
    /// Sellers: open requests from buyers near them (newest first; not their own) – within the buyer's radius, or in
    /// the buyer's country when the buyer gave no location; never another country (PartRequestAudience, using the
    /// seller's location from the token). Shows what is wanted and the buyer's city – never the buyer's name or location.
    /// </summary>
    [SellerOnly]
    [HttpGet("open")]
    public async Task<ActionResult<List<OpenPartRequestDto>>> Open([FromQuery] string? country, [FromQuery] int limit = 50)
    {
        var query = context.PartRequests.AsNoTracking().Where(x => x.Status == CustomOrderStatus.Open && x.User != Username);
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(x => x.CountryCode == country.ToUpper());
        var (sellerCountry, sellerLat, sellerLon) = PartRequestAudience.FromClaims(User);
        var requests = (await query.OrderByDescending(x => x.CreatedAt).ToListAsync())
            .Where(r => PartRequestAudience.Reaches(r.CountryCode, r.Latitude, r.Longitude, r.RadiusKm, sellerCountry, sellerLat, sellerLon))
            .Take(Math.Clamp(limit, 1, 200));
        return requests.Select(r => new OpenPartRequestDto
        {
            Id = r.Id,
            Title = RequestTitle(r),
            PartName = NullIfEmpty(r.PartName),
            PartNumber = r.PartNumber,
            Make = NullIfEmpty(r.Make),
            Model = NullIfEmpty(r.Model),
            Year = r.Year == 0 ? null : r.Year,
            Specs = r.Specs,
            MaxBudget = r.MaxBudget > 0 ? r.MaxBudget : null,
            LocationName = CityOf(r.LocationName),
            CountryCode = r.CountryCode,
            CreatedAt = r.CreatedAt,
        }).ToList();
    }

    /// <summary>"Front bumper – 2016 Toyota Hilux", "Part no. 52119-0K935", or the words the buyer searched for.</summary>
    public static string RequestTitle(PartRequest r)
    {
        var part = !string.IsNullOrWhiteSpace(r.PartName) ? r.PartName.Trim()
            : !string.IsNullOrWhiteSpace(r.PartNumber) ? $"Part no. {r.PartNumber}"
            : r.SearchText ?? "A part";
        var vehicle = string.Join(" ", new[] { r.Year > 0 ? r.Year.ToString() : null, r.Make, r.Model }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return vehicle.Length > 0 ? $"{char.ToUpperInvariant(part[0])}{part[1..]} – {vehicle}" : $"{char.ToUpperInvariant(part[0])}{part[1..]}";
    }

    /// <summary>"Kukatpally, Hyderabad" → "Hyderabad": only the city is shared with sellers.</summary>
    private static string? CityOf(string? locationName) =>
        string.IsNullOrWhiteSpace(locationName) ? null : locationName.Split(',').Last().Trim();

    /// <summary>The buyer no longer needs the part: no more notifications for this order.</summary>
    [HttpPost("{id}/close")]
    public async Task<ActionResult<PartRequestDto>> Close(string id)
    {
        var req = await context.PartRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (req == null || req.User != Username) return NotFound();

        req.Status = CustomOrderStatus.Closed;
        req.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return MapToDto(req);
    }

    /// <summary>Container feasibility (hidden in the app while FEATURES.containers is off).</summary>
    [AllowAnonymous]
    [HttpGet("check-feasibility")]
    public ActionResult<FeasibilityCheckResultDto> CheckFeasibility(string make, string model, int year, string partName)
    {
        var now = DateTime.UtcNow;
        return Ok(new FeasibilityCheckResultDto
        {
            IsFeasible = true,
            TargetContainerCode = "CN-2026-09",
            FeasibilityStatus = "Feasible & Slots Available",
            CutoffDate = now.AddDays(12),
            DaysRemaining = 12,
            PickupPort = "Lagos Apapa Port",
            Note = $"Part '{partName}' for {year} {make} {model} can be packed into container CN-2026-09 leaving Bremerhaven before cutoff date."
        });
    }

    private static string Clean(string? value, int maxLength)
    {
        var v = value?.Trim() ?? "";
        return v.Length > maxLength ? v[..maxLength] : v;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static PartRequestDto MapToDto(PartRequest r) => new()
    {
        Id = r.Id,
        User = r.User,
        SearchText = r.SearchText,
        Make = r.Make,
        Model = r.Model,
        Year = r.Year,
        PartName = r.PartName,
        PartNumber = r.PartNumber,
        Vin = r.Vin,
        Specs = r.Specs,
        MaxBudget = r.MaxBudget,
        LocationName = r.LocationName,
        CountryCode = r.CountryCode,
        RadiusKm = r.RadiusKm,
        PreferredPort = r.PreferredPort,
        Status = r.Status,
        MatchedAuctionId = r.MatchedAuctionId,
        MatchedAt = r.MatchedAt,
        FeasibilityNote = r.FeasibilityNote,
        EstimatedDeliveryDate = r.EstimatedDeliveryDate,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}
