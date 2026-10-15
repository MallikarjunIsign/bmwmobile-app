using System.Net;
using Contracts;
using IdentityService.Contracts;
using IdentityService.Data;
using IdentityService.Infrastructure;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public class SellerApplicationOptions
{
    /// <summary>Extra addresses told about new applications (besides every admin account).</summary>
    public List<string> NotifyEmails { get; set; } = [];
}

/// <summary>
/// "Want to sell?" requests: the app's form stores them here and emails the admins. An admin reviews them
/// (GET / PATCH /api/admin/seller-applications) and creates the seller with the existing admin API;
/// creating the seller (or make-seller) marks the open request Approved.
/// </summary>
public class SellerApplicationService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    AccountService accounts,
    IMailSender mail,
    Microsoft.Extensions.Options.IOptions<SellerApplicationOptions> options,
    ILogger<SellerApplicationService> logger)
{
    private static readonly SellerApplicationStatus[] Open = [SellerApplicationStatus.New, SellerApplicationStatus.InReview];

    public async Task<MySellerApplicationDto> SubmitAsync(SellerApplicationRequest req, string? userId, CancellationToken ct)
    {
        var email = AccountService.NormalizeEmail(req.Email);

        var account = userId is not null ? await users.FindByIdAsync(userId) : await users.FindByEmailAsync(email);
        if (account is not null && await accounts.GetRoleAsync(account) is AppRoles.Seller or AppRoles.Admin)
            throw AppException.Conflict("This account can already sell. Log in and use the Sell tab.", "ALREADY_SELLER");

        var pending = await db.SellerApplications
            .Where(a => Open.Contains(a.Status) && (a.Email == email || (userId != null && a.UserId == userId)))
            .FirstOrDefaultAsync(ct);
        if (pending is not null)
            throw AppException.Conflict(
                $"We already have your request from {pending.CreatedAt:d MMM yyyy}. Our team will contact you soon.", "APPLICATION_PENDING");

        var application = new SellerApplication
        {
            FullName = req.FullName.Trim(),
            Email = email,
            PhoneNumber = req.PhoneNumber.Trim(),
            CountryCode = req.CountryCode.Trim().ToUpperInvariant(),
            City = req.City.Trim(),
            BusinessName = Clean(req.BusinessName),
            BusinessType = req.BusinessType,
            Sells = req.Sells,
            BusinessAddress = Clean(req.BusinessAddress),
            TaxId = Clean(req.TaxId),
            Message = Clean(req.Message),
            UserId = userId,
        };
        db.SellerApplications.Add(application);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("New seller application {Id} ({Country}, {Type})", application.Id, application.CountryCode, application.BusinessType);

        await NotifyAdminsAsync(application, ct);
        return ToMine(application);
    }

    /// <summary>The signed-in user's latest request (by account or email), or null.</summary>
    public async Task<MySellerApplicationDto?> MineAsync(string userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId);
        var email = user?.Email is { } e ? AccountService.NormalizeEmail(e) : null;
        var latest = await db.SellerApplications.AsNoTracking()
            .Where(a => a.UserId == userId || (email != null && a.Email == email))
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        return latest is null ? null : ToMine(latest);
    }

    // ── admin ─────────────────────────────────────────────────────────────

    public async Task<PagedResponse<SellerApplicationDto>> ListAsync(string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.SellerApplications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SellerApplicationStatus>(status, ignoreCase: true, out var s))
                throw AppException.BadRequest("Status must be New, InReview, Approved or Rejected.", "VALIDATION_FAILED");
            query = query.Where(a => a.Status == s);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().Replace("%", "").Replace("_", "")}%";
            query = query.Where(a => EF.Functions.ILike(a.FullName, term) || EF.Functions.ILike(a.Email, term) ||
                                     EF.Functions.ILike(a.BusinessName ?? "", term) || EF.Functions.ILike(a.City, term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<SellerApplicationDto>(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<SellerApplicationDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync(id, ct));

    public async Task<SellerApplicationDto> ReviewAsync(Guid id, ReviewSellerApplicationRequest req, string? admin, CancellationToken ct)
    {
        var application = await FindAsync(id, ct);
        application.Status = Enum.Parse<SellerApplicationStatus>(req.Status);
        application.AdminNote = Clean(req.AdminNote) ?? application.AdminNote;
        application.ReviewedAt = DateTime.UtcNow;
        application.ReviewedBy = admin;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seller application {Id} is now {Status} ({Admin})", id, application.Status, admin);
        return ToDto(application);
    }

    /// <summary>The admin created this person's seller account: their open request is done.</summary>
    public async Task MarkApprovedAsync(ApplicationUser seller, CancellationToken ct)
    {
        var email = AccountService.NormalizeEmail(seller.Email ?? "");
        var open = await db.SellerApplications
            .Where(a => Open.Contains(a.Status) && (a.Email == email || a.UserId == seller.Id)).ToListAsync(ct);
        foreach (var a in open)
        {
            a.Status = SellerApplicationStatus.Approved;
            a.ReviewedAt = DateTime.UtcNow;
            a.SellerUserId = seller.Id;
            a.AdminNote ??= "Seller account created.";
        }
        if (open.Count > 0) await db.SaveChangesAsync(ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private async Task NotifyAdminsAsync(SellerApplication a, CancellationToken ct)
    {
        var admins = (await users.GetUsersInRoleAsync(AppRoles.Admin))
            .Where(u => !u.IsDisabled && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => new MailRecipient(u.FullName ?? u.UserName ?? u.Email!, u.Email!));
        var extra = options.Value.NotifyEmails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => new MailRecipient(e, e.Trim()));
        var to = admins.Concat(extra).DistinctBy(r => r.Email.ToLowerInvariant()).ToList();

        var who = a.BusinessName is null ? a.FullName : $"{a.FullName} ({a.BusinessName})";
        var subject = $"New seller application: {who}, {a.City} {a.CountryCode}";
        var rows = new (string Label, string? Value)[]
        {
            ("Name", a.FullName), ("Email", a.Email), ("Phone", a.PhoneNumber), ("City", $"{a.City}, {a.CountryCode}"),
            ("Business name", a.BusinessName), ("Business type", a.BusinessType), ("Sells", a.Sells),
            ("Business address", a.BusinessAddress), ("Tax / GST number", a.TaxId), ("Message", a.Message),
            ("Existing account", a.UserId is null ? "no (new person)" : $"yes - user id {a.UserId}"),
        };
        var next = a.UserId is null
            ? $"To create the seller: POST /api/admin/sellers with this name, email, phone, country, city and business name."
            : $"To make this buyer a seller: POST /api/admin/users/{a.UserId}/make-seller.";
        var text = string.Join("\n", rows.Where(r => r.Value is not null).Select(r => $"{r.Label}: {r.Value}")) +
                   $"\n\nApplication id: {a.Id}\n{next}\nList: GET /api/admin/seller-applications?status=New";
        var html = $"""
            <div style="font-family:Arial,sans-serif">
              <p style="color:#B226FF;font-weight:800;letter-spacing:2px;margin:0">SHOPCARPARTS</p>
              <h3>New seller application</h3>
              <table cellpadding="4">{string.Concat(rows.Where(r => r.Value is not null).Select(r =>
                  $"<tr><td><b>{WebUtility.HtmlEncode(r.Label)}</b></td><td>{WebUtility.HtmlEncode(r.Value)}</td></tr>"))}</table>
              <p>Application id: {a.Id}<br/>{WebUtility.HtmlEncode(next)}</p>
            </div>
            """;
        await mail.SendAsync(to, subject, text, html, ct);
    }

    private async Task<SellerApplication> FindAsync(Guid id, CancellationToken ct) =>
        await db.SellerApplications.FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw AppException.NotFound("Seller application not found.", "APPLICATION_NOT_FOUND");

    private static MySellerApplicationDto ToMine(SellerApplication a) =>
        new(a.Id, a.CreatedAt, a.Status.ToString(), a.BusinessName, a.Status == SellerApplicationStatus.Rejected ? a.AdminNote : null);

    private static SellerApplicationDto ToDto(SellerApplication a) => new(
        a.Id, a.FullName, a.Email, a.PhoneNumber, a.CountryCode, a.City, a.BusinessName, a.BusinessType, a.Sells,
        a.BusinessAddress, a.TaxId, a.Message, a.UserId, a.CreatedAt, a.Status.ToString(), a.ReviewedAt, a.ReviewedBy,
        a.AdminNote, a.SellerUserId);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
