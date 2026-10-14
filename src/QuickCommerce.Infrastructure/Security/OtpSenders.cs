using System.Net.Http.Json;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Infrastructure.Security;

/// <summary>Development only: the code goes to the API log so the whole sign-in can be tried without an SMS account. Never registered outside Development.</summary>
public sealed class LogOtpSender(ILogger<LogOtpSender> log) : IOtpSender
{
    public Task SendAsync(string phone, string code, CancellationToken cancellationToken = default)
    {
        log.LogWarning("DEVELOPMENT sign-in code for {Phone}: {Code}", phone, code);
        return Task.CompletedTask;
    }
}

/// <summary>Configuration section "Otp:Msg91". The auth key must come from an environment variable or a secret store.</summary>
public sealed class Msg91Settings
{
    public const string SectionName = "Otp:Msg91";
    public string AuthKey { get; set; } = "";

    /// <summary>The DLT-approved template that carries the code (MSG91 fills ##OTP##).</summary>
    public string TemplateId { get; set; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AuthKey) || string.IsNullOrWhiteSpace(TemplateId))
        {
            throw new InvalidOperationException("Otp:Msg91:AuthKey and Otp:Msg91:TemplateId must be configured when Otp:Sender is Msg91.");
        }
    }
}

/// <summary>
/// Sends the code through MSG91's OTP API. Written to MSG91's published API but not run against a live account yet.
/// The number and code are never logged; a failure logs only the status.
/// </summary>
public sealed class Msg91OtpSender(HttpClient http, Msg91Settings settings, OtpSettings otp, ILogger<Msg91OtpSender> log) : IOtpSender
{
    public async Task SendAsync(string phone, string code, CancellationToken cancellationToken = default)
    {
        var url = $"https://control.msg91.com/api/v5/otp?template_id={Uri.EscapeDataString(settings.TemplateId)}&mobile={Uri.EscapeDataString(phone.TrimStart('+'))}&otp={Uri.EscapeDataString(code)}&otp_expiry={otp.LifetimeMinutes}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("authkey", settings.AuthKey);
        request.Content = JsonContent.Create(new { });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            log.LogError("MSG91 refused a sign-in code with status {Status}", (int)response.StatusCode);
            throw new InvalidOperationException("The text message could not be sent.");
        }
    }
}

/// <summary>Checks Google ID tokens with Google's own library: signature against Google's keys, expiry, issuer, and that the audience is one of our client ids.</summary>
public sealed class GoogleTokenValidator(GoogleSettings settings, ILogger<GoogleTokenValidator> log) : IGoogleTokenValidator
{
    public bool IsConfigured => settings.ClientIds.Length > 0;

    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = settings.ClientIds });
            return string.IsNullOrWhiteSpace(payload.Subject) ? null : new GoogleIdentity(payload.Subject, payload.Email ?? string.Empty, payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Google's keys could not be fetched: treat as a failed sign-in, and leave a trace for whoever runs the server.
            log.LogWarning(exception, "Google's signing keys could not be fetched");
            return null;
        }
    }
}
