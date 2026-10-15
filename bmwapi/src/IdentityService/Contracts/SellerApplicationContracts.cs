using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts;

/// <summary>The app's "Become a seller" form. Signed in or not; a signed-in buyer's application is linked to their account.</summary>
public record SellerApplicationRequest(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Phone number must be in international format, e.g. +919876543210")] string PhoneNumber,
    [Required, RegularExpression("^(?i)(IN|US|NG|IE)$", ErrorMessage = "Country must be IN, US, NG or IE")] string CountryCode,
    [Required, StringLength(100, MinimumLength = 2)] string City,
    [Required, RegularExpression("^(Individual|Shop|Dealer|Garage|Scrapyard|Other)$",
        ErrorMessage = "Business type must be Individual, Shop, Dealer, Garage, Scrapyard or Other")] string BusinessType,
    [Required, RegularExpression("^(Parts|Vehicles|Both)$", ErrorMessage = "What you sell must be Parts, Vehicles or Both")] string Sells,
    [StringLength(100)] string? BusinessName = null,
    [StringLength(250)] string? BusinessAddress = null,
    [StringLength(30)] string? TaxId = null,
    [StringLength(1000)] string? Message = null);

public record SellerApplicationDto(
    Guid Id,
    string FullName,
    string Email,
    string PhoneNumber,
    string CountryCode,
    string City,
    string? BusinessName,
    string BusinessType,
    string Sells,
    string? BusinessAddress,
    string? TaxId,
    string? Message,
    string? UserId,
    DateTime CreatedAt,
    string Status,
    DateTime? ReviewedAt,
    string? ReviewedBy,
    string? AdminNote,
    string? SellerUserId);

/// <summary>What the applicant sees about their own request (no admin-only fields).</summary>
public record MySellerApplicationDto(Guid Id, DateTime CreatedAt, string Status, string? BusinessName, string? AdminNote);

/// <summary>Admin: InReview, Approved or Rejected, with an optional note (a Rejected note is shown to the applicant).</summary>
public record ReviewSellerApplicationRequest(
    [Required, RegularExpression("^(New|InReview|Approved|Rejected)$", ErrorMessage = "Status must be New, InReview, Approved or Rejected")] string Status,
    [StringLength(500)] string? AdminNote = null);
