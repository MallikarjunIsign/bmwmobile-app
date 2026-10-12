namespace AuctionService.Entities;

public enum ItemType
{
    Vehicle = 0,
    Part = 1
}

public enum PartCategory
{
    Body,
    Engine,
    Transmission,
    Electrical,
    Suspension,
    Brakes,
    Interior,
    WheelsAndTyres,
    Lighting,
    Other
}

public enum ItemCondition
{
    New,
    Used,
    Refurbished,
    /// <summary>For repair / not working.</summary>
    ForRepair
}

public enum ShippingStatus
{
    Loading,
    InTransit,
    Arrived,
    Delivered
}

/// <summary>How a listing is sold: bidding until a seller-chosen end, or at a fixed price until sold out.</summary>
public enum SaleType
{
    Auction = 0,
    FixedPrice = 1
}

public enum EndReason
{
    TimeEnded,
    SellerAccepted,
    SellerAcceptedAfterEnd,
    GracePeriodExpired
}