using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;
using Contracts;

namespace AuctionService.RequestHelpers;

/// <summary>
/// Maps auctions to DTOs and events. Expects Item, Item.Images and Item.Container to be loaded
/// (see <see cref="AuctionQueryExtensions.WithDetails"/>).
/// </summary>
public class AuctionMapper(ImageUrlBuilder urls)
{
    public AuctionDto ToDto(Auction auction, string? currentUser = null)
    {
        var item = auction.Item;
        var images = item.OrderedImages.ToList();

        return new AuctionDto
        {
            Id = auction.Id,
            StartingPrice = auction.StartingPrice,
            BidIncrement = auction.BidIncrement,
            ReservePrice = currentUser != null && currentUser == auction.Seller ? auction.ReservePrice : null,
            ReserveMet = auction.IsReserveMet(),
            Seller = auction.Seller,
            Winner = auction.Winner,
            SoldAmount = auction.SoldAmount,
            CurrentHighBid = auction.CurrentHighBid,
            CreatedAt = auction.CreatedAt,
            UpdatedAt = auction.UpdatedAt,
            AuctionEnd = auction.AuctionEnd,
            Status = auction.Status.ToString(),
            EndReason = auction.EndReason?.ToString(),
            SellerDecisionDeadline = auction.SellerDecisionDeadline,
            ItemType = item.ItemType.ToString(),
            Make = item.Make,
            Model = item.Model,
            Year = item.Year,
            YearTo = item.YearTo,
            Color = item.Color,
            Mileage = item.Mileage,
            Description = item.Description,
            PartName = item.PartName,
            PartNumber = item.PartNumber,
            PartCategory = item.PartCategory?.ToString(),
            Condition = item.Condition?.ToString(),
            Quantity = item.Quantity,
            ContainerId = item.ContainerId,
            Container = item.Container is null ? null : ToContainerInfo(item.Container),
            LocationName = item.LocationName,
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            ImageUrl = images.Count > 0 ? urls.Build(images[0].Url) : string.Empty,
            Images = images.Select(x => urls.Build(x.Url)).ToList(),
            ImageItems = images.Select(x => new ItemImageDto
            {
                Id = x.Id,
                Url = urls.Build(x.Url),
                SortOrder = x.SortOrder,
                IsPrimary = x.IsPrimary
            }).ToList()
        };
    }

    public AuctionCreated ToCreated(Auction auction) => Fill(new AuctionCreated
    {
        Id = auction.Id, Seller = auction.Seller, Status = "", ItemType = "", Make = "", Model = "", Description = ""
    }, auction);

    public AuctionUpdated ToUpdated(Auction auction) => Fill(new AuctionUpdated
    {
        Id = auction.Id, Seller = auction.Seller, Status = "", ItemType = "", Make = "", Model = "", Description = ""
    }, auction);

    private T Fill<T>(T target, Auction auction) where T : AuctionSnapshot
    {
        var dto = ToDto(auction);

        target.ReservePrice = auction.ReservePrice;
        target.StartingPrice = dto.StartingPrice;
        target.BidIncrement = dto.BidIncrement;
        target.ReserveMet = dto.ReserveMet;
        target.Winner = dto.Winner;
        target.SoldAmount = dto.SoldAmount;
        target.CurrentHighBid = dto.CurrentHighBid;
        target.CreatedAt = dto.CreatedAt;
        target.UpdatedAt = dto.UpdatedAt;
        target.AuctionEnd = dto.AuctionEnd;
        target.Status = dto.Status;
        target.EndReason = dto.EndReason;
        target.SellerDecisionDeadline = dto.SellerDecisionDeadline;
        target.ItemType = dto.ItemType;
        target.Make = dto.Make;
        target.Model = dto.Model;
        target.Year = dto.Year;
        target.YearTo = dto.YearTo;
        target.Color = dto.Color;
        target.Mileage = dto.Mileage;
        target.Description = dto.Description;
        target.PartName = dto.PartName;
        target.PartNumber = dto.PartNumber;
        target.PartCategory = dto.PartCategory;
        target.Condition = dto.Condition;
        target.Quantity = dto.Quantity;
        target.Container = dto.Container;
        target.LocationName = dto.LocationName;
        target.Latitude = dto.Latitude;
        target.Longitude = dto.Longitude;
        target.ImageUrl = dto.ImageUrl;
        target.Images = dto.Images;

        return target;
    }

    public static ContainerInfo ToContainerInfo(Container container) => new()
    {
        Id = container.Id,
        ContainerNumber = container.ContainerNumber,
        OriginPort = container.OriginPort,
        OriginCountry = container.OriginCountry,
        DestinationPort = container.DestinationPort,
        DestinationCountry = container.DestinationCountry,
        DepartureDate = container.DepartureDate,
        EstimatedArrivalDate = container.EstimatedArrivalDate,
        ShippingStatus = container.ShippingStatus.ToString()
    };

