namespace IdentityService.Models;

public enum OtpPurpose
{
    Login,
    ResetPassword,
    /// <summary>A new seller sets their first password (longer expiry: Otp:InviteExpiryHours).</summary>
    SellerInvite,
}

/// <summary>A 6-digit email code. Only a salted hash of the code is stored.</summary>
public class OtpCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public OtpPurpose Purpose { get; set; }
    public required string CodeHash { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
