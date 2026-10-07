using System.Security.Claims;
using System.Text.RegularExpressions;
using AuctionService.DTOs;
using AuctionService.Entities;

namespace AuctionService.Validation;

public static partial class ContainerRules
{
    /// <summary>
    /// Authorization policy for creating containers. Today: any logged-in user. To make it admin-only later,
    /// change the policy registration in <see cref="AddContainerPolicy"/> (nothing else needs to change).
    /// </summary>
    public const string ManagePolicy = "CanManageContainers";

    public static void AddContainerPolicy(this Microsoft.AspNetCore.Authorization.AuthorizationOptions options) =>
        options.AddPolicy(ManagePolicy, policy => policy.RequireAuthenticatedUser());

    /// <summary>Only the user who created a container may change or delete it.</summary>
    public static bool CanEdit(Container container, ClaimsPrincipal user) =>
        user.Identity?.Name is { } name && name == container.CreatedBy;

    // ISO 6346 style: 3-letter owner code + equipment category letter + 7 digits, e.g. MSCU1234567.
    [GeneratedRegex("^[A-Z]{4}[0-9]{7}$")]
    private static partial Regex ContainerNumberPattern();

    public static string NormalizeNumber(string? value) => (value ?? "").Trim().Replace(" ", "").ToUpperInvariant();

    public static Dictionary<string, string[]> Validate(SaveContainerDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (!ContainerNumberPattern().IsMatch(NormalizeNumber(dto.ContainerNumber)))
            errors["containerNumber"] = ["Container number must be 4 letters followed by 7 digits, e.g. MSCU1234567"];
        if (string.IsNullOrWhiteSpace(dto.OriginPort)) errors["originPort"] = ["Origin port is required"];
        if (string.IsNullOrWhiteSpace(dto.OriginCountry)) errors["originCountry"] = ["Origin country is required"];
        if (string.IsNullOrWhiteSpace(dto.DestinationPort)) errors["destinationPort"] = ["Destination port is required"];
        if (string.IsNullOrWhiteSpace(dto.DestinationCountry)) errors["destinationCountry"] = ["Destination country is required"];
        if (dto.DepartureDate == default) errors["departureDate"] = ["Departure date is required"];
        if (dto.EstimatedArrivalDate == default) errors["estimatedArrivalDate"] = ["Estimated arrival date is required"];
        else if (dto.EstimatedArrivalDate < dto.DepartureDate)
            errors["estimatedArrivalDate"] = ["Estimated arrival date must be on or after the departure date"];

        return errors;
    }

    public static void Apply(SaveContainerDto dto, Container container)
    {
        container.ContainerNumber = NormalizeNumber(dto.ContainerNumber);
        container.OriginPort = dto.OriginPort.Trim();
        container.OriginCountry = dto.OriginCountry.Trim();
        container.DestinationPort = dto.DestinationPort.Trim();
        container.DestinationCountry = dto.DestinationCountry.Trim();
        container.DepartureDate = AsUtc(dto.DepartureDate);
        container.EstimatedArrivalDate = AsUtc(dto.EstimatedArrivalDate);
        container.ShippingStatus = dto.ShippingStatus;
    }

    // Dates without a zone (e.g. "2026-10-01" from a date picker) are taken as UTC, not server-local time.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}