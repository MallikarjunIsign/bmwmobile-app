using Duende.IdentityServer.Services;
using IdentityModel;
using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Options;
using Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>
/// Mobile account features: sign-up, email codes, forgot / reset password and Google / Apple accounts.
/// Tokens are issued by IdentityServer itself (see the Grants folder), so every other service keeps trusting them.
/// </summary>
public class AccountService(
    UserManager<ApplicationUser> users,
    IOtpService otp,
    ITrustedDeviceService devices,
    ILoginChallenges challenges,
    IPersistedGrantService grants,
    LocationService locations,
    IOptions<OtpOptions> otpOptions,
    IOptions<FeatureOptions> features,
    IHostEnvironment env,
    ILogger<AccountService> logger)
{
    public const string RoleCustomer = "customer";
    public const string RolePro = "pro";
    private const string GenericResetMessage = "If an account exists for this email, we've sent a code to reset the password.";

    public bool RolesEnabled => features.Value.RolesEnabled;

    public async Task<UserDto> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        if (!req.AcceptTerms)
            throw AppException.BadRequest("Please accept the Terms and Privacy Policy.", "TERMS_REQUIRED");

        var passwordError = PasswordRules.Validate(req.Password);
        if (passwordError is not null) throw AppException.BadRequest(passwordError, "WEAK_PASSWORD");

        var role = RolesEnabled
            ? ParseRole(req.Role) ?? throw AppException.BadRequest("Role must be 'customer' or 'pro'.", "VALIDATION_FAILED")
            : null;

        var email = NormalizeEmail(req.Email);
        if (await users.FindByEmailAsync(email) is not null)
            throw AppException.Conflict("An account with this email already exists. Please log in.", "EMAIL_TAKEN");

        var fullName = req.FullName.Trim();
        var user = await CreateUserAsync(fullName, email, user =>
        {
            user.PhoneNumber = req.PhoneNumber.Trim();
            user.TermsAcceptedAt = DateTime.UtcNow;
        }, req.Password, ct);

        if (role is not null) await AddRoleAsync(user, role);
        // Everyone who signs up in the app is a buyer. Seller accounts are made by an admin (AdminSellersController).
        await AddRoleAsync(user, AppRoles.Buyer);
        logger.LogInformation("Registered {Username} from the mobile app", user.UserName);
        return await ToDtoAsync(user);
    }

    /// <summary>Email + password is correct but this device isn't trusted: email a code and return the challenge.</summary>
    public async Task<LoginChallenge> StartOtpLoginAsync(ApplicationUser user, CancellationToken ct)
    {
        var issued = await otp.IssueAsync(user, OtpPurpose.Login, reuseRecent: true, ct);
        return new LoginChallenge(challenges.Create(user.Id), MaskEmail(user.Email!), issued.ResendAfterSeconds, DevCode(issued.Code));
    }

    public async Task<OtpSentResponse> ResendLoginOtpAsync(string challengeId, CancellationToken ct)
    {
        var user = await UserFromChallengeAsync(challengeId);
        var issued = await otp.IssueAsync(user, OtpPurpose.Login, reuseRecent: false, ct);
        return new OtpSentResponse("New code sent.", issued.ResendAfterSeconds, DevCode(issued.Code));
    }

    public async Task<ApplicationUser> UserFromChallengeAsync(string? challengeId)
    {
        var userId = challenges.Read(challengeId);
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        return user ?? throw AppException.BadRequest("This login session has expired. Please log in again.", "SESSION_EXPIRED");
    }

    public async Task<OtpSentResponse> ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(NormalizeEmail(req.Email));

        // Same answer whether or not the account exists, so emails can't be discovered.
        if (user is null) return new OtpSentResponse(GenericResetMessage, otp.ResendCooldownSeconds);

        var issued = await otp.IssueAsync(user, OtpPurpose.ResetPassword, reuseRecent: false, ct);
        return new OtpSentResponse(GenericResetMessage, issued.ResendAfterSeconds, DevCode(issued.Code));
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var passwordError = PasswordRules.Validate(req.NewPassword);
        if (passwordError is not null) throw AppException.BadRequest(passwordError, "WEAK_PASSWORD");

        var user = await users.FindByEmailAsync(NormalizeEmail(req.Email))
                   ?? throw AppException.BadRequest("This code has expired. Tap Resend to get a new one.", "OTP_EXPIRED");

        // The same screen sets a new seller's first password with the code from their invite email.
        var purpose = await otp.NewestActiveAsync(user, [OtpPurpose.ResetPassword, OtpPurpose.SellerInvite], ct)
                      ?? OtpPurpose.ResetPassword;
        await otp.VerifyAsync(user, purpose, req.Otp, ct);
        EnsureActive(user);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
            throw AppException.BadRequest(result.Errors.First().Description, "WEAK_PASSWORD");

        user.EmailConfirmed = true;
        await users.UpdateAsync(user);

        // A password reset signs the user out everywhere: remembered devices and refresh tokens.
        await devices.ForgetAllAsync(user.Id, ct);
        await grants.RemoveAllGrantsAsync(user.Id);
    }

    /// <summary>Finds the account for a Google / Apple sign-in, links it by verified email, or creates it.</summary>
    public async Task<ApplicationUser> FindOrCreateExternalUserAsync(SocialIdentity identity, string? fullName, string? role, CancellationToken ct)
    {
        var user = await users.FindByLoginAsync(identity.Provider, identity.SubjectId);
        if (user is not null) return EnsureActive(user);

        var loginInfo = new UserLoginInfo(identity.Provider, identity.SubjectId, identity.Provider == "google" ? "Google" : "Apple");

        // Link to an existing email account only when the provider has verified the email.
        if (identity.Email is not null && identity.EmailVerified)
        {
            user = await users.FindByEmailAsync(identity.Email);
            if (user is not null)
            {
                EnsureActive(user);
                Check(await users.AddLoginAsync(user, loginInfo));
                if (!user.EmailConfirmed)
                {
                    user.EmailConfirmed = true;
                    await users.UpdateAsync(user);
                }
                logger.LogInformation("Linked {Provider} sign-in to {Username}", identity.Provider, user.UserName);
                return user;
            }
        }

        string? newRole = null;
        if (RolesEnabled)
        {
            newRole = ParseRole(role)
                      ?? throw AppException.BadRequest("Choose whether you're a customer or a service pro to continue.", "ROLE_REQUIRED");
        }

        if (identity.Email is null)
            throw AppException.BadRequest("We couldn't get your email from the sign-in provider. Please sign up with email instead.", "EMAIL_MISSING");

        var name = FirstNonEmpty(fullName, identity.Name, identity.Email.Split('@')[0]);
        user = await CreateUserAsync(name, identity.Email, u => u.EmailConfirmed = identity.EmailVerified, password: null, ct);
        Check(await users.AddLoginAsync(user, loginInfo));
        if (newRole is not null) await AddRoleAsync(user, newRole);
        await AddRoleAsync(user, AppRoles.Buyer);
        logger.LogInformation("Created {Provider} account {Username}", identity.Provider, user.UserName);
        return user;
    }

    public async Task MarkSignedInAsync(ApplicationUser user, bool emailVerified)
    {
        user.LastLoginAt = DateTime.UtcNow;
        if (emailVerified) user.EmailConfirmed = true;
        await users.UpdateAsync(user);
    }

    public async Task<UserDto> GetUserAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId) ?? throw AppException.NotFound("Account not found.", "USER_NOT_FOUND");
        return await ToDtoAsync(user);
    }

    public async Task<UserDto> ToDtoAsync(ApplicationUser user)
    {
        var fullName = user.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            var claims = await users.GetClaimsAsync(user);
            fullName = claims.FirstOrDefault(c => c.Type == JwtClaimTypes.Name)?.Value ?? user.UserName ?? string.Empty;
        }

        return new UserDto(
            user.Id,
            user.UserName ?? string.Empty,
            fullName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            await GetRoleAsync(user),
            user.EmailConfirmed,
            await locations.HasAnyAsync(user.Id, CancellationToken.None));
    }

    /// <summary>
    /// The marketplace role in the token and /me: "admin", "seller" or "buyer" (highest wins; none = buyer).
    /// The old Customer / Pro roles (Features:RolesEnabled) are separate and not used for this.
    /// </summary>
    public async Task<string> GetRoleAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return AppRoles.All.FirstOrDefault(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)) ?? AppRoles.Buyer;
    }

    public const string DisabledMessage = "This account has been disabled. Please contact support.";

    /// <summary>Throws when an admin has disabled the account.</summary>
    public static ApplicationUser EnsureActive(ApplicationUser user) =>
        user.IsDisabled ? throw new AppException(StatusCodes.Status403Forbidden, DisabledMessage, "ACCOUNT_DISABLED") : user;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // ── helpers ────────────────────────────────────────────────────────────

    internal async Task<ApplicationUser> CreateUserAsync(
        string fullName, string email, Action<ApplicationUser> configure, string? password, CancellationToken ct)
    {
        // Two people with the same first name signing up at once can pick the same username; retry with the next one.
        for (var attempt = 0; ; attempt++)
        {
            var user = new ApplicationUser
            {
                UserName = await UsernameGenerator.NextAvailableAsync(users, fullName, ct),
                Email = email,
                FullName = fullName,
                CreatedAt = DateTime.UtcNow,
            };
            configure(user);

            var result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
            if (result.Succeeded)
            {
                // Older parts of the system (web app, profile service) read the display name from the "name" claim.
                Check(await users.AddClaimAsync(user, new System.Security.Claims.Claim(JwtClaimTypes.Name, fullName)));
                return user;
            }

            var duplicateName = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
            if (!duplicateName || attempt >= 4)
                throw AppException.BadRequest(result.Errors.First().Description, "VALIDATION_FAILED");
        }
    }

    internal async Task AddRoleAsync(ApplicationUser user, string role) => Check(await users.AddToRoleAsync(user, role));

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw AppException.BadRequest(result.Errors.First().Description, "VALIDATION_FAILED");
    }

    internal string? DevCode(string? code) =>
        env.IsDevelopment() && otpOptions.Value.ReturnOtpInResponse ? code : null;

    private static string? ParseRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        RoleCustomer => RoleCustomer,
        RolePro => RolePro,
        _ => null,
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(v => !string.IsNullOrWhiteSpace(v))!.Trim();

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        return $"{email[0]}{new string('*', Math.Min(at - 1, 5))}{email[(at - 1)..]}";
    }
}

public record LoginChallenge(string ChallengeId, string MaskedEmail, int ResendAfterSeconds, string? DevOtp);
