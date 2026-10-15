namespace IdentityService.Options;

public class OtpOptions
{
    public int Length { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 10;

    /// <summary>How long a new seller's "set your password" code stays valid.</summary>
    public int InviteExpiryHours { get; set; } = 72;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 30;

    /// <summary>Include the code in API responses so it can be tested without email. Only honoured in Development.</summary>
    public bool ReturnOtpInResponse { get; set; }
}

public class TrustedDeviceOptions
{
    public int Days { get; set; } = 30;
}

public class FeatureOptions
{
    /// <summary>
    /// Customer / Service Pro accounts. Off = everyone is one plain "user".
    /// Turn on to bring back the role choice on sign-up and social sign-in.
    /// </summary>
    public bool RolesEnabled { get; set; }
}

/// <summary>
/// Accounts created at startup for local testing. Emails and passwords come only from user-secrets or
/// environment variables (Seed__AdminEmail, Seed__AdminPassword, ...); empty = not created. See RUN_LOCAL.md.
/// </summary>
public class SeedOptions
{
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminFullName { get; set; } = "ShopCarParts Admin";

    public string? SellerEmail { get; set; }
    public string? SellerPassword { get; set; }
    public string SellerFullName { get; set; } = "Test Seller";
    public string SellerCountryCode { get; set; } = "IN";
    public string? SellerCity { get; set; } = "Hyderabad";
    public string? SellerBusinessName { get; set; } = "Test Seller Parts";
    public string SellerPhoneNumber { get; set; } = "+919000000001";
}

/// <summary>Where existing users with listings are found when roles are first given out (RoleBackfillService).</summary>
public class RoleBackfillOptions
{
    /// <summary>AuctionService base URL; empty = skip the seller step (everyone stays a buyer).</summary>
    public string? AuctionServiceUrl { get; set; }
    public int MaxAttempts { get; set; } = 30;
    public int RetrySeconds { get; set; } = 10;
}

/// <summary>Saved locations. The radius rules (default 200, 5–1,000 km / mi) are Contracts.RegionRules – one setting for all services.</summary>
public class LocationOptions
{
    public int MaxLocationsPerUser { get; set; } = 10;

    /// <summary>GPS moves smaller than this don't change the primary location (avoids constant updates).</summary>
    public double MoveThresholdKm { get; set; } = 1;
}

public class SocialAuthOptions
{
    /// <summary>Google OAuth client IDs (Web, Android, iOS) whose ID tokens are accepted.</summary>
    public string[] GoogleClientIds { get; set; } = [];

    /// <summary>Apple audiences: the iOS bundle ID (and a Services ID if web sign-in is added).</summary>
    public string[] AppleAudiences { get; set; } = [];
}

/// <summary>
/// SMTP for OTP emails (Gmail works with an App Password).
/// Username and Password come only from user-secrets or environment variables (Email__Username, Email__Password).
/// When they are empty the code is written to the log instead.
/// </summary>
public class EmailOptions
{
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "ShopCarParts";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
