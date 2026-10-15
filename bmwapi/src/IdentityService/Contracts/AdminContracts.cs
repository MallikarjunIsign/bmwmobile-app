using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts;

/// <summary>An admin creates a seller account. The seller gets an email with a one-time code to set their password.</summary>
public record CreateSellerRequest(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Phone number must be in international format, e.g. +919876543210")] string PhoneNumber,
    [Required, RegularExpression("^(?i)(IN|US|NG|IE)$", ErrorMessage = "Country must be IN, US, NG or IE")] string CountryCode,
    [StringLength(100)] string? BusinessName = null,
    [StringLength(100)] string? City = null);

/// <summary>Only the fields sent are changed. Disabled = true blocks sign-in and signs the seller out everywhere.</summary>
public record UpdateSellerRequest(
    [StringLength(100, MinimumLength = 2)] string? FullName = null,
    [RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Phone number must be in international format, e.g. +919876543210")] string? PhoneNumber = null,
    [RegularExpression("^(?i)(IN|US|NG|IE)$", ErrorMessage = "Country must be IN, US, NG or IE")] string? CountryCode = null,
    [StringLength(100)] string? BusinessName = null,
    [StringLength(100)] string? City = null,
    bool? Disabled = null);

public record AdminUserDto(
    string Id,
    string Username,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Role,
    string? CountryCode,
    string? City,
    string? BusinessName,
    bool Disabled,
    // False until an invited seller has set their password (or for Google / Apple-only accounts).
    bool HasPassword,
    DateTime? InvitedAt,
    DateTime? CreatedAt,
    DateTime? LastLoginAt);

/// <summary>DevInviteCode is only filled in Development with Otp:ReturnOtpInResponse (it's also in the log).</summary>
public record SellerInviteResponse(AdminUserDto Seller, string Message, string? DevInviteCode = null);

public record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
