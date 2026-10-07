using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdentityService.Services;

/// <summary>Delivers OTP codes to the user.</summary>
public interface IOtpSender
{
    Task SendAsync(ApplicationUser user, string code, OtpPurpose purpose, CancellationToken ct = default);
}

/// <summary>
/// Emails the code through SMTP (Gmail for local testing).
/// Without Email:Username / Email:Password it only writes the code to the log.
/// </summary>
public class EmailOtpSender(
    IOptions<EmailOptions> options,
    IHostEnvironment env,
    ILogger<EmailOtpSender> logger) : IOtpSender
{
    private readonly EmailOptions _opt = options.Value;

    public async Task SendAsync(ApplicationUser user, string code, OtpPurpose purpose, CancellationToken ct = default)
    {
        var email = user.Email ?? throw new InvalidOperationException("User has no email");

        if (!_opt.IsConfigured)
        {
            logger.LogWarning("Email is not configured - DEV OTP for {Email} ({Purpose}): {Code}", email, purpose, code);
            return;
        }

        var message = new MimeMessage();
        var from = string.IsNullOrWhiteSpace(_opt.FromAddress) ? _opt.Username! : _opt.FromAddress;
        message.From.Add(new MailboxAddress(_opt.FromName, from));
        message.To.Add(new MailboxAddress(user.FullName ?? user.UserName ?? email, email));
        message.Subject = purpose == OtpPurpose.ResetPassword
            ? $"{code} is your OKOH Parts password reset code"
            : $"{code} is your OKOH Parts sign-in code";

        var action = purpose == OtpPurpose.ResetPassword ? "reset your password" : "sign in";
        var builder = new BodyBuilder
        {
            TextBody = $"Your code to {action} is {code}.\n\nIt expires in 10 minutes. If you didn't ask for it, you can ignore this email.",
            HtmlBody = $"""
                <div style="font-family:Arial,sans-serif;background:#07071C;color:#ffffff;padding:24px;border-radius:12px">
                  <p style="color:#B226FF;font-weight:800;letter-spacing:2px;margin:0">OKOH PARTS</p>
                  <p>Your code to {action} is</p>
                  <p style="font-size:32px;font-weight:900;letter-spacing:8px;color:#B226FF;margin:8px 0">{code}</p>
                  <p style="color:#94A3B8">It expires in 10 minutes. If you didn't ask for it, you can ignore this email.</p>
                </div>
                """,
        };
        message.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opt.SmtpHost, _opt.SmtpPort, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_opt.Username!, _opt.Password!, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            logger.LogInformation("Sent {Purpose} code to {Email}", purpose, email);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send {Purpose} code to {Email}", purpose, email);
            if (env.IsDevelopment())
            {
                logger.LogWarning("DEV OTP for {Email} ({Purpose}): {Code}", email, purpose, code);
                return;
            }
            throw new AppException(StatusCodes.Status503ServiceUnavailable,
                "We couldn't send the email right now. Please try again in a minute.", "EMAIL_FAILED");
        }
    }
}
