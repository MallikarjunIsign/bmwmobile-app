using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts;

public record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Phone number must be in international format, e.g. +919876543210")] string PhoneNumber,
    [Required] string Password,
    bool AcceptTerms,
    // Only used when Features:RolesEnabled is true (then it is required). Ignored otherwise.
    [RegularExpression("^(customer|pro)$", ErrorMessage = "Role must be 'customer' or 'pro'")] string? Role = null);

public record ResendOtpRequest([Required] string ChallengeId);

public record ForgotPasswordRequest([Required, EmailAddress] string Email);

public record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from the email")] string Otp,
    [Required] string NewPassword);

/// <summary>The signed-in user as the app sees it. Role is "user" while Features:RolesEnabled is off.</summary>
public record UserDto(
    string Id,
    string Username,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Role,
    bool EmailVerified,
    // False until the user has saved a location (the app then shows "Set your location" once).
    bool HasLocation);

public record OtpSentResponse(string Message, int ResendAfterSeconds, string? DevOtp = null);

public record MessageResponse(string Message);

public record ErrorResponse(string Message, string? Code = null, IDictionary<string, string[]>? Errors = null);
