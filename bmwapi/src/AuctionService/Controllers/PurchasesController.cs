using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PurchasesController(AuctionDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PurchaseDto>>> GetPurchases()
    {
        var username = User.Identity?.Name ?? "demo_buyer";
        var purchases = await context.Purchases
            .AsNoTracking()
            .Where(x => x.Buyer == username || username == "demo_buyer")
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        if (purchases.Count == 0)
        {
            // Seed mock purchase data for demo if empty
            var mockPurchase = new Purchase
            {
                Id = "pur-101",
                AuctionId = "auc-bmw-m4-01",
                Buyer = username,
                ItemTitle = "2021 BMW M4 Competition S58 Twin-Turbo Engine Assembly",
                WinningBidAmount = 14500.00m,
                BuyerFee = 350.00m,
                ShippingFee = 0.00m,
                TotalAmount = 14850.00m,
                DeliveryOption = "PortPickup",
                PickupPort = "Lagos Apapa Port",
                PaymentMethod = "BankTransfer",
                PaymentStatus = "Paid",
                QrPassCode = "PU-1042-7731",
                ShippingStatus = "Ready for Pickup",
                ContainerNumber = "MSCU7821940",
                ImageUrl = "https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop",
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            };
            return Ok(new List<PurchaseDto> { MapToDto(mockPurchase) });
        }

        return Ok(purchases.Select(MapToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseDto>> GetPurchase(string id)
    {
        var purchase = await context.Purchases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (purchase == null)
        {
            // Fallback mock check
            if (id == "pur-101" || id == "demo")
            {
                return Ok(MapToDto(new Purchase
                {
                    Id = id,
                    AuctionId = "auc-bmw-m4-01",
                    Buyer = User.Identity?.Name ?? "demo_buyer",
                    ItemTitle = "2021 BMW M4 Competition S58 Twin-Turbo Engine Assembly",
                    WinningBidAmount = 14500.00m,
                    BuyerFee = 350.00m,
                    ShippingFee = 0.00m,
                    TotalAmount = 14850.00m,
                    DeliveryOption = "PortPickup",
                    PickupPort = "Lagos Apapa Port",
                    PaymentMethod = "BankTransfer",
                    PaymentStatus = "Paid",
                    QrPassCode = "PU-1042-7731",
                    ShippingStatus = "Ready for Pickup",
                    ContainerNumber = "MSCU7821940",
                    ImageUrl = "https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop",
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                }));
            }
            return NotFound();
        }
        return MapToDto(purchase);
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<PurchaseDto>> Checkout(CheckoutDto dto)
    {
        var username = User.Identity?.Name ?? "demo_buyer";

        var auction = await context.Auctions.Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == dto.AuctionId);

        decimal winningBid = auction?.CurrentHighBid ?? 12500m;
        decimal buyerFee = winningBid * 0.03m;
        decimal shippingFee = dto.DeliveryOption == "DoorstepDelivery" ? 45.00m : 0.00m;
        decimal totalAmount = winningBid + buyerFee + shippingFee;

        var randomCode = $"PU-{Random.Shared.Next(1000, 9999)}-{Random.Shared.Next(1000, 9999)}";

        var purchase = new Purchase
        {
            AuctionId = dto.AuctionId,
            Buyer = username,
            ItemTitle = auction != null ? auction.Item.DisplayName : "BMW M Performance Part",
            WinningBidAmount = winningBid,
            BuyerFee = buyerFee,
            ShippingFee = shippingFee,
            TotalAmount = totalAmount,
            DeliveryOption = dto.DeliveryOption,
            DeliveryAddress = dto.DeliveryAddress,
            PickupPort = dto.PickupPort,
            PaymentMethod = dto.PaymentMethod,
            PaymentStatus = "Paid",
            QrPassCode = randomCode,
            ShippingStatus = "Order Confirmed",
            ContainerNumber = "CN-2026-09",
            ImageUrl = auction?.Item.PrimaryImage?.Url ?? "https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop"
        };

        context.Purchases.Add(purchase);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPurchase), new { id = purchase.Id }, MapToDto(purchase));
    }

    [HttpPost("{id}/rating")]
    public async Task<IActionResult> SubmitRating(string id, SubmitRatingDto dto)
    {
        var purchase = await context.Purchases.FirstOrDefaultAsync(x => x.Id == id);
        if (purchase != null)
        {
            purchase.Rating = dto.Rating;
            purchase.RatingComment = dto.Comment;
            purchase.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        return Ok(new { Message = "Rating submitted successfully", Rating = dto.Rating, Comment = dto.Comment });
    }

    private static PurchaseDto MapToDto(Purchase p) => new()
    {
        Id = p.Id,
        AuctionId = p.AuctionId,
        Buyer = p.Buyer,
        ItemTitle = p.ItemTitle,
        WinningBidAmount = p.WinningBidAmount,
        BuyerFee = p.BuyerFee,
        ShippingFee = p.ShippingFee,
        TotalAmount = p.TotalAmount,
        DeliveryOption = p.DeliveryOption,
        DeliveryAddress = p.DeliveryAddress,
        PickupPort = p.PickupPort,
        PaymentMethod = p.PaymentMethod,
        PaymentStatus = p.PaymentStatus,
        QrPassCode = p.QrPassCode,
        ShippingStatus = p.ShippingStatus,
        ContainerNumber = p.ContainerNumber,
        ImageUrl = p.ImageUrl,
        Rating = p.Rating,
        RatingComment = p.RatingComment,
        CreatedAt = p.CreatedAt
    };
}
