using Microsoft.AspNetCore.Identity;

namespace IdentityService.Models;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    /// <summary>Display name. Older accounts (seed users, web sign-up) keep it in the "name" claim instead.</summary>
    public string? FullName { get; set; }

    /// <summary>When the user accepted the Terms and Privacy Policy (mobile sign-up).</summary>
    public DateTime? TermsAcceptedAt { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// "Auto-update primary from my current location". On: the app moves the primary location
    /// to the phone's GPS position. Choosing a primary by hand turns it off.
    /// </summary>
    public bool AutoUpdatePrimaryLocation { get; set; } = true;

    // ── Seller accounts (created by an admin, see AdminSellersController) ──

    /// <summary>Optional shop / business name shown for sellers.</summary>
    public string? BusinessName { get; set; }

    /// <summary>ISO country (IN, US, NG, IE) and city an admin entered for a seller.</summary>
    public string? CountryCode { get; set; }
    public string? City { get; set; }

    /// <summary>When the last "set your password" invite was sent.</summary>
    public DateTime? InvitedAt { get; set; }

    /// <summary>Disabled by an admin: can't sign in and refresh tokens stop working.</summary>
    public bool IsDisabled { get; set; }
}
