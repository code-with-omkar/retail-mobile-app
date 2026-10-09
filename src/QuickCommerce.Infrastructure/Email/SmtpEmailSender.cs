using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Infrastructure.Email;

/// <summary>Sends mail through an SMTP server from configuration. Never logs the message body or the full recipient address.</summary>
public sealed class SmtpEmailSender(EmailSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.UseSsl,
                Credentials = string.IsNullOrEmpty(settings.UserName) ? null : new NetworkCredential(settings.UserName, settings.Password)
            };
            using var mail = new MailMessage(settings.From, message.To, message.Subject, message.Body)
            {
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (Exception exception)
        {
            var domain = message.To[(message.To.IndexOf('@') + 1)..];
            logger.LogError(exception, "Sending an account email failed (recipient domain {Domain}, subject {Subject})", domain, message.Subject);
            throw;
        }
    }
}
