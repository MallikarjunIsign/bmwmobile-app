namespace IdentityService.Models;

/// <summary>
/// "Remember this device": the phone skips the email code for a while.
/// The server gives the phone a random device token and stores only its SHA-256 hash.
/// </summary>
public class TrustedDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string DeviceId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
