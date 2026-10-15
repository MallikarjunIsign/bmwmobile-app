using Contracts;
using Duende.IdentityServer.Services;
using IdentityService.Contracts;
using IdentityService.Data;
using IdentityService.Infrastructure;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

/// <summary>
/// Seller accounts are made only by an admin (no seller sign-up in the app). A new seller has no password:
/// they get an email with a one-time code, set the password on the app's "Got a seller invite?" screen
/// (the reset-password endpoint) and then log in normally with email, password and OTP.
/// </summary>
public class AdminSellerService(
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    AccountService accounts,
    IOtpService otp,
    ITrustedDeviceService devices,
    IPersistedGrantService grants,
    SellerApplicationService applications,
    ILogger<AdminSellerService> logger)
{
    public async Task<SellerInviteResponse> CreateSellerAsync(CreateSellerRequest req, CancellationToken ct)
    {
        var email = AccountService.NormalizeEmail(req.Email);
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
            throw AppException.Conflict(
                $"An account with this email already exists (id {existing.Id}). Use POST /api/admin/users/{existing.Id}/make-seller to upgrade it.",
                "EMAIL_TAKEN");

        var fullName = req.FullName.Trim();
        var user = await accounts.CreateUserAsync(fullName, email, u =>
        {
            u.PhoneNumber = req.PhoneNumber.Trim();
            u.CountryCode = req.CountryCode.Trim().ToUpperInvariant();
            u.City = Clean(req.City);
            u.BusinessName = Clean(req.BusinessName);
        }, password: null, ct);
        await accounts.AddRoleAsync(user, AppRoles.Seller);

        var code = await SendInviteAsync(user, ct);
        await applications.MarkApprovedAsync(user, ct);
        logger.LogInformation("Admin created seller {Username}", user.UserName);
        return new SellerInviteResponse(await ToDtoAsync(user), $"Seller created. We emailed {AccountService.MaskEmail(email)} a code to set their password.",
            accounts.DevCode(code));
    }

    public async Task<SellerInviteResponse> ResendInviteAsync(string id, CancellationToken ct)
    {
        var user = await FindSellerAsync(id);
        if (await users.HasPasswordAsync(user))
            throw AppException.Conflict("This seller has already set a password. They can use \"Forgot password?\" on the Login screen.", "ALREADY_ACTIVE");
        if (user.IsDisabled)
            throw AppException.BadRequest("This seller is disabled. Enable the account first.", "ACCOUNT_DISABLED");

        var code = await SendInviteAsync(user, ct);
        return new SellerInviteResponse(await ToDtoAsync(user), $"Invite sent again to {AccountService.MaskEmail(user.Email!)}.", accounts.DevCode(code));
    }

    public async Task<AdminUserDto> UpdateSellerAsync(string id, UpdateSellerRequest req, CancellationToken ct)
    {
        var user = await FindSellerAsync(id);

        if (req.FullName is { } name && name.Trim() != user.FullName)
        {
            user.FullName = name.Trim();
            // Older parts of the system read the display name from the "name" claim: keep it in step.
            var claims = await users.GetClaimsAsync(user);
            var old = claims.FirstOrDefault(c => c.Type == IdentityModel.JwtClaimTypes.Name);
            var updated = new System.Security.Claims.Claim(IdentityModel.JwtClaimTypes.Name, user.FullName);
            if (old is null) await users.AddClaimAsync(user, updated);
            else await users.ReplaceClaimAsync(user, old, updated);
        }
        if (req.PhoneNumber is not null) user.PhoneNumber = req.PhoneNumber.Trim();
        if (req.CountryCode is not null) user.CountryCode = req.CountryCode.Trim().ToUpperInvariant();
        if (req.City is not null) user.City = Clean(req.City);
        if (req.BusinessName is not null) user.BusinessName = Clean(req.BusinessName);

        var disabling = req.Disabled == true && !user.IsDisabled;
        if (req.Disabled is { } disabled) user.IsDisabled = disabled;

        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) throw AppException.BadRequest(result.Errors.First().Description, "VALIDATION_FAILED");

        if (disabling)
        {
            // Signed out everywhere: refresh tokens and remembered devices stop working.
            await devices.ForgetAllAsync(user.Id, ct);
            await grants.RemoveAllGrantsAsync(user.Id);
            logger.LogInformation("Admin disabled seller {Username}", user.UserName);
        }
        return await ToDtoAsync(user);
    }

    /// <summary>Sellers, newest first; search matches name, email, username or business name.</summary>
    public Task<PagedResponse<AdminUserDto>> ListSellersAsync(string? search, int page, int pageSize, CancellationToken ct) =>
        ListAsync(search, AppRoles.Seller, page, pageSize, ct);

    /// <summary>All accounts (any role), e.g. to find a buyer's id for make-seller.</summary>
    public Task<PagedResponse<AdminUserDto>> ListUsersAsync(string? search, int page, int pageSize, CancellationToken ct) =>
        ListAsync(search, role: null, page, pageSize, ct);

    /// <summary>Upgrades a buyer: the app picks up the seller role at its next token refresh (no reinstall).</summary>
    public async Task<AdminUserDto> MakeSellerAsync(string id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id) ?? throw AppException.NotFound("Account not found.", "USER_NOT_FOUND");
        var role = await accounts.GetRoleAsync(user);
        if (role == AppRoles.Admin)
            throw AppException.BadRequest("Admin accounts can't be sellers. Create a separate seller account.", "IS_ADMIN");

        if (role != AppRoles.Seller)
        {
            await accounts.AddRoleAsync(user, AppRoles.Seller);
            if (await users.IsInRoleAsync(user, AppRoles.Buyer)) await users.RemoveFromRoleAsync(user, AppRoles.Buyer);
            await applications.MarkApprovedAsync(user, ct);
            logger.LogInformation("Admin upgraded {Username} to seller", user.UserName);
        }
        return await ToDtoAsync(user);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<string?> SendInviteAsync(ApplicationUser user, CancellationToken ct)
    {
        var issued = await otp.IssueAsync(user, OtpPurpose.SellerInvite, reuseRecent: false, ct);
        user.InvitedAt = DateTime.UtcNow;
        await users.UpdateAsync(user);
        return issued.Code;
    }

    private async Task<ApplicationUser> FindSellerAsync(string id)
    {
        var user = await users.FindByIdAsync(id) ?? throw AppException.NotFound("Seller not found.", "USER_NOT_FOUND");
        if (await accounts.GetRoleAsync(user) != AppRoles.Seller)
            throw AppException.NotFound("This account isn't a seller. Use make-seller to upgrade it first.", "NOT_A_SELLER");
        return user;
    }

    private async Task<PagedResponse<AdminUserDto>> ListAsync(string? search, string? role, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Users.AsNoTracking();
        if (role is not null)
        {
            var normalized = role.ToUpperInvariant();
            var roleIds = db.Roles.Where(r => r.NormalizedName == normalized).Select(r => r.Id);
            query = query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().Replace("%", "").Replace("_", "")}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName ?? "", term) || EF.Functions.ILike(u.Email ?? "", term) ||
                EF.Functions.ILike(u.UserName ?? "", term) || EF.Functions.ILike(u.BusinessName ?? "", term));
        }

        var total = await query.CountAsync(ct);
        var page_ = await query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.UserName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = new List<AdminUserDto>(page_.Count);
        foreach (var user in page_) items.Add(await ToDtoAsync(user));
        return new PagedResponse<AdminUserDto>(items, total, page, pageSize);
    }

    private async Task<AdminUserDto> ToDtoAsync(ApplicationUser user) => new(
        user.Id,
        user.UserName ?? "",
        user.FullName ?? user.UserName ?? "",
        user.Email ?? "",
        user.PhoneNumber,
        await accounts.GetRoleAsync(user),
        user.CountryCode,
        user.City,
        user.BusinessName,
        user.IsDisabled,
        await users.HasPasswordAsync(user),
        user.InvitedAt,
        user.CreatedAt,
        user.LastLoginAt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
