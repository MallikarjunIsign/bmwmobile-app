using AuctionService.Entities;

namespace AuctionService.Validation;

/// <summary>
/// Rules an auction must satisfy after a create or update. Returns errors keyed by field name
/// (camelCase, matching the JSON the website sends) for a ValidationProblem response.
/// </summary>
public static class AuctionValidator
{
    public static Dictionary<string, string[]> Validate(Auction auction, bool requireFutureEnd)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var item = auction.Item;
        var maxYear = DateTime.UtcNow.Year + 1;

        if (string.IsNullOrWhiteSpace(item.Make)) Add("make", "Make is required");
        if (string.IsNullOrWhiteSpace(item.Model)) Add("model", "Model is required");
        if (string.IsNullOrWhiteSpace(item.Description)) Add("description", "Description is required");
        if (item.Year < 1886 || item.Year > maxYear) Add("year", $"Year must be between 1886 and {maxYear}");

        if (item.ItemType == ItemType.Vehicle)
        {
            if (string.IsNullOrWhiteSpace(item.Color)) Add("color", "Color is required for a vehicle");
            if (item.Mileage is null) Add("mileage", "Mileage is required for a vehicle");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(item.PartName)) Add("partName", "Part name is required for a part");
            if (item.PartCategory is null) Add("partCategory", "Part category is required for a part");
            if (item.Condition is null) Add("condition", "Condition is required for a part");
            if (item.Quantity is null or < 1) Add("quantity", "Quantity must be at least 1");
            if (item.YearTo is not null && (item.YearTo < item.Year || item.YearTo > maxYear))
                Add("yearTo", $"Year to must be between {item.Year} and {maxYear}");
        }

        if (item.Mileage is < 0) Add("mileage", "Mileage cannot be negative");

        if (auction.StartingPrice <= 0) Add("startingPrice", "Starting price must be greater than 0");
        if (auction.BidIncrement <= 0) Add("bidIncrement", "Bid increment must be greater than 0");
        if (auction.ReservePrice < auction.StartingPrice)
            Add("reservePrice", "Reserve price must be at least the starting price");

        if (requireFutureEnd && auction.AuctionEnd <= DateTime.UtcNow)
            Add("auctionEnd", "Auction end must be in the future");

        return errors.ToDictionary(x => x.Key, x => x.Value.ToArray());
    }
}