using System.Security.Claims;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// "+ Sell": common parts, vehicle-specific parts and vehicles (design doc §4–§6).
/// A listing starts as a private Draft (saved automatically by the app), gets photos, and is published:
/// only then do BidService / SearchService hear about it (AuctionCreated). Before the first bid the seller can still
/// change everything or cancel; after it only photos and description notes can be added.
/// </summary>
[ApiController]
[Authorize]
[Route("api/listings")]
public class ListingsController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox,
    AuctionMapper mapper, CustomOrderMatcher customOrders, ListingImageService images, TimeProvider clock) : ControllerBase
{
    private string Username => User.Identity?.Name ?? throw new UnauthorizedAccessException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Starts a draft: "What are you adding?" (+ the part type when already chosen).</summary>
    [HttpPost]
    public async Task<ActionResult<AuctionDto>> Create(CreateListingDto dto)
    {
        if (!Enum.TryParse<ListingKind>(dto.Kind, true, out var kind))
            return BadRequest(new { message = "kind must be CommonPart, VehicleSpecificPart or Vehicle" });

        var id = Guid.NewGuid().ToString();
        var country = RegionRules.IsSupported(dto.CountryCode) ? dto.CountryCode!.ToUpperInvariant() : null;
        var auction = new Auction
        {
            Id = id,
            Seller = Username,
            SellerName = DisplayName(User),
            Status = Status.Draft,
            StartingPrice = 1,
            ReservePrice = 1,
            BidIncrement = 1,
            // The default end the seller can change (a whole hour, a week from now).
            AuctionEnd = new DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 0, 0, DateTimeKind.Utc).AddDays(7).AddHours(1),
            Item = new Item
            {
                Id = id,
                Kind = kind,
                ItemType = kind == ListingKind.Vehicle ? ItemType.Vehicle : ItemType.Part,
                Make = "",
                Model = "",
                Description = "",
                CountryCode = country,
                Currency = RegionRules.CurrencyFor(country),
                LocationName = ListingRules.Clean(dto.LocationName, 120),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                DeliveryOptions = DeliveryOptions.Pickup,
                VehicleDetails = kind == ListingKind.Vehicle
                    ? new VehicleDetails { ItemId = id, MileageUnit = RegionRules.DistanceUnit(country) }
                    : null,
                PartDetails = kind == ListingKind.Vehicle ? null : new PartDetails { ItemId = id }
            }
        };

        if (dto.PartTypeId is not null && kind != ListingKind.Vehicle)
        {
            var partType = await LoadPartTypeAsync(dto.PartTypeId.Value);
            if (partType is null) return BadRequest(new { message = "Unknown part type" });
            auction.Item.PartTypeId = partType.Id;
            auction.Item.PartType = partType;
        }

        ListingRules.SyncDerivedFields(auction.Item);
        // The seller's rating travels with the listing (kept current when reviews change).
        if (await context.SellerRatings.AsNoTracking().FirstOrDefaultAsync(s => s.Seller == Username) is { Count: > 0 } rating)
            (auction.SellerRatingAverage, auction.SellerRatingCount) = (rating.Average, rating.Count);
        context.Auctions.Add(auction);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id }, mapper.ToDto(auction, Username));
    }

    /// <summary>The seller's view of a listing (drafts included, full VIN).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionDto>> Get(string id)
    {
        var auction = await context.Auctions.AsNoTracking().WithDetails().FirstOrDefaultAsync(x => x.Id == id);
        if (auction == null) return NotFound();
        if (auction.Seller != Username && auction.Status is Status.Draft or Status.Cancelled) return NotFound();
        return mapper.ToDto(auction, Username);
    }

    /// <summary>
    /// Saves the sent fields (the app autosaves each step). Drafts and Live listings without bids only.
    /// A Live listing is re-checked with the publish rules and the change is sent to Bid/Search.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<AuctionDto>> Save(string id, SaveListingDto dto)
    {
        var (auction, error) = await LoadOwnAsync(id);
        if (error != null) return error;

        if (auction!.Status is not (Status.Draft or Status.Live))
            return BadRequest(new { message = "This listing has ended and can't be changed" });
        if (HasBids(auction))
            return BadRequest(new { message = "After the first bid only photos and description notes can be added" });
        if (auction.QuantitySold > 0 && string.Equals(dto.SaleType, nameof(SaleType.Auction), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Some units are already sold – it stays a fixed-price listing" });

        PartType? partType = null;
        if (dto.PartTypeId is not null)
        {
            partType = await LoadPartTypeAsync(dto.PartTypeId.Value);
            if (partType is null) return BadRequest(new { message = "Unknown part type" });
        }

        // Replace the collections (the dto sends the whole list).
        if (dto.Fitments is not null) context.PartFitments.RemoveRange(auction.Item.Fitments);
        if (dto.PartNumbers is not null) context.PartNumbers.RemoveRange(auction.Item.PartNumbers);
        if (dto.Specs is not null) context.PartSpecs.RemoveRange(auction.Item.Specs);

        var fitmentsBefore = auction.Item.Fitments.ToList();
        ListingRules.Apply(dto, auction, partType ?? auction.Item.PartType);
        // A common part drops its fitment rows: make sure EF deletes them.
        foreach (var removed in fitmentsBefore.Where(f => !auction.Item.Fitments.Contains(f)))
            context.PartFitments.Remove(removed);

        var shapeErrors = ShapeErrors(dto, Now);
        if (shapeErrors.Count > 0) return Invalid(shapeErrors);

        auction.UpdatedAt = Now;
        if (auction.Status == Status.Live)
        {
            // A Live fixed-price listing never ends; one switched to an auction ends when the seller chose.
            if (auction.SaleType == SaleType.FixedPrice) auction.AuctionEnd = ListingRules.NoEnd;
            var errors = ListingRules.ValidateForPublish(auction, Now);
            if (errors.Count > 0) return Invalid(errors);
            await outbox.PublishAsync(mapper.ToUpdated(auction));
        }

        await outbox.SaveChangesAndFlushMessagesAsync();
        return mapper.ToDto(auction, Username);
    }

    /// <summary>Checks everything (fitment, specs, details, photos, location, auction settings) and goes Live.</summary>
    [HttpPost("{id}/publish")]
    public async Task<ActionResult<AuctionDto>> Publish(string id)
    {
        var (auction, error) = await LoadOwnAsync(id);
        if (error != null) return error;
        if (auction!.Status != Status.Draft) return BadRequest(new { message = "Only drafts can be published" });

        ListingRules.SyncDerivedFields(auction.Item);
        var now = Now;
        var errors = ListingRules.ValidateForPublish(auction, now);
        if (errors.Count > 0) return Invalid(errors);

        auction.Status = Status.Live;
        auction.CreatedAt = now;
        auction.UpdatedAt = now;
        // Auctions end when the seller chose; fixed-price listings stay until sold out or cancelled.
        auction.AuctionEnd = ListingRules.EndWhenPublished(auction, now);
        // No reserve in this phase; the bid step comes from BidService's tier table.
        auction.ReservePrice = auction.StartingPrice;
        auction.BidIncrement = 1;
        (auction.SellerEmail, auction.SellerPhone) = Contact(User);
        auction.SellerName = DisplayName(User) ?? auction.SellerName;

        await outbox.PublishAsync(mapper.ToCreated(auction));
        // Buyers waiting for this part (custom orders) are notified in the same transaction.
        await customOrders.MatchNewListingAsync(auction, outbox);
        await outbox.SaveChangesAndFlushMessagesAsync();

        return mapper.ToDto(auction, Username);
    }

    /// <summary>Description notes can be added at any time while Live (also after bids).</summary>
    [HttpPost("{id}/notes")]
    public async Task<ActionResult<AuctionDto>> AddNote(string id, AddNoteDto dto)
    {
        var (auction, error) = await LoadOwnAsync(id);
        if (error != null) return error;
        if (auction!.Status is not (Status.Live or Status.Draft)) return BadRequest(new { message = "This listing has ended" });

        var text = ListingRules.Clean(dto.Text, 500);
        if (text is null) return BadRequest(new { message = "Write the note first" });
        if (ListingRules.ContainsContactDetails(text))
            return BadRequest(new { message = "Please don't put phone numbers or e-mail addresses in notes" });

        var note = $"Update {Now:dd MMM yyyy}: {text}";
        var description = string.IsNullOrWhiteSpace(auction.Item.Description) ? note : $"{auction.Item.Description}\n\n{note}";
        if (description.Length > 2000) return BadRequest(new { message = "The description is full" });

        auction.Item.Description = description;
        auction.UpdatedAt = Now;
        if (auction.Status == Status.Live) await outbox.PublishAsync(mapper.ToUpdated(auction));
        await outbox.SaveChangesAndFlushMessagesAsync();
        return mapper.ToDto(auction, Username);
    }

    /// <summary>
    /// Cancel: a draft is deleted (with its photos); a Live listing without bids becomes Cancelled and is removed
    /// from bidding and search. Not possible once there are bids.
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        var (auction, error) = await LoadOwnAsync(id);
        if (error != null) return error;

        if (auction!.Status == Status.Draft)
        {
            var photos = auction.Item.Images.ToList();
            context.Auctions.Remove(auction);
            await context.SaveChangesAsync();
            foreach (var photo in photos) await images.DeleteFilesAsync(photo);
            return NoContent();
        }

        if (auction.Status != Status.Live) return BadRequest(new { message = "This listing has already ended" });
        if (HasBids(auction)) return BadRequest(new { message = "A listing with bids can't be cancelled" });

        auction.Status = Status.Cancelled;
        auction.UpdatedAt = Now;
        await outbox.PublishAsync(new AuctionDeleted { Id = auction.Id });
        await outbox.SaveChangesAndFlushMessagesAsync();
        return NoContent();
    }

    /// <summary>"Ended – no bids" → relist with one tap: a new Live listing with the same details, price and duration.</summary>
    [HttpPost("{id}/relist")]
    public async Task<ActionResult<AuctionDto>> Relist(string id)
    {
        var source = await context.Auctions.AsNoTracking().WithDetails().FirstOrDefaultAsync(x => x.Id == id);
        if (source == null) return NotFound();
        if (source.Seller != Username) return Forbid();
        if (source.Status is not (Status.Unsold or Status.ReserveNotMet))
            return BadRequest(new { message = "Only listings that ended without a sale can be relisted" });

        var copy = await CopyAsync(source);
        var now = Now;
        copy.Status = Status.Live;
        copy.CreatedAt = now;
        copy.UpdatedAt = now;
        // Runs as long as the original did (seller-chosen end), or the older "N days".
        var length = source.Item.DurationDays is { } days
            ? TimeSpan.FromDays(days)
            : Clamp(source.AuctionEnd - source.CreatedAt, ListingRules.MinAuctionLength, ListingRules.MaxAuctionLength);
        copy.AuctionEnd = now + length;
        (copy.SellerEmail, copy.SellerPhone) = Contact(User);
        copy.SellerName = DisplayName(User) ?? source.SellerName;
        (copy.SellerRatingAverage, copy.SellerRatingCount) = (source.SellerRatingAverage, source.SellerRatingCount);

        context.Auctions.Add(copy);
        await outbox.PublishAsync(mapper.ToCreated(copy));
        await customOrders.MatchNewListingAsync(copy, outbox);
        await outbox.SaveChangesAndFlushMessagesAsync();

        return CreatedAtAction(nameof(Get), new { id = copy.Id }, mapper.ToDto(copy, Username));
    }

    /// <summary>My Listings: Draft / Live / Ended – no bids / Sold / Completed / Cancelled, newest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<MyListingDto>>> Mine()
    {
        var auctions = await context.Auctions.AsNoTracking()
            .Include(x => x.Item).ThenInclude(x => x.Images)
            .Include(x => x.Item).ThenInclude(x => x.PartType)
            .Where(x => x.Seller == Username)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync();
        var ids = auctions.Select(a => a.Id).ToList();
        var orders = await context.Orders.AsNoTracking().Where(o => ids.Contains(o.AuctionId)).ToListAsync();

        return auctions.Select(a =>
        {
            var mine = orders.Where(o => o.AuctionId == a.Id).OrderByDescending(o => o.CreatedAt).ToList();
            return new MyListingDto
            {
                Id = a.Id,
                Title = a.Item.Title ?? a.Item.DisplayName,
                State = StateOf(a, mine),
                Status = a.Status.ToString(),
                Kind = a.Item.Kind?.ToString(),
                ImageUrl = mapper.CoverUrl(a),
                PartPrice = a.StartingPrice,
                CurrentHighBid = a.CurrentHighBid,
                BidCount = a.BidCount,
                AuctionEnd = a.AuctionEnd,
                UpdatedAt = a.UpdatedAt,
                Currency = a.Item.Currency,
                OrderId = mine.FirstOrDefault()?.Id,
                SaleType = a.SaleType.ToString(),
                QuantityAvailable = a.QuantityAvailable,
                QuantitySold = a.QuantitySold
            };
        }).ToList();
    }

    /// <summary>
    /// Buy now: units of a fixed-price listing at its price. Each purchase is its own order (no online payment in this
    /// phase); the last unit marks the listing Sold. Two buyers racing for the last unit: the second gets 409.
    /// </summary>
    [HttpPost("{id}/buy")]
    public async Task<ActionResult<OrderDto>> Buy(string id, BuyListingDto dto)
    {
        var auction = await context.Auctions.WithDetails().FirstOrDefaultAsync(x => x.Id == id);
        if (auction == null || auction.Status is Status.Draft or Status.Cancelled) return NotFound();
        if (auction.SaleType != SaleType.FixedPrice) return BadRequest(new { message = "This is an auction – place a bid instead" });
        if (auction.Seller == Username) return BadRequest(new { message = "You can't buy your own listing" });
        if (await context.UserBlocks.AnyAsync(b => b.Blocker == auction.Seller && b.Blocked == Username))
            return BadRequest(new { message = "You can't buy from this seller" });
        if (auction.Status != Status.Live || auction.QuantityAvailable == 0) return BadRequest(new { message = "Sorry, this is sold out" });
        if (dto.Quantity < 1) return BadRequest(new { message = "Buy at least 1" });
        if (dto.Quantity > auction.QuantityAvailable)
            return BadRequest(new { message = $"Only {auction.QuantityAvailable} left" });

        var now = Now;
        auction.QuantitySold += dto.Quantity;
        auction.UpdatedAt = now;
        if (auction.QuantityAvailable == 0)
        {
            auction.Status = Status.Sold;
            auction.Winner = Username;
            auction.SoldAmount = auction.StartingPrice;
        }

        var order = OrderTracking.NewOrder(auction, Username, dto.Quantity, auction.StartingPrice * dto.Quantity, mapper.CoverUrl(auction));
        order.CreatedAt = now;
        (order.BuyerEmail, order.BuyerPhone) = Contact(User);
        context.Orders.Add(order);

        // Search shows the units left (and drops the listing once sold out); the seller is told to hand it over.
        await outbox.PublishAsync(mapper.ToUpdated(auction));
        await outbox.PublishAsync(new OrderCreated { OrderId = order.Id, AuctionId = auction.Id, Seller = auction.Seller, Buyer = Username });
        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = auction.Id, Title = order.Title, NotifyUser = auction.Seller,
            StatusLabel = dto.Quantity > 1 ? $"New order: {dto.Quantity} units bought" : "New order: bought"
        });

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Someone else just bought it – check how many are left" });
        }

        return OrdersController.ToDto(order, Username);
    }

    /// <summary>"Copy fitment from my last listing": the fitment rows of the seller's latest vehicle-specific part.</summary>
    [HttpGet("last-fitment")]
    public async Task<ActionResult<List<FitmentDto>>> LastFitment([FromQuery] string? exceptId)
    {
        var latest = await context.Auctions.AsNoTracking()
            .Include(x => x.Item).ThenInclude(x => x.Fitments)
            .Where(x => x.Seller == Username && x.Id != exceptId && x.Item.Fitments.Count > 0)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync();

        return (latest?.Item.Fitments ?? []).OrderBy(f => f.SortOrder).Select(f => new FitmentDto
        {
            YearFrom = f.YearFrom, YearTo = f.YearTo, MakeId = f.MakeId, Make = f.Make, ModelId = f.ModelId, Model = f.Model,
            Trim = f.Trim, Engine = f.Engine, Fuel = f.Fuel, Transmission = f.Transmission, Drivetrain = f.Drivetrain,
            SteeringSide = f.SteeringSide, Market = f.Market, Note = f.Note
        }).ToList();
    }

    /// <summary>Sold → Completed once every order of the listing is completed.</summary>
    public static string StateOf(Auction auction, IReadOnlyCollection<Order> orders) => auction.Status switch
    {
        Status.Draft => "Draft",
        Status.Live => "Live",
        Status.Cancelled => "Cancelled",
        Status.Sold => orders.Count > 0 && orders.All(o => o.Status == OrderStatus.Completed) ? "Completed" : "Sold",
        _ => "EndedNoBids"
    };

    // ── helpers ────────────────────────────────────────────────────────

    private static bool HasBids(Auction auction) => auction.BidCount > 0 || auction.CurrentHighBid > 0;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>E-mail and phone from the sign-in token (shown to the other party only after a sale).</summary>
    public static (string? Email, string? Phone) Contact(ClaimsPrincipal user) =>
        (user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value,
         user.FindFirst("phone_number")?.Value ?? user.FindFirst(ClaimTypes.MobilePhone)?.Value);

    /// <summary>The seller's display name from the sign-in token ("name" claim), shown on their listings.</summary>
    public static string? DisplayName(ClaimsPrincipal user) =>
        ListingRules.Clean(user.FindFirst("name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value, 100);

    private async Task<(Auction? Auction, ActionResult? Error)> LoadOwnAsync(string id)
    {
        var auction = await context.Auctions.WithDetails().FirstOrDefaultAsync(x => x.Id == id);
        if (auction == null) return (null, NotFound());
        if (auction.Seller != Username) return (null, Forbid());
        return (auction, null);
    }

    private Task<PartType?> LoadPartTypeAsync(int id) =>
        context.PartTypes.Include(p => p.Category).ThenInclude(c => c!.Parent).FirstOrDefaultAsync(p => p.Id == id);

    private static Dictionary<string, string[]> ShapeErrors(SaveListingDto dto, DateTime now)
    {
        var errors = new Dictionary<string, string[]>();
        if (dto.PartPrice is <= 0) errors["partPrice"] = ["The part price must be more than 0"];
        if (dto.ShippingFee is < 0) errors["shippingFee"] = ["The shipping fee can't be negative"];
        if (dto.DurationDays is not null && !ListingRules.Durations.Contains(dto.DurationDays.Value))
            errors["durationDays"] = ["Choose 1, 3, 5, 7 or 10 days"];
        if (dto.SaleType is not null && !(Enum.TryParse<SaleType>(dto.SaleType, true, out var saleType) && Enum.IsDefined(saleType)))
            errors["saleType"] = ["Choose Auction or Fixed price"];
        if (dto.AuctionEnd is { } end)
        {
            var utc = ListingRules.AsUtc(end);
            if (utc < now + ListingRules.MinAuctionLength)
                errors["auctionEnd"] = ["Choose an end date and time at least 1 hour from now"];
            else if (utc > now + ListingRules.MaxAuctionLength)
                errors["auctionEnd"] = [$"An auction can run for at most {ListingRules.MaxAuctionLength.TotalDays:0} days"];
        }
        if (dto.Fitments is { Count: > ListingRules.MaxFitmentRows })
            errors["fitments"] = [$"At most {ListingRules.MaxFitmentRows} vehicles"];
        if ((dto.Latitude is null) != (dto.Longitude is null) ||
            dto.Latitude is not null && !GeoMath.IsValidCoordinate(dto.Latitude.Value, dto.Longitude!.Value))
            errors["location"] = ["Location must have a valid latitude and longitude"];
        return errors;
    }

    private ActionResult Invalid(Dictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }

    /// <summary>A deep copy for relisting (own photo files, same details).</summary>
    private async Task<Auction> CopyAsync(Auction source)
    {
        var id = Guid.NewGuid().ToString();
        var s = source.Item;
        var item = new Item
        {
            Id = id,
            ItemType = s.ItemType,
            Kind = s.Kind,
            FitmentMode = s.FitmentMode,
            PartTypeId = s.PartTypeId,
            Make = s.Make,
            Model = s.Model,
            Year = s.Year,
            YearTo = s.YearTo,
            Color = s.Color,
            Mileage = s.Mileage,
            Description = s.Description,
            PartName = s.PartName,
            PartNumber = s.PartNumber,
            PartCategory = s.PartCategory,
            Condition = s.Condition,
            Quantity = s.Quantity,
            ContainerId = s.ContainerId,
            LocationName = s.LocationName,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            Title = s.Title,
            TitleExtra = s.TitleExtra,
            CountryCode = s.CountryCode,
            Currency = s.Currency,
            DeliveryOptions = s.DeliveryOptions,
            SizeClass = s.SizeClass,
            ShippingFee = s.ShippingFee,
            PickupAddress = s.PickupAddress,
            DurationDays = s.DurationDays,
            PartDetails = s.PartDetails is not { } d ? null : new PartDetails
            {
                ItemId = id, BrandType = d.BrandType, Brand = d.Brand, Position = d.Position, Side = d.Side,
                UsedGrade = d.UsedGrade, DonorMileage = d.DonorMileage, Colour = d.Colour, PaintCode = d.PaintCode,
                Includes = d.Includes, Warranty = d.Warranty
            },
            Fitments = s.Fitments.Select(f => new PartFitment
            {
                ItemId = id, SortOrder = f.SortOrder, YearFrom = f.YearFrom, YearTo = f.YearTo, MakeId = f.MakeId, Make = f.Make,
                ModelId = f.ModelId, Model = f.Model, Trim = f.Trim, Engine = f.Engine, Fuel = f.Fuel,
                Transmission = f.Transmission, Drivetrain = f.Drivetrain, SteeringSide = f.SteeringSide, Market = f.Market,
                Note = f.Note
            }).ToList(),
            PartNumbers = s.PartNumbers.Select(n => new PartNumber { ItemId = id, Value = n.Value, Normalized = n.Normalized, Kind = n.Kind }).ToList(),
            Specs = s.Specs.Select(x => new PartSpec { ItemId = id, Key = x.Key, Value = x.Value }).ToList(),
            VehicleDetails = s.VehicleDetails is not { } v ? null : new VehicleDetails
            {
                ItemId = id, Trim = v.Trim, BodyType = v.BodyType, Fuel = v.Fuel, EngineSize = v.EngineSize,
                Transmission = v.Transmission, Drivetrain = v.Drivetrain, SteeringSide = v.SteeringSide, Vin = v.Vin,
                RunningStatus = v.RunningStatus, MileageUnit = v.MileageUnit, DamageType = v.DamageType,
                DamageAreas = v.DamageAreas, Keys = v.Keys, Owners = v.Owners, ServiceHistory = v.ServiceHistory,
                InspectionAllowed = v.InspectionAllowed, PapersJson = v.PapersJson
            }
        };

        foreach (var image in s.OrderedImages)
        {
            var copy = await images.CopyAsync(image, id);
            item.Images.Add(copy);
        }

        // The part type is tracked separately; attach it for the title / mapping.
        item.PartType = s.PartTypeId is null ? null : await LoadPartTypeAsync(s.PartTypeId.Value);

        return new Auction
        {
            Id = id,
            Seller = source.Seller,
            StartingPrice = source.StartingPrice,
            ReservePrice = source.StartingPrice,
            BidIncrement = 1,
            Item = item
        };
    }
}
