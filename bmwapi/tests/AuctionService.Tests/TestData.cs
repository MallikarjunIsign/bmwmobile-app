using AuctionService.DTOs;
using AuctionService.Entities;

namespace AuctionService.Tests;

internal static class TestData
{
    public static Auction CreateAuction(Action<Auction>? configure = null)
    {
        var id = Guid.NewGuid().ToString();
        var auction = new Auction
        {
            Id = id,
            Seller = "seller",
            StartingPrice = 50,
            ReservePrice = 100,
            BidIncrement = 10,
            Status = Status.Live,
            AuctionEnd = DateTime.UtcNow.AddDays(7),
            Item = new Item
            {
                Id = id,
                Make = "Ford",
                Model = "Mustang",
                Year = 2021,
                Mileage = 1000,
                Color = "Red",
                Description = "Test car",
                Images = [new ItemImage { ItemId = id, Url = "https://image.url/car.jpg", FileName = "car.jpg", IsPrimary = true }]
            }
        };

        configure?.Invoke(auction);

        return auction;
    }

    public static Auction CreatePartAuction(Action<Auction>? configure = null)
    {
        return CreateAuction(auction =>
        {
            auction.Item.ItemType = ItemType.Part;
            auction.Item.PartName = "Front bumper";
            auction.Item.PartCategory = PartCategory.Body;
            auction.Item.Condition = ItemCondition.Used;
            auction.Item.Quantity = 1;
            auction.Item.Make = "Toyota";
            auction.Item.Model = "Hilux";
            auction.Item.Year = 2016;
            auction.Item.YearTo = 2020;
            auction.Item.Color = null;
            auction.Item.Mileage = null;
            configure?.Invoke(auction);
        });
    }

    public static CreateAuctionDto BuildCreateAuctionDto(Action<CreateAuctionDto>? configure = null)
    {
        var dto = new CreateAuctionDto
        {
            Make = "Ford",
            Model = "Mustang",
            Year = 2021,
            Mileage = 1000,
            Color = "Red",
            Description = "Test car",
            StartingPrice = 5000,
            ReservePrice = 10000,
            BidIncrement = 500,
            AuctionEnd = DateTime.UtcNow.AddDays(7),
        };

        configure?.Invoke(dto);

        return dto;
    }

    public static CreateAuctionDto BuildCreatePartDto(Action<CreateAuctionDto>? configure = null)
    {
        return BuildCreateAuctionDto(dto =>
        {
            dto.ItemType = ItemType.Part;
            dto.PartName = "Front bumper";
            dto.PartNumber = "52119-0K954";
            dto.PartCategory = PartCategory.Body;
            dto.Condition = ItemCondition.Used;
            dto.Quantity = 1;
            dto.Make = "Toyota";
            dto.Model = "Hilux";
            dto.Year = 2016;
            dto.YearTo = 2020;
            dto.Color = null;
            dto.Mileage = null;
            configure?.Invoke(dto);
        });
    }
}