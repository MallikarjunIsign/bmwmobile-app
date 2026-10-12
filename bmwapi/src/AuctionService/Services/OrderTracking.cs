using AuctionService.Entities;

namespace AuctionService.Services;

/// <summary>
/// Order tracking: the steps that apply to the delivery method, who moves an order to each step, and what the
/// caller can do next. The app only shows what this returns.
///
///   Pickup:          Sold → Ready for pickup (seller) → Picked up (seller) → Completed (buyer confirms)
///   Shipping:        Sold → Shipped (seller, courier / tracking optional) → Delivered (buyer confirms) → Completed
///   Not chosen yet:  Sold → Completed (both options offered and the buyer hasn't chosen)
///
/// The seller moves the order forward (steps may be skipped, never undone). The buyer confirms the end and may do so
/// at any time, so a seller who forgets to update can't block the order. Confirming delivery completes the order.
/// </summary>
public static class OrderTracking
{
    /// <summary>A step; <paramref name="ByBuyer"/> steps are the buyer's to confirm.</summary>
    public record Step(OrderStatus Status, string Label, bool ByBuyer);

    /// <summary>A new order ("Sold") for an auction win or a fixed-price purchase.</summary>
    public static Order NewOrder(Auction auction, string buyer, int quantity, int total, string? imageUrl)
    {
        var item = auction.Item;
        var options = Normalize(item.DeliveryOptions);
        return new Order
        {
            AuctionId = auction.Id,
            Seller = auction.Seller,
            Buyer = buyer,
            SaleType = auction.SaleType,
            Quantity = quantity,
            FinalTotal = total,
            PartPrice = auction.StartingPrice * quantity,
            ShippingFee = options.HasFlag(DeliveryOptions.Shipping) ? item.ShippingFee ?? 0 : null,
            CountryCode = item.CountryCode,
            PickupAddress = options.HasFlag(DeliveryOptions.Pickup) ? item.PickupAddress : null,
            Currency = item.Currency,
            Title = item.Title ?? item.DisplayName,
            ImageUrl = imageUrl,
            LocationName = item.LocationName,
            DeliveryOptions = options,
            // Only one way offered → no need to ask the buyer (Shipping still needs their address).
            DeliveryMethod = DefaultMethod(options),
            Status = OrderStatus.SoldAwaitingHandover,
            SellerEmail = auction.SellerEmail,
            SellerPhone = auction.SellerPhone
        };
    }

