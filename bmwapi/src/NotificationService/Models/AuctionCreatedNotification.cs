using Contracts;

namespace NotificationService.Models;

/// <summary>
/// What browsers receive for a new auction. Built from AuctionCreated but without the internal ReservePrice,
/// plus a ready-to-show title that works for vehicles and parts.
/// </summary>
public record AuctionCreatedNotification(
    string Id,
    string Title,
    string ItemType,
    string Seller,
    string Make,
    string Model,
    int Year,
    string? PartName,
    int StartingPrice,
    DateTime AuctionEnd,
    string ImageUrl)
{
    public static AuctionCreatedNotification From(AuctionCreated message) => new(
        message.Id,
        TitleFor(message),
        message.ItemType,
        message.Seller,
        message.Make,
        message.Model,
        message.Year,
        message.PartName,
        message.StartingPrice,
        message.AuctionEnd,
        message.ImageUrl);

    /// <summary>"Front bumper (fits Toyota Hilux)" for a part, "2020 Ford GT" for a vehicle.</summary>
    public static string TitleFor(AuctionSnapshot auction) =>
        auction.ItemType == "Part" && !string.IsNullOrWhiteSpace(auction.PartName)
            ? $"{auction.PartName} (fits {auction.Make} {auction.Model})"
            : $"{auction.Year} {auction.Make} {auction.Model}";
}