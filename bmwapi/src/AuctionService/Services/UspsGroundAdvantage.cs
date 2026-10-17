using System.Globalization;
using System.Text.RegularExpressions;
using AuctionService.Entities;
using Contracts;

namespace AuctionService.Services;

/// <summary>A parcel's weight (lb) and box size (inches).</summary>
public record Parcel(decimal WeightLb, decimal LengthIn, decimal WidthIn, decimal HeightIn);

/// <summary>A USPS Ground Advantage price and how it was worked out.</summary>
public record UspsPrice(decimal Price, int Zone, int BillableWeightOz, bool Oversized, decimal Fees);

/// <summary>
/// USPS Ground Advantage – Retail, worked out offline from the official price list (USPS Notice 123, the CSV in
/// Data/Seed) and the USPS rules printed with it:
/// <list type="bullet">
/// <item>Zone from the distance between the seller's and the buyer's area (USPS zones 1–8; zone 9 = territories).</item>
/// <item>Weight rounded up to the next price step (4, 8, 12, 15.999 oz, then whole pounds up to 70 lb).</item>
/// <item>Over one cubic foot (1,728 in³): charged on the greater of the actual and the dimensional weight
///   (L × W × H ÷ 166, rounded up to a whole pound; dimensions rounded to the nearest inch).</item>
/// <item>Length + girth over 108 in (up to 130 in): the oversized price for the zone, regardless of weight.</item>
/// <item>Non-standard fees: longest side over 22 in +$4.50, over 30 in +$10.00; over 2 ft³ (3,456 in³) +$21.00.</item>
/// <item>Over 70 lb or over 130 in length + girth: not USPS (freight instead).</item>
/// </list>
/// When the USPS API keys arrive, the live Domestic Prices API can replace this (exact ZIP-to-ZIP zones).
/// </summary>
public class UspsGroundAdvantage
{
    public const string ServiceName = "USPS Ground Advantage";
    public const decimal MaxWeightLb = 70;
    public const decimal MaxLengthPlusGirthIn = 130;
    public const decimal OversizedOverIn = 108;
    public const int DimDivisor = 166;
    public const int CubicFootIn3 = 1728;

    /// <summary>The price list edition (from the CSV header), e.g. "2026-10-04".</summary>
    public string Edition { get; }

    // weight step (oz) → price per zone 1–9; and the oversized prices per zone.
    private readonly SortedDictionary<int, decimal[]> _prices = new();
    private readonly decimal[] _oversized = new decimal[9];

