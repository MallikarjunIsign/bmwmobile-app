using System.ComponentModel.DataAnnotations;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Errors;
using AuctionService.Services;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

public class OrderDto
{
    public required string Id { get; set; }
    public required string AuctionId { get; set; }
    /// <summary>"buyer" or "seller" – the caller's side of this sale.</summary>
    public required string Role { get; set; }
    public required string Seller { get; set; }
    public required string Buyer { get; set; }
    /// <summary>The other party's username.</summary>
    public required string OtherParty { get; set; }
    public required string Title { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>Auction (won) or FixedPrice (bought).</summary>
    public string SaleType { get; set; } = "Auction";
    /// <summary>Units bought (fixed price can be more than 1).</summary>
    public int Quantity { get; set; } = 1;
    /// <summary>The item: part price + winning bid (auctions) or price × quantity. Shipping not included.</summary>
    public int FinalTotal { get; set; }
    /// <summary>The breakdown: part price, the winning bid on top of it (0 for direct sales), shipping and the total.</summary>
    public int PartPrice { get; set; }
    public int WinningBid { get; set; }
    /// <summary>Older apps: the shipping charge (same as ShippingCharge).</summary>
    public int? ShippingFee { get; set; }
    /// <summary>
    /// 0 for Pickup; for Shipping the charge calculated when the buyer chose it (fixed – later rate changes don't
    /// change it); null while not chosen yet.
    /// </summary>
    public int? ShippingCharge { get; set; }
    /// <summary>Road distance the charge used (km, or mi in the USA) and the item's size class.</summary>
    public int? ShippingDistance { get; set; }
    public string? ShippingDistanceUnit { get; set; }
    public string? ShippingSizeClass { get; set; }
    /// <summary>US: "USPS Ground Advantage" (with its zone) or "Freight".</summary>
    public string? ShippingService { get; set; }
    public int? ShippingZone { get; set; }
    /// <summary>The charge used an industry-average estimate (the app shows "estimated rate").</summary>
    public bool ShippingRateEstimated { get; set; }
    /// <summary>What the buyer pays: FinalTotal + the shipping charge (0 for Pickup).</summary>
    public int Total { get; set; }
    public string? Currency { get; set; }
    public string? LocationName { get; set; }
    /// <summary>The seller's country – shipping goes to addresses there only.</summary>
    public string? CountryCode { get; set; }
    public List<string> DeliveryOptions { get; set; } = [];
    /// <summary>Pickup / Shipping; null until the buyer chose (when the listing offered both).</summary>
    public string? DeliveryMethod { get; set; }
    /// <summary>Delivery options the buyer can pick from while choosing is open (empty otherwise).</summary>
    public List<string> DeliveryChoices { get; set; } = [];
    /// <summary>Shipping is the method but the buyer hasn't given the address yet.</summary>
    public bool NeedsShippingAddress { get; set; }
    /// <summary>Shipping: where it goes (both parties see it).</summary>
    public AddressDto? ShipTo { get; set; }
    /// <summary>One line, also for older orders.</summary>
    public string? DeliveryAddress { get; set; }
    /// <summary>Pickup: the seller's exact address (the buyer sees it after the sale), else the public area.</summary>
    public string? PickupAddress { get; set; }
    public string? Courier { get; set; }
    public string? TrackingNumber { get; set; }
    /// <summary>SoldAwaitingHandover, ReadyForPickup, PickedUp, Shipped, Delivered, Completed.</summary>
    public required string Status { get; set; }
    /// <summary>The current step's name for this delivery method ("Shipped", "Picked up", …).</summary>
    public required string StatusLabel { get; set; }
    /// <summary>The tracking steps for this delivery method, in order.</summary>
    public List<OrderStepDto> Tracking { get; set; } = [];
    /// <summary>What the caller can do now (the next step for the seller, the confirmation for the buyer).</summary>
    public List<OrderActionDto> Actions { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? HandedOverAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>The other party's contact (only the two parties of the sale ever see it).</summary>
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    /// <summary>The buyer's review of this order, if written.</summary>
    public string? ReviewId { get; set; }
    /// <summary>The buyer can review it now ("How was your purchase?") – until ReviewDeadline.</summary>
    public bool CanReview { get; set; }
    public DateTime? ReviewDeadline { get; set; }
}

public record OrderStepDto(string Status, string Label, bool Done, bool Current, DateTime? At, string? Note);

public record OrderActionDto(string Status, string Label);

public class OrderStatusDto
{
    [Required]
    public string Status { get; set; } = "";
    [MaxLength(60)]
    public string? Courier { get; set; }
    [MaxLength(60)]
    public string? TrackingNumber { get; set; }
}

/// <summary>
/// Orders: a sale is created when an auction is Sold or a direct sale is bought (no online payment in this phase), then
/// tracked step by step (see <see cref="OrderTracking"/>): the buyer chooses Pickup / Shipping (with the address) when
/// both were offered, the seller moves it forward, the buyer confirms. Every step notifies the other party.
/// </summary>
[ApiController]
[Authorize]
[Route("api/orders")]
public class OrdersController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox, TimeProvider clock,
    ShippingService shipping) : ControllerBase
{
    private string Username => User.Identity?.Name ?? throw new UnauthorizedAccessException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>The caller's sales and purchases, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<OrderDto>>> Mine()
    {
        var user = Username;
        var orders = await context.Orders.Include(o => o.Events).Where(o => o.Buyer == user || o.Seller == user)
            .OrderByDescending(o => o.CreatedAt).ToListAsync();

        // Remember the caller's contact so the other party can reach them.
        if (orders.Count > 0 && RememberContact(orders, user)) await context.SaveChangesAsync();

        var reviews = await ReviewIdsAsync(orders.Select(o => o.Id));
        return orders.Select(o => WithReview(ToDto(o, user), o, user, reviews)).ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> Get(string id)
    {
        var (order, error) = await LoadAsync(id);
        if (error != null) return error;
        if (RememberContact([order!], Username)) await context.SaveChangesAsync();
        return WithReview(ToDto(order!, Username), order!, Username, await ReviewIdsAsync([order!.Id]));
    }

    private Task<Dictionary<string, string>> ReviewIdsAsync(IEnumerable<string> orderIds)
    {
        var ids = orderIds.ToList();
        return context.Reviews.AsNoTracking().Where(r => ids.Contains(r.OrderId)).ToDictionaryAsync(r => r.OrderId, r => r.Id);
    }

    private OrderDto WithReview(OrderDto dto, Order order, string user, Dictionary<string, string> reviews)
    {
        dto.ReviewId = reviews.GetValueOrDefault(order.Id);
        dto.CanReview = ReviewService.CannotReview(order, user, dto.ReviewId != null, Now) is null;
        dto.ReviewDeadline = order.CompletedAt + ReviewService.WriteWindow;
        return dto;
    }

    /// <summary>
    /// The buyer chooses Pickup or Shipping (when both were offered), or adds the shipping address (Shipping only).
    /// Shipping goes to an address-book entry or a new address (saved to the address book), in the seller's country.
    /// </summary>
    [HttpPost("{id}/delivery-method")]
    public async Task<ActionResult<OrderDto>> ChooseDelivery(string id, ChooseDeliveryDto dto)
    {
        var (order, error) = await LoadAsync(id);
        if (error != null) return error;
        if (order!.Buyer != Username) return Forbidden.With("Only the buyer can choose how to get this order", "NOT_BUYER");
        if (order.Status != OrderStatus.SoldAwaitingHandover)
            return BadRequest(new { message = "The seller has already started on this order" });
        if (OrderTracking.ParseMethod(dto.Method) is not { } method || !OrderTracking.Choices(order).Contains(method))
            return BadRequest(new { message = "Choose one of the delivery options of this listing" });

        if (method == DeliveryOptions.Shipping)
        {
            AddressInfo address;
            if (dto.AddressId is { Length: > 0 } addressId)
            {
                var saved = await context.ShippingAddresses.FirstOrDefaultAsync(a => a.Id == addressId && a.User == Username);
                if (saved is null) return BadRequest(new { message = "That address isn't in your address book" });
                address = Copy(saved.Address);
            }
            else
            {
                var errors = AddressRules.Validate(dto.Address);
                if (errors.Count > 0) return Invalid(errors);
                address = AddressRules.ToInfo(dto.Address!);
                if (dto.SaveAddress) await ShippingAddressesController.AddToBookAsync(context, Username, address, Now);
            }

            if (order.CountryCode is { } country && !string.Equals(address.CountryCode, country, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = $"The seller ships within {RegionRules.CountryName(country)} only" });

            // The delivery location (saved location or GPS) gives the distance for the charge.
            if (dto.Latitude is { } lat && dto.Longitude is { } lon && GeoMath.IsValidCoordinate(lat, lon))
                (address.Latitude, address.Longitude) = (lat, lon);
            if (address.Latitude is null || address.Longitude is null)
                return BadRequest(new { message = "Choose your delivery location (a saved location or your current location) so we can work out the shipping charge", code = "LOCATION_REQUIRED" });

            // Calculated here and fixed on the order (the app never sends a price).
            var item = await context.Set<Item>().Include(i => i.PartType).FirstOrDefaultAsync(i => i.Id == order.AuctionId);
            if (item is null) return BadRequest(new { message = "This listing no longer exists" });
            var quote = await shipping.QuoteAsync(item, address.CountryCode, address.Latitude.Value, address.Longitude.Value, order.ShippingSizeClass);
            if (!quote.Available) return BadRequest(new { message = quote.Reason, code = "SHIPPING_NOT_AVAILABLE" });

            order.ShipTo = address;
            order.DeliveryAddress = address.OneLine();
            order.ShippingFee = quote.Charge;
            order.ShippingDistance = quote.Distance;
            order.ShippingDistanceUnit = quote.Unit;
            order.ShippingService = quote.Service;
            order.ShippingZone = quote.Zone;
            order.ShippingRateEstimated = quote.EstimatedRate;
            order.ShippingSizeClass ??= Enum.Parse<SizeClass>(quote.SizeClass);
        }
        else
        {
            // Pickup is free.
            order.ShipTo = null;
            order.DeliveryAddress = null;
            order.ShippingFee = null;
            order.ShippingDistance = null;
            order.ShippingDistanceUnit = null;
            order.ShippingService = null;
            order.ShippingZone = null;
            order.ShippingRateEstimated = false;
        }

        order.DeliveryMethod = method;
        await PublishAsync(order, order.Seller,
            method == DeliveryOptions.Pickup ? "Buyer will pick it up" : "Buyer chose shipping – ship it");
        await outbox.SaveChangesAndFlushMessagesAsync();
        return ToDto(order, Username);
    }

    /// <summary>Moves the order to a tracking step (seller: ready for pickup, picked up, shipped; buyer: delivered / completed).</summary>
    [HttpPost("{id}/status")]
    public async Task<ActionResult<OrderDto>> SetStatus(string id, OrderStatusDto dto)
    {
        var (order, error) = await LoadAsync(id);
        if (error != null) return error;
        if (ParseStatus(dto.Status) is not { } next)
            return BadRequest(new { message = "Unknown order step" });

        var problem = OrderTracking.CheckMove(order!, Username, next);
        if (problem != null) return problem.StartsWith("Only") ? Forbidden.With(problem, "NOT_ORDER_PARTY") : BadRequest(new { message = problem });

        if (next == OrderStatus.Shipped)
        {
            order!.Courier = Clean(dto.Courier) ?? order.Courier;
            order.TrackingNumber = Clean(dto.TrackingNumber) ?? order.TrackingNumber;
        }

        return await MoveAsync(order!, next);
    }

    /// <summary>Older app versions: the seller marks the handover step of the order's delivery method.</summary>
    [HttpPost("{id}/handed-over")]
    public async Task<ActionResult<OrderDto>> HandedOver(string id)
    {
        var (order, error) = await LoadAsync(id);
        if (error != null) return error;
        if (order!.Seller != Username) return Forbidden.With("Only this order's seller can update fulfilment", "NOT_ORDER_SELLER");
        var step = order.DeliveryMethod == DeliveryOptions.Shipping ? OrderStatus.Shipped : OrderStatus.PickedUp;
        if (OrderTracking.CheckMove(order, Username, step) is { } problem)
            return BadRequest(new { message = problem });

        return await MoveAsync(order, step);
    }

    /// <summary>Older app versions: the buyer confirms they received it.</summary>
    [HttpPost("{id}/received")]
    public async Task<ActionResult<OrderDto>> Received(string id)
    {
        var (order, error) = await LoadAsync(id);
        if (error != null) return error;
        if (order!.Buyer != Username) return Forbidden.With("Only the buyer can confirm they received it", "NOT_BUYER");
        if (order.Status == OrderStatus.Completed) return BadRequest(new { message = "This order is already completed" });
        return await MoveAsync(order, order.DeliveryMethod == DeliveryOptions.Shipping ? OrderStatus.Delivered : OrderStatus.Completed);
    }

    private async Task<ActionResult<OrderDto>> MoveAsync(Order order, OrderStatus next)
    {
        var now = Now;
        foreach (var step in OrderTracking.StepsFor(next))
        {
            order.Status = step;
            if (step is OrderStatus.Shipped or OrderStatus.ReadyForPickup or OrderStatus.PickedUp) order.HandedOverAt ??= now;
            if (step == OrderStatus.Completed)
            {
                // The buyer may confirm even if the seller forgot to update the steps.
                order.HandedOverAt ??= now;
                order.CompletedAt = now;
                // The app asks "How was your purchase?" now; a reminder follows in 2 days if there's no review.
                await outbox.PublishAsync(new Handlers.ReviewReminder(order.Id),
                    new Wolverine.DeliveryOptions { ScheduledTime = now + ReviewService.ReminderAfter });
            }

            var note = step == OrderStatus.Shipped
                ? string.Join(" · ", new[] { order.Courier, order.TrackingNumber is null ? null : $"Tracking {order.TrackingNumber}" }.Where(x => x != null))
                : null;
            order.Events.Add(new OrderEvent { OrderId = order.Id, Status = step, At = now, By = Username, Note = string.IsNullOrEmpty(note) ? null : note });
        }

        await PublishAsync(order, order.Seller == Username ? order.Buyer : order.Seller, OrderTracking.Label(order));
        await outbox.SaveChangesAndFlushMessagesAsync();
        return WithReview(ToDto(order, Username), order, Username, await ReviewIdsAsync([order.Id]));
    }

    /// <summary>The step names, plus the ones older apps send ("Dispatched", "HandedOver").</summary>
    private static OrderStatus? ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dispatched" => OrderStatus.Shipped,
        "handedover" => OrderStatus.PickedUp,
        { } v when Enum.TryParse<OrderStatus>(v, true, out var s) && Enum.IsDefined(s) => s,
        _ => null
    };

    private Task PublishAsync(Order order, string notifyUser, string label) =>
        outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, StatusLabel = label, NotifyUser = notifyUser
        }).AsTask();

    private async Task<(Order? Order, ActionResult? Error)> LoadAsync(string id)
    {
        var order = await context.Orders.Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null || (order.Buyer != Username && order.Seller != Username)) return (null, NotFound());
        return (order, null);
    }

    private ActionResult Invalid(Dictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }

    private static AddressInfo Copy(AddressInfo a) => new()
    {
        Name = a.Name, Phone = a.Phone, Line1 = a.Line1, Line2 = a.Line2, City = a.City, Postcode = a.Postcode, CountryCode = a.CountryCode,
        Latitude = a.Latitude, Longitude = a.Longitude,
    };

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private bool RememberContact(IEnumerable<Order> orders, string user)
    {
        var (email, phone) = ListingsController.Contact(User);
        var changed = false;
        foreach (var o in orders)
        {
            if (o.Buyer == user && (o.BuyerEmail != email || o.BuyerPhone != phone) && (email ?? phone) != null)
            {
                o.BuyerEmail = email ?? o.BuyerEmail;
                o.BuyerPhone = phone ?? o.BuyerPhone;
                changed = true;
            }
            if (o.Seller == user && (o.SellerEmail != email || o.SellerPhone != phone) && (email ?? phone) != null)
            {
                o.SellerEmail = email ?? o.SellerEmail;
                o.SellerPhone = phone ?? o.SellerPhone;
                changed = true;
            }
        }
        return changed;
    }

    public static OrderDto ToDto(Order o, string user)
    {
        var isSeller = o.Seller == user;
        var steps = OrderTracking.Pipeline(o.DeliveryMethod);
        var position = OrderTracking.Position(o);
        return new OrderDto
        {
            Id = o.Id,
            AuctionId = o.AuctionId,
            Role = isSeller ? "seller" : "buyer",
            Seller = o.Seller,
            Buyer = o.Buyer,
            OtherParty = isSeller ? o.Buyer : o.Seller,
            Title = o.Title,
            ImageUrl = o.ImageUrl,
            SaleType = o.SaleType.ToString(),
            Quantity = o.Quantity,
            FinalTotal = o.FinalTotal,
            // Older orders have no part price stored: show it all as the item price.
            PartPrice = o.PartPrice > 0 ? o.PartPrice : o.FinalTotal,
            WinningBid = o.PartPrice > 0 ? Math.Max(o.FinalTotal - o.PartPrice, 0) : 0,
            ShippingFee = o.DeliveryMethod == DeliveryOptions.Pickup ? 0 : o.ShippingFee,
            ShippingCharge = o.DeliveryMethod switch
            {
                DeliveryOptions.Pickup => 0,
                DeliveryOptions.Shipping => o.ShippingFee,
                _ => null,
            },
            ShippingDistance = o.DeliveryMethod == DeliveryOptions.Shipping ? o.ShippingDistance : null,
            ShippingDistanceUnit = o.DeliveryMethod == DeliveryOptions.Shipping ? o.ShippingDistanceUnit : null,
            ShippingSizeClass = o.ShippingSizeClass?.ToString(),
            ShippingService = o.DeliveryMethod == DeliveryOptions.Shipping ? o.ShippingService : null,
            ShippingZone = o.DeliveryMethod == DeliveryOptions.Shipping ? o.ShippingZone : null,
            ShippingRateEstimated = o.DeliveryMethod == DeliveryOptions.Shipping && o.ShippingRateEstimated,
            Total = OrderTracking.Total(o),
            Currency = o.Currency,
            LocationName = o.LocationName,
            CountryCode = o.CountryCode,
            DeliveryOptions = RequestHelpers.AuctionMapper.DeliveryList(OrderTracking.Normalize(o.DeliveryOptions)),
            DeliveryMethod = o.DeliveryMethod?.ToString(),
            DeliveryChoices = OrderTracking.CanChooseMethod(o, user) ? OrderTracking.Choices(o).Select(c => c.ToString()).ToList() : [],
            NeedsShippingAddress = OrderTracking.NeedsShippingAddress(o),
            ShipTo = o.ShipTo is null ? null : AddressRules.ToDto(o.ShipTo),
            DeliveryAddress = o.DeliveryAddress,
            PickupAddress = o.DeliveryMethod == DeliveryOptions.Shipping ? null : o.PickupAddress ?? o.LocationName,
            Courier = o.Courier,
            TrackingNumber = o.TrackingNumber,
            Status = o.Status.ToString(),
            StatusLabel = steps[position].Label,
            Tracking = steps.Select((s, i) =>
            {
                var done = i <= position;
                var ev = o.Events.Where(e => e.Status == s.Status).OrderByDescending(e => e.At).FirstOrDefault();
                var at = i == 0 ? o.CreatedAt : done ? ev?.At : null;
                return new OrderStepDto(s.Status.ToString(), s.Label, done, i == position, at, done ? ev?.Note : null);
            }).ToList(),
            Actions = OrderTracking.NextActions(o, user).Select(s => new OrderActionDto(s.Status.ToString(), s.Label)).ToList(),
            CreatedAt = o.CreatedAt,
            HandedOverAt = o.HandedOverAt,
            CompletedAt = o.CompletedAt,
            ContactEmail = isSeller ? o.BuyerEmail : o.SellerEmail,
            ContactPhone = isSeller ? o.BuyerPhone : o.SellerPhone
        };
    }
}
