using System.Net.Http.Json;
using IdentityService.Models;
using IdentityService.Options;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>
/// Opens a new seller's payment account in AuctionService (Stripe Connect, USA / Ireland) when an admin creates a
/// seller or upgrades a buyer. Best effort: if AuctionService is down, the seller gets the account on their first visit
/// to Profile → Payments, so the admin's action never fails because of it.
/// </summary>
public interface ISellerPaymentAccounts
{
    Task OpenAsync(ApplicationUser seller, CancellationToken ct);
}

public class SellerPaymentAccounts(IHttpClientFactory http, IOptions<RoleBackfillOptions> options, ILogger<SellerPaymentAccounts> logger)
    : ISellerPaymentAccounts
{
    public async Task OpenAsync(ApplicationUser seller, CancellationToken ct)
    {
        var baseUrl = options.Value.AuctionServiceUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(seller.UserName)) return;
        try
        {
            using var response = await http.CreateClient(nameof(SellerPaymentAccounts)).PostAsJsonAsync(
                $"{baseUrl.TrimEnd('/')}/api/internal/payment-accounts",
                new { seller = seller.UserName, country = seller.CountryCode, email = seller.Email, name = seller.FullName }, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Payment account for seller {Seller} not opened ({Status}); it is opened on their first visit to Payments",
                    seller.UserName, (int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("AuctionService not reachable: payment account for {Seller} is opened on their first visit to Payments", seller.UserName);
        }
    }
}
