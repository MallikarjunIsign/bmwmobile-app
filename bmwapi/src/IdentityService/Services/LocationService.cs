using Contracts;
using IdentityService.Contracts;
using IdentityService.Data;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>
/// The user's saved locations (like Home / Work in food-delivery apps).
/// Rules:
///  - exactly one primary location (the first one saved becomes primary);
///  - auto-update ON: the primary is the "Current location" row, moved to the phone's GPS when it moves &gt; 1 km;
///  - choosing a primary by hand turns auto-update OFF so GPS never overwrites it;
///  - saved places never change by themselves, and the primary can't be deleted.
/// </summary>
public class LocationService(ApplicationDbContext db, IOptions<LocationOptions> options)
{
    private readonly LocationOptions _opt = options.Value;

    public async Task<MyLocationsResponse> GetAsync(string userId, CancellationToken ct)
    {
        var auto = await db.Users.Where(u => u.Id == userId).Select(u => u.AutoUpdatePrimaryLocation).FirstAsync(ct);
        var rows = await db.UserLocations.AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.IsPrimary)
            .ThenBy(l => l.CreatedAt)
            .ToListAsync(ct);
        return new MyLocationsResponse(auto, rows.Select(ToDto).ToList());
    }

    public Task<bool> HasAnyAsync(string userId, CancellationToken ct) =>
        db.UserLocations.AnyAsync(l => l.UserId == userId, ct);

    public async Task<MyLocationsResponse> AddAsync(string userId, SaveLocationRequest req, CancellationToken ct)
    {
        var (lat, lon) = ValidCoordinates(req.Latitude, req.Longitude);
        var existing = await db.UserLocations.Where(l => l.UserId == userId).ToListAsync(ct);
        if (existing.Count >= _opt.MaxLocationsPerUser)
            throw AppException.BadRequest($"You can save up to {_opt.MaxLocationsPerUser} locations. Delete one first.", "TOO_MANY_LOCATIONS");

        var now = DateTime.UtcNow;
        var country = CleanCountry(req.CountryCode) ?? CityCatalog.Describe(lat, lon).CountryCode;
        var row = new UserLocation
        {
            UserId = userId,
            Label = CleanLabel(req.Label),
            PlaceName = CleanPlace(req.PlaceName),
            Latitude = lat,
            Longitude = lon,
            RadiusKm = RadiusKm(req.RadiusValue, req.RadiusKm, country),
            CountryCode = country,
            CreatedAt = now,
            UpdatedAt = now,
        };
        SetAddress(row, req);
        db.UserLocations.Add(row);
        await db.SaveChangesAsync(ct);

        // The first location is always the primary one; otherwise only when asked.
        if (req.MakePrimary || existing.Count == 0)
            await MakePrimaryAsync(userId, row, existing, ct);

        return await GetAsync(userId, ct);
    }

    public async Task<MyLocationsResponse> UpdateAsync(string userId, Guid id, SaveLocationRequest req, CancellationToken ct)
    {
        var row = await FindAsync(userId, id, ct);
        var country = row.CountryCode;

        // The GPS row's place comes from GPS; only its radius can be changed by hand.
        if (!row.IsCurrentLocation)
        {
            var (lat, lon) = ValidCoordinates(req.Latitude, req.Longitude);
            row.Label = CleanLabel(req.Label);
            row.PlaceName = CleanPlace(req.PlaceName);
            row.Latitude = lat;
            row.Longitude = lon;
            country = CleanCountry(req.CountryCode) ?? CityCatalog.Describe(lat, lon).CountryCode;
            SetAddress(row, req);
        }
        // Not sent: the same number in the (possibly new) country's unit.
        var keep = Math.Clamp(RegionRules.RadiusValue(row.RadiusKm, row.CountryCode), RegionRules.MinRadiusValue, RegionRules.MaxRadiusValue);
        row.RadiusKm = req.RadiusValue is null && req.RadiusKm is > 0
            ? RadiusKm(null, req.RadiusKm, country)
            : RadiusKm(req.RadiusValue ?? keep, null, country);
        row.CountryCode = country;

        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<MyLocationsResponse> DeleteAsync(string userId, Guid id, CancellationToken ct)
    {
        var row = await FindAsync(userId, id, ct);
        if (row.IsPrimary)
            throw AppException.Conflict("Your primary location can't be deleted. Make another location primary first.", "PRIMARY_CANNOT_BE_DELETED");

        db.UserLocations.Remove(row);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    /// <summary>Only the radius (in the location's unit, 5–1,000) – Home's location line, or the GPS row.</summary>
    public async Task<MyLocationsResponse> SetRadiusAsync(string userId, Guid id, int? radiusValue, CancellationToken ct)
    {
        var row = await FindAsync(userId, id, ct);
        row.RadiusKm = RadiusKm(radiusValue ?? throw InvalidRadius(row.CountryCode), null, row.CountryCode);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    /// <summary>"Set as primary" on a saved location. A manual choice turns auto-update off.</summary>
    public async Task<MyLocationsResponse> SetPrimaryAsync(string userId, Guid id, CancellationToken ct)
    {
        var row = await FindAsync(userId, id, ct);
        var all = await db.UserLocations.Where(l => l.UserId == userId).ToListAsync(ct);
        await MakePrimaryAsync(userId, row, all, ct);
        return await GetAsync(userId, ct);
    }

    /// <summary>
    /// The phone's GPS position. Explicit ("Use my current location"): the primary becomes the
    /// current location and auto-update turns on. Automatic (app opened): only when auto-update
    /// is on and the user moved more than 1 km.
    /// </summary>
    public async Task<MyLocationsResponse> UpdateCurrentAsync(string userId, CurrentLocationRequest req, CancellationToken ct)
    {
        var (lat, lon) = ValidCoordinates(req.Latitude, req.Longitude);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var all = await db.UserLocations.Where(l => l.UserId == userId).ToListAsync(ct);
        var current = all.FirstOrDefault(l => l.IsCurrentLocation);

        if (!req.Explicit)
        {
            if (!user.AutoUpdatePrimaryLocation) return await GetAsync(userId, ct);
            if (current is not null && current.IsPrimary &&
                GeoMath.HaversineKm(current.Latitude, current.Longitude, lat, lon) < _opt.MoveThresholdKm)
                return await GetAsync(userId, ct);
        }

        var place = CityCatalog.Describe(lat, lon);
        var now = DateTime.UtcNow;
        if (current is null)
        {
            // Without a choice: the default radius (200 km / 200 mi) for where the phone is.
            current = new UserLocation
            {
                UserId = userId,
                Label = CityCatalog.CurrentLocationName,
                PlaceName = place.Name,
                RadiusKm = RadiusKm(req.RadiusValue, req.RadiusKm, place.CountryCode),
                IsCurrentLocation = true,
                CreatedAt = now,
            };
            db.UserLocations.Add(current);
            all.Add(current);
        }
        else if (req.RadiusValue is not null || req.RadiusKm is not null)
        {
            current.RadiusKm = RadiusKm(req.RadiusValue, req.RadiusKm, place.CountryCode);
        }

        current.PlaceName = place.Name;
        current.Latitude = lat;
        current.Longitude = lon;
        current.CountryCode = place.CountryCode;
        current.UpdatedAt = now;
        user.AutoUpdatePrimaryLocation = true;
        await db.SaveChangesAsync(ct);

        if (!current.IsPrimary) await SwitchPrimaryAsync(current, all, ct);
        return await GetAsync(userId, ct);
    }

    /// <summary>Turning auto-update off keeps the primary as it is; turning it on lets the app send GPS again.</summary>
    public async Task<MyLocationsResponse> SetAutoUpdateAsync(string userId, bool autoUpdate, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.AutoUpdatePrimaryLocation = autoUpdate;
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// The radius to store (km, rounded): <paramref name="value"/> in the location's unit must be 5–1,000 (else 400
    /// INVALID_RADIUS); an older app's km value is pulled into that range; nothing → the default.
    /// </summary>
    private static int RadiusKm(int? value, double? legacyKm, string? country)
    {
        if (value is { } v)
        {
            if (!RegionRules.IsValidRadius(v)) throw InvalidRadius(country);
            return (int)Math.Round(RegionRules.Radius(v, country).Km, MidpointRounding.AwayFromZero);
        }
        if (legacyKm is > 0)
        {
            var clamped = Math.Clamp(RegionRules.RadiusValue(legacyKm.Value, country), RegionRules.MinRadiusValue, RegionRules.MaxRadiusValue);
            return (int)Math.Round(RegionRules.Radius(clamped, country).Km, MidpointRounding.AwayFromZero);
        }
        return (int)Math.Round(RegionRules.DefaultSearchRadius(country).Km, MidpointRounding.AwayFromZero);
    }

    private static AppException InvalidRadius(string? country) => AppException.BadRequest(
        $"Choose a radius between {RegionRules.MinRadiusValue} and {RegionRules.MaxRadiusValue:N0} {(RegionRules.DistanceUnit(country) == "mi" ? "miles" : "km")}.",
        "INVALID_RADIUS");

    /// <summary>Manual primary choice: the GPS row is no longer needed and auto-update turns off.</summary>
    private async Task MakePrimaryAsync(string userId, UserLocation row, List<UserLocation> all, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.AutoUpdatePrimaryLocation = false;

        var gpsRow = all.FirstOrDefault(l => l.IsCurrentLocation && l.Id != row.Id);
        if (gpsRow is not null)
        {
            db.UserLocations.Remove(gpsRow);
            all.Remove(gpsRow);
        }

        await SwitchPrimaryAsync(row, all, ct);
    }

    /// <summary>Clears the old primary first, then sets the new one (one primary per user is enforced by an index).</summary>
    private async Task SwitchPrimaryAsync(UserLocation row, List<UserLocation> all, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var other in all.Where(l => l.IsPrimary && l.Id != row.Id)) other.IsPrimary = false;
        await db.SaveChangesAsync(ct);
        row.IsPrimary = true;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<UserLocation> FindAsync(string userId, Guid id, CancellationToken ct) =>
        await db.UserLocations.FirstOrDefaultAsync(l => l.UserId == userId && l.Id == id, ct)
        ?? throw AppException.NotFound("This location no longer exists.", "LOCATION_NOT_FOUND");

    private static (double Lat, double Lon) ValidCoordinates(double? lat, double? lon)
    {
        if (lat is null || lon is null || !GeoMath.IsValidCoordinate(lat.Value, lon.Value))
            throw AppException.BadRequest("We couldn't read this location. Please pick a city instead.", "INVALID_LOCATION");
        return (lat.Value, lon.Value);
    }

    private static string CleanLabel(string? label)
    {
        var l = label?.Trim();
        return string.IsNullOrEmpty(l) ? "Other" : l.Length > 40 ? l[..40] : l;
    }

    private static string CleanPlace(string place)
    {
        var p = place.Trim();
        if (p.Length == 0) throw AppException.BadRequest("Choose a place first.", "VALIDATION_FAILED");
        return p.Length > 120 ? p[..120] : p;
    }

    private static void SetAddress(UserLocation row, SaveLocationRequest req)
    {
        row.AddressLine = Optional(req.AddressLine, 200);
        row.Area = Optional(req.Area, 100);
        row.Landmark = Optional(req.Landmark, 100);
        row.PostalCode = Optional(req.PostalCode, 12);
    }

    private static string? Optional(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }

    private static string? CleanCountry(string? code) =>
        CityCatalog.TryGetCountry(code, out var country) && !string.IsNullOrWhiteSpace(code) ? country.Code : null;

    /// <summary>The radius as the user chose it (200 mi) and its exact km (321.87); stored as whole km.</summary>
    public static UserLocationDto ToDto(UserLocation l)
    {
        var radius = RegionRules.Radius(RegionRules.RadiusValue(l.RadiusKm, l.CountryCode), l.CountryCode);
        return new(l.Id, l.Label, l.PlaceName, l.Latitude, l.Longitude, radius.Km, l.CountryCode, l.IsPrimary, l.IsCurrentLocation,
            l.AddressLine, l.Area, l.Landmark, l.PostalCode, radius.Value, radius.Unit);
    }
}
