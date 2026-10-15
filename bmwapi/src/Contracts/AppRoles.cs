using System.Security.Claims;

namespace Contracts;

/// <summary>
/// Marketplace roles. Every access token carries exactly one in the "role" claim (IdentityService):
/// buyer (everyone who signs up in the app), seller (created or upgraded by an admin) and admin.
/// Shared so every service checks the same names and shows the same messages.
/// </summary>
public static class AppRoles
{
    public const string ClaimType = "role";

    public const string Buyer = "buyer";
    public const string Seller = "seller";
    public const string Admin = "admin";

    /// <summary>Highest first: a user with several roles gets the first one that matches.</summary>
    public static readonly string[] All = [Admin, Seller, Buyer];

    public const string SellerOnlyCode = "SELLER_ONLY";
    public const string SellerOnlyMessage =
        "Only seller accounts can list and manage items. Want to sell? Contact us and we'll set up a seller account.";

    public const string AdminOnlyCode = "ADMIN_ONLY";
    public const string AdminOnlyMessage = "Only admins can do this.";

    /// <summary>
    /// The user's role from the token. JWT handlers may map "role" to <see cref="ClaimTypes.Role"/>, so both are read.
    /// No role (older tokens) means buyer.
    /// </summary>
    public static string Of(ClaimsPrincipal? user)
    {
        if (user is null) return Buyer;
        var roles = user.Claims
            .Where(c => c.Type is ClaimType or ClaimTypes.Role)
            .Select(c => c.Value.Trim().ToLowerInvariant())
            .ToHashSet();
        return All.FirstOrDefault(roles.Contains) ?? Buyer;
    }

    public static bool IsSeller(ClaimsPrincipal? user) => Of(user) == Seller;

    public static bool IsAdmin(ClaimsPrincipal? user) => Of(user) == Admin;
}
