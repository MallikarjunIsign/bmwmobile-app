namespace IdentityService.Models;

/// <summary>New → (InReview) → Approved (seller account created) or Rejected.</summary>
public enum SellerApplicationStatus
{
    New,
    InReview,
    Approved,
    Rejected,
}

/// <summary>
/// "Want to sell?": a person's request for a seller account, from the app's form (signed in or not). An admin reviews it
/// and creates the seller with the existing admin API (POST /api/admin/sellers or make-seller), which marks it Approved.
/// </summary>
public class SellerApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Contact
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string PhoneNumber { get; set; }
    public required string CountryCode { get; set; }
    public required string City { get; set; }

    // Business
    public string? BusinessName { get; set; }
    /// <summary>Individual, Shop, Dealer, Garage, Scrapyard or Other.</summary>
    public required string BusinessType { get; set; }
    /// <summary>Parts, Vehicles or Both.</summary>
    public required string Sells { get; set; }
    public string? BusinessAddress { get; set; }
    /// <summary>GSTIN (India), EIN (USA), TIN (Nigeria), VAT number (Ireland) – optional.</summary>
    public string? TaxId { get; set; }
    public string? Message { get; set; }

    /// <summary>The applicant's account when they applied while signed in (a buyer who wants to sell).</summary>
    public string? UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SellerApplicationStatus Status { get; set; } = SellerApplicationStatus.New;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? AdminNote { get; set; }
    /// <summary>The seller account made for this application.</summary>
    public string? SellerUserId { get; set; }
}