    public static Auction ToEntity(CreateAuctionDto dto, string seller)
    {
        var auction = new Auction
        {
            Seller = seller,
            StartingPrice = dto.StartingPrice,
            ReservePrice = dto.ReservePrice is null or 0 ? dto.StartingPrice : dto.ReservePrice.Value,
            BidIncrement = dto.BidIncrement,
            AuctionEnd = dto.AuctionEnd,
            Status = Status.Live,
            Item = new Item
            {
                ItemType = dto.ItemType,
                Make = (dto.Make ?? "").Trim(),
                Model = (dto.Model ?? "").Trim(),
                Year = dto.Year,
                YearTo = dto.YearTo is null or 0 ? null : dto.YearTo,
                Color = Clean(dto.Color),
                Mileage = dto.Mileage,
                Description = (dto.Description ?? "").Trim(),
                PartName = Clean(dto.PartName),
                PartNumber = Clean(dto.PartNumber),
                PartCategory = dto.PartCategory,
                Condition = dto.Condition,
                Quantity = dto.ItemType == ItemType.Part ? dto.Quantity ?? 1 : null,
                ContainerId = Clean(dto.ContainerId),
                LocationName = Clean(dto.LocationName),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude
            }
        };

        if (auction.Item.ItemType == ItemType.Vehicle) auction.Item.ClearPartFields();

        return auction;
    }

    /// <summary>A new Live auction with the same item and container as <paramref name="source"/> (images not included).</summary>
    public static Auction CopyForRelist(Auction source, RelistAuctionDto dto)
    {
        var id = Guid.NewGuid().ToString();
        var item = source.Item;

        return new Auction
        {
            Id = id,
            Seller = source.Seller,
            StartingPrice = dto.StartingPrice,
            ReservePrice = dto.ReservePrice is null or 0 ? dto.StartingPrice : dto.ReservePrice.Value,
            BidIncrement = dto.BidIncrement,
            AuctionEnd = dto.AuctionEnd,
            Status = Status.Live,
            Item = new Item
            {
                Id = id,
                ItemType = item.ItemType,
                Make = item.Make,
                Model = item.Model,
                Year = item.Year,
                YearTo = item.YearTo,
                Color = item.Color,
                Mileage = item.Mileage,
                Description = item.Description,
                PartName = item.PartName,
                PartNumber = item.PartNumber,
                PartCategory = item.PartCategory,
                Condition = item.Condition,
                Quantity = item.Quantity,
                ContainerId = item.ContainerId,
                LocationName = item.LocationName,
                Latitude = item.Latitude,
                Longitude = item.Longitude
            }
        };
    }

    /// <summary>Applies a partial update (null = unchanged, "" / 0 = clear optional field).</summary>
    public static void ApplyUpdate(UpdateAuctionDto dto, Auction auction)
    {
        var item = auction.Item;

        if (dto.ItemType is not null) item.ItemType = dto.ItemType.Value;
        if (dto.Make is not null) item.Make = dto.Make.Trim();
        if (dto.Model is not null) item.Model = dto.Model.Trim();
        if (dto.Description is not null) item.Description = dto.Description.Trim();
        if (dto.Year is not null) item.Year = dto.Year.Value;
        if (dto.YearTo is not null) item.YearTo = dto.YearTo == 0 ? null : dto.YearTo;
        if (dto.Color is not null) item.Color = Clean(dto.Color);
        if (dto.Mileage is not null) item.Mileage = dto.Mileage == 0 && item.ItemType == ItemType.Part ? null : dto.Mileage;
        if (dto.PartName is not null) item.PartName = Clean(dto.PartName);
        if (dto.PartNumber is not null) item.PartNumber = Clean(dto.PartNumber);
        if (dto.PartCategory is not null) item.PartCategory = dto.PartCategory;
        if (dto.Condition is not null) item.Condition = dto.Condition;
        if (dto.Quantity is not null) item.Quantity = dto.Quantity;
        // Only the FK changes here; the controller reloads the Container navigation afterwards.
        if (dto.ContainerId is not null) item.ContainerId = Clean(dto.ContainerId);
        if (dto.Latitude is not null || dto.Longitude is not null)
        {
            item.Latitude = dto.Latitude;
            item.Longitude = dto.Longitude;
        }
        if (dto.LocationName is not null) item.LocationName = Clean(dto.LocationName);

        if (dto.StartingPrice is not null) auction.StartingPrice = dto.StartingPrice.Value;
        if (dto.BidIncrement is not null) auction.BidIncrement = dto.BidIncrement.Value;
        if (dto.ReservePrice is not null)
            auction.ReservePrice = dto.ReservePrice == 0 ? auction.StartingPrice : dto.ReservePrice.Value;
        if (dto.AuctionEnd is not null) auction.AuctionEnd = dto.AuctionEnd.Value;

        if (item.ItemType == ItemType.Vehicle) item.ClearPartFields();
        else item.Quantity ??= 1;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}