    public UspsGroundAdvantage(string csv)
    {
        Edition = Regex.Match(csv, @"effective (\d{4}-\d{2}-\d{2})").Groups[1].Value;
        foreach (var line in csv.Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("weight", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(',');
            var zones = parts.Skip(1).Select(p => decimal.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (zones.Length != 9) throw new FormatException($"Expected 9 zone prices: {line}");
            if (parts[0] == "oversized") zones.CopyTo(_oversized, 0);
            else _prices[int.Parse(parts[0], CultureInfo.InvariantCulture)] = zones;
        }
        if (_prices.Count == 0 || _oversized[0] == 0) throw new FormatException("The USPS price list is empty");
    }

    /// <summary>Loads Data/Seed/usps-ground-advantage-retail.csv (copied next to the service).</summary>
    public static UspsGroundAdvantage FromFile(string? path = null) =>
        new(File.ReadAllText(path ?? Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "usps-ground-advantage-retail.csv")));

    /// <summary>USPS zone from the distance in miles (USPS uses the distance between 3-digit ZIP areas).</summary>
    public static int Zone(double miles) => miles switch
    {
        <= 50 => 1,
        <= 150 => 2,
        <= 300 => 3,
        <= 600 => 4,
        <= 1000 => 5,
        <= 1400 => 6,
        <= 1800 => 7,
        _ => 8,
    };

    public static decimal Round(decimal inches) => Math.Round(inches, 0, MidpointRounding.AwayFromZero);

    /// <summary>Longest side + 2 × (the other two sides).</summary>
    public static decimal LengthPlusGirth(Parcel p)
    {
        var sides = new[] { Round(p.LengthIn), Round(p.WidthIn), Round(p.HeightIn) }.OrderByDescending(x => x).ToArray();
        return sides[0] + 2 * (sides[1] + sides[2]);
    }

    public static decimal Volume(Parcel p) => Round(p.LengthIn) * Round(p.WidthIn) * Round(p.HeightIn);

    /// <summary>Why USPS won't take it (null = it can go by USPS).</summary>
    public static string? TooBig(Parcel p) =>
        p.WeightLb > MaxWeightLb ? $"over {MaxWeightLb:0} lb"
        : LengthPlusGirth(p) > MaxLengthPlusGirthIn ? $"over {MaxLengthPlusGirthIn:0} in length + girth"
        : null;

    /// <summary>Billable weight in ounces: actual (rounded up), or the dimensional weight when it's more (over 1 ft³).</summary>
    public static int BillableWeightOz(Parcel p)
    {
        var actualOz = (int)Math.Ceiling(p.WeightLb * 16);
        var volume = Volume(p);
        if (volume <= CubicFootIn3) return Math.Max(actualOz, 1);
        var dimLb = (int)Math.Ceiling(volume / DimDivisor);
        return Math.Max(Math.Max(actualOz, dimLb * 16), 1);
    }

    /// <summary>The price for a parcel over a distance (miles); null when USPS won't take it.</summary>
    public UspsPrice? Price(Parcel parcel, double miles)
    {
        if (TooBig(parcel) is not null) return null;
        var zone = Zone(miles);
        var billableOz = BillableWeightOz(parcel);
        var oversized = LengthPlusGirth(parcel) > OversizedOverIn;

        decimal basePrice;
        if (oversized) basePrice = _oversized[zone - 1];
        else
        {
            var step = _prices.Keys.FirstOrDefault(k => k >= billableOz);
            if (step == 0) return null; // heavier than the list (can't happen below 70 lb)
            basePrice = _prices[step][zone - 1];
        }

        // Non-standard fees (USPS Notice 123, Ground Advantage – Retail).
        var longest = new[] { Round(parcel.LengthIn), Round(parcel.WidthIn), Round(parcel.HeightIn) }.Max();
        var fees = longest > 30 ? 10.00m : longest > 22 ? 4.50m : 0m;
        if (Volume(parcel) > 2 * CubicFootIn3) fees += 21.00m;

        return new UspsPrice(basePrice + fees, zone, billableOz, oversized, fees);
    }
}

/// <summary>
/// Default package (weight and box) for a part, which the seller can change: from the part type where it's known,
/// otherwise from the size class. Used for US shipping prices (USPS needs weight and dimensions).
/// </summary>
public static partial class ParcelDefaults
{
    private static readonly (Regex Pattern, Parcel Parcel)[] ByPartType =
    [
        (new Regex(@"bumper", RegexOptions.IgnoreCase), new Parcel(25, 72, 20, 14)),
        (new Regex(@"bonnet|hood|boot lid|tailgate|^door$", RegexOptions.IgnoreCase), new Parcel(45, 56, 44, 8)),
        (new Regex(@"fender|wing", RegexOptions.IgnoreCase), new Parcel(15, 40, 24, 8)),
        (new Regex(@"windscreen|windshield|door glass", RegexOptions.IgnoreCase), new Parcel(30, 62, 36, 6)),
        (new Regex(@"alloy wheel", RegexOptions.IgnoreCase), new Parcel(28, 22, 22, 10)),
        (new Regex(@"^tyre|^tire", RegexOptions.IgnoreCase), new Parcel(28, 30, 30, 10)),
        (new Regex(@"headlamp|tail ?lamp", RegexOptions.IgnoreCase), new Parcel(8, 24, 16, 14)),
        (new Regex(@"battery", RegexOptions.IgnoreCase), new Parcel(40, 14, 9, 10)),
        (new Regex(@"radiator|condenser", RegexOptions.IgnoreCase), new Parcel(18, 32, 26, 6)),
        (new Regex(@"alternator|starter|compressor", RegexOptions.IgnoreCase), new Parcel(15, 12, 10, 10)),
        (new Regex(@"bulb|fuse|relay|spark plug|key", RegexOptions.IgnoreCase), new Parcel(0.5m, 6, 4, 4)),
        (new Regex(@"brake pads|filter|sensor|ecu", RegexOptions.IgnoreCase), new Parcel(3, 10, 8, 4)),
        // A set of seat covers: soft, folded – under one cubic foot, so the actual weight is charged.
        (new Regex(@"seat\s*covers?", RegexOptions.IgnoreCase), new Parcel(4, 18, 14, 6)),
    ];

    private static readonly Dictionary<SizeClass, Parcel> BySize = new()
    {
        [SizeClass.Small] = new Parcel(2, 10, 8, 6),
        [SizeClass.Medium] = new Parcel(10, 20, 14, 12),
        [SizeClass.Large] = new Parcel(30, 40, 24, 16),
        [SizeClass.Freight] = new Parcel(150, 40, 30, 30),
        [SizeClass.Vehicle] = new Parcel(3000, 180, 72, 60),
    };

    public static Parcel For(string? partType, SizeClass size)
    {
        if (!string.IsNullOrWhiteSpace(partType))
            foreach (var (pattern, parcel) in ByPartType)
                if (pattern.IsMatch(partType)) return parcel;
        return BySize[size];
    }

    /// <summary>The listing's package: what the seller entered, with the defaults for anything missing.</summary>
    public static Parcel Of(Item item, SizeClass size)
    {
        var d = For(item.PartType?.Name, size);
        return new Parcel(item.WeightLb ?? d.WeightLb, item.LengthIn ?? d.LengthIn, item.WidthIn ?? d.WidthIn, item.HeightIn ?? d.HeightIn);
    }
}
