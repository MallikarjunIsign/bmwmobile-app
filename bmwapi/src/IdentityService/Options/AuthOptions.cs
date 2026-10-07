namespace IdentityService.Options;

public class OtpOptions
{
    public int Length { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 10;
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
    public string FromName { get; set; } = "OKOH Parts";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
