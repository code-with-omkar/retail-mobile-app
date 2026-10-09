using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Infrastructure.Email;

/// <summary>
/// DEVELOPMENT ONLY. Writes the email, including any reset code, to the log and to a folder under the system temp directory,
/// so a code can be read without a mail server. The API refuses to start with this sender outside Development.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public static string OutboxFolder => Path.Combine(Path.GetTempPath(), "quickcart-dev-outbox");

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(OutboxFolder);
        var safeName = string.Concat(message.To.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(OutboxFolder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safeName}.txt");
        await File.WriteAllTextAsync(path, $"To: {message.To}\nSubject: {message.Subject}\n\n{message.Body}\n", cancellationToken);
        logger.LogInformation("DEV EMAIL to {To} | {Subject}\n{Body}\n(saved to {Path})", message.To, message.Subject, message.Body, path);
    }
}
