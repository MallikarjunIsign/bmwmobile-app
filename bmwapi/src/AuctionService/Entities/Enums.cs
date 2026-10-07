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
    Refurbished
}

public enum ShippingStatus
{
    Loading,
    InTransit,
    Arrived,
    Delivered
}

public enum EndReason
{
    TimeEnded,
    SellerAccepted,
    SellerAcceptedAfterEnd,
    GracePeriodExpired
}