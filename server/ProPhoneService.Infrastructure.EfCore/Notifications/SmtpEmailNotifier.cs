using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Отправка писем через SMTP провайдера (Resend)
/// </summary>
public class SmtpEmailNotifier(EmailOptions emailOptions, ILogger<SmtpEmailNotifier> logger) : INotifier
{
    private const int TimeoutMilliseconds = 15_000;

    /// <inheritdoc />
    public async Task SendAsync(
        string target,
        string subject,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!emailOptions.IsConfigured)
        {
            throw new InvalidOperationException(
                "Email-провайдер не настроен: задайте Email__Host, Email__Password и Email__FromAddress");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(emailOptions.FromName, emailOptions.FromAddress));
        message.To.Add(MailboxAddress.Parse(target));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };

        using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
        var socketOptions = !emailOptions.UseTls
            ? SecureSocketOptions.None
            : emailOptions.UseImplicitTls
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

        await client.ConnectAsync(emailOptions.Host!, emailOptions.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(emailOptions.User))
        {
            await client.AuthenticateAsync(emailOptions.User, emailOptions.Password!, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Письмо отправлено: {Target} [{Subject}]", target, subject);
    }
}