    /// <summary>"Pickup" / "Shipping" (older apps send "SellerShips"; "BuyerArranges" no longer exists).</summary>
    public static DeliveryOptions? ParseMethod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pickup" => DeliveryOptions.Pickup,
        "shipping" or "sellerships" => DeliveryOptions.Shipping,
        _ => null
    };

    /// <summary>Older listings without any option are treated as Pickup.</summary>
    public static DeliveryOptions Normalize(DeliveryOptions offered)
    {
        var known = offered & (DeliveryOptions.Pickup | DeliveryOptions.Shipping);
        return known == DeliveryOptions.None ? DeliveryOptions.Pickup : known;
    }

    public static IReadOnlyList<Step> Pipeline(DeliveryOptions? method) => method switch
    {
        DeliveryOptions.Shipping =>
        [
            new(OrderStatus.SoldAwaitingHandover, "Sold", false),
            new(OrderStatus.Shipped, "Shipped", false),
            new(OrderStatus.Delivered, "Delivered", true),
            new(OrderStatus.Completed, "Completed", true),
        ],
        DeliveryOptions.Pickup =>
        [
            new(OrderStatus.SoldAwaitingHandover, "Sold", false),
            new(OrderStatus.ReadyForPickup, "Ready for pickup", false),
            new(OrderStatus.PickedUp, "Picked up", false),
            new(OrderStatus.Completed, "Completed", true),
        ],
        _ =>
        [
            new(OrderStatus.SoldAwaitingHandover, "Sold", false),
            new(OrderStatus.Completed, "Completed", true),
        ],
    };

    /// <summary>Position of the order's status in its pipeline (a step from another method counts as the last seller step).</summary>
    public static int Position(Order order)
    {
        var steps = Pipeline(order.DeliveryMethod);
        var index = steps.ToList().FindIndex(s => s.Status == order.Status);
        if (index >= 0) return index;
        return Math.Max(0, steps.ToList().FindLastIndex(s => !s.ByBuyer));
    }

    public static string Label(Order order) => Pipeline(order.DeliveryMethod)[Position(order)].Label;

    /// <summary>The delivery options the buyer can choose from.</summary>
    public static List<DeliveryOptions> Choices(Order order)
    {
        var offered = Normalize(order.DeliveryOptions);
        return new[] { DeliveryOptions.Pickup, DeliveryOptions.Shipping }.Where(o => offered.HasFlag(o)).ToList();
    }

    /// <summary>The method is fixed at once when the listing offered only one way.</summary>
    public static DeliveryOptions? DefaultMethod(DeliveryOptions offered)
    {
        var normalized = Normalize(offered);
        return normalized is DeliveryOptions.Pickup or DeliveryOptions.Shipping ? normalized : null;
    }

    /// <summary>Shipping without an address yet: the buyer has to add one before the seller can ship.</summary>
    public static bool NeedsShippingAddress(Order order) =>
        order.DeliveryMethod == DeliveryOptions.Shipping && order.ShipTo is null && string.IsNullOrEmpty(order.DeliveryAddress);

    /// <summary>The buyer can still choose / change how to receive it (and add the shipping address).</summary>
    public static bool CanChooseMethod(Order order, string user) =>
        order.Buyer == user && order.Status == OrderStatus.SoldAwaitingHandover &&
        (Choices(order).Count > 1 || NeedsShippingAddress(order));

    /// <summary>What the buyer pays: the item, plus the shipping fee when it is shipped.</summary>
    public static int Total(Order order) =>
        order.FinalTotal + (order.DeliveryMethod == DeliveryOptions.Shipping ? order.ShippingFee ?? 0 : 0);

    /// <summary>Why <paramref name="user"/> can't move the order to <paramref name="next"/> (null = allowed).</summary>
    public static string? CheckMove(Order order, string user, OrderStatus next)
    {
        if (order.Status == OrderStatus.Completed) return "This order is already completed";
        var steps = Pipeline(order.DeliveryMethod);
        var target = steps.ToList().FindIndex(s => s.Status == next);
        if (target < 0) return "That step doesn't apply to this delivery method";

        if (steps[target].ByBuyer)
            return order.Buyer == user ? null : "Only the buyer can confirm this";

        if (order.Seller != user) return "Only the seller can update this step";
        if (order.DeliveryMethod is null) return "Waiting for the buyer to choose pickup or shipping";
        if (next == OrderStatus.Shipped && NeedsShippingAddress(order)) return "Waiting for the buyer's shipping address";
        return target > Position(order) ? null : "The order is already past this step";
    }

    /// <summary>What the caller can do now: the next seller step (seller), or the buyer's confirmation.</summary>
    public static List<Step> NextActions(Order order, string user)
    {
        if (order.Status == OrderStatus.Completed) return [];
        var steps = Pipeline(order.DeliveryMethod);
        // The buyer's first step to confirm ("Delivered" for shipping, "Completed" otherwise).
        if (order.Buyer == user) return NeedsShippingAddress(order) ? [] : [steps.First(s => s.ByBuyer)];
        if (order.Seller != user || order.DeliveryMethod is null || NeedsShippingAddress(order)) return [];
        var next = steps[Position(order) + 1];
        return next.ByBuyer ? [] : [next];
    }

    /// <summary>
    /// The steps a move goes through: confirming delivery also completes the order; the seller may skip steps.
    /// </summary>
    public static List<OrderStatus> StepsFor(OrderStatus next) =>
        next == OrderStatus.Delivered ? [OrderStatus.Delivered, OrderStatus.Completed] : [next];
}
