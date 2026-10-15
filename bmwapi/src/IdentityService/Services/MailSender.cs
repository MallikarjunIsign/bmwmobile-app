using IdentityService.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdentityService.Services;

public record MailRecipient(string Name, string Email);

/// <summary>Plain notification emails (e.g. "New seller application" to the admins).</summary>
public interface IMailSender
{
    /// <summary>Never throws: a notice that can't be sent is logged, the request that caused it still succeeds.</summary>
    Task SendAsync(IReadOnlyList<MailRecipient> to, string subject, string text, string html, CancellationToken ct = default);
}

/// <summary>SMTP (the same Email settings as the OTP codes). Without Email:Username / Password it only logs the notice.</summary>
public class SmtpMailSender(IOptions<EmailOptions> options, ILogger<SmtpMailSender> logger) : IMailSender
{
    private readonly EmailOptions _opt = options.Value;

    public async Task SendAsync(IReadOnlyList<MailRecipient> to, string subject, string text, string html, CancellationToken ct = default)
    {
        if (to.Count == 0)
        {
            logger.LogWarning("No recipients for \"{Subject}\" - set an admin account (Seed:AdminEmail) or SellerApplications:NotifyEmails", subject);
            return;
        }
        if (!_opt.IsConfigured)
        {
            logger.LogWarning("Email is not configured - notice for {To}: {Subject}\n{Text}", string.Join(", ", to.Select(r => r.Email)), subject, text);
            return;
        }

        var message = new MimeMessage();
        var from = string.IsNullOrWhiteSpace(_opt.FromAddress) ? _opt.Username! : _opt.FromAddress;
        message.From.Add(new MailboxAddress(_opt.FromName, from));
        foreach (var r in to) message.To.Add(new MailboxAddress(r.Name, r.Email));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opt.SmtpHost, _opt.SmtpPort, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_opt.Username!, _opt.Password!, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            logger.LogInformation("Sent \"{Subject}\" to {Count} recipient(s)", subject, to.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send \"{Subject}\"\n{Text}", subject, text);
        }
    }
}
