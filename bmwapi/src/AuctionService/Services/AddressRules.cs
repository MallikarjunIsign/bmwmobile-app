using System.Text.RegularExpressions;
using AuctionService.DTOs;
using AuctionService.Entities;
using Contracts;

namespace AuctionService.Services;

/// <summary>Checks and cleans shipping addresses (field → messages, like the listing rules).</summary>
public static partial class AddressRules
{
    /// <summary>Postcode format per country; Nigeria's postcode is optional (rarely used for deliveries).</summary>
    private static readonly Dictionary<string, (Regex Pattern, string Name, bool Required)> Postcodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IN"] = (IndiaPin(), "PIN code (6 digits)", true),
        ["US"] = (UsZip(), "ZIP code (5 digits)", true),
        ["IE"] = (Eircode(), "Eircode (e.g. D02 X285)", true),
        ["NG"] = (NigeriaPostcode(), "postcode (6 digits)", false),
    };

    public static Dictionary<string, string[]> Validate(AddressDto? dto)
    {
        var errors = new Dictionary<string, string[]>();
        if (dto is null) return new() { ["address"] = ["Add the shipping address"] };

        if (Clean(dto.Name) is not { Length: >= 2 }) errors["name"] = ["Enter the name of the person receiving it"];
        if (Clean(dto.Phone) is not { } phone || phone.Count(char.IsDigit) is < 6 or > 15) errors["phone"] = ["Enter a phone number for the courier"];
        if (Clean(dto.Line1) is null) errors["line1"] = ["Enter the street address"];
        if (Clean(dto.City) is null) errors["city"] = ["Enter the city"];

        var country = dto.CountryCode?.Trim().ToUpperInvariant();
        if (!RegionRules.IsSupported(country)) errors["countryCode"] = ["Choose the country"];
        else if (Postcodes.TryGetValue(country!, out var rule))
        {
            var postcode = Clean(dto.Postcode);
            if (postcode is null ? rule.Required : !rule.Pattern.IsMatch(postcode))
                errors["postcode"] = [$"Enter a valid {rule.Name}"];
        }
        return errors;
    }

    /// <summary>The cleaned address (call after <see cref="Validate"/> found no errors).</summary>
    public static AddressInfo ToInfo(AddressDto dto) => new()
    {
        Name = Clean(dto.Name, 100)!,
        Phone = Clean(dto.Phone, 30)!,
        Line1 = Clean(dto.Line1, 150)!,
        Line2 = Clean(dto.Line2, 150),
        City = Clean(dto.City, 80)!,
        Postcode = Clean(dto.Postcode, 20)?.ToUpperInvariant() ?? "",
        CountryCode = dto.CountryCode.Trim().ToUpperInvariant()
    };

    public static AddressDto ToDto(AddressInfo a) => new()
    {
        Name = a.Name, Phone = a.Phone, Line1 = a.Line1, Line2 = a.Line2, City = a.City, Postcode = a.Postcode, CountryCode = a.CountryCode
    };

    private static string? Clean(string? value, int max = 200) => ListingRules.Clean(value, max);

    [GeneratedRegex(@"^[1-9]\d{5}$")]
    private static partial Regex IndiaPin();

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex UsZip();

    [GeneratedRegex(@"^[A-Za-z]\d[\dWw] ?[A-Za-z\d]{4}$")]
    private static partial Regex Eircode();

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex NigeriaPostcode();
}
