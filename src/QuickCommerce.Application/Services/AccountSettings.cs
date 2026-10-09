namespace QuickCommerce.Application.Services;

/// <summary>Configuration section "Account": password reset behaviour and the secret that keys the reset-code hash.</summary>
public sealed class AccountSettings
{
    public const string SectionName = "Account";

    /// <summary>Server secret for HMAC-hashing reset codes. Must come from configuration or a secret store, never source control.</summary>
    public string ResetCodeKey { get; set; } = "";

    public int CodeLifetimeMinutes { get; set; } = 30;
    public int MaxCodeAttempts { get; set; } = 5;
    public int MaxCodeRequestsPerHour { get; set; } = 3;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ResetCodeKey) || ResetCodeKey.Length < 32)
        {
            throw new InvalidOperationException("Account:ResetCodeKey must be configured and at least 32 characters long.");
        }

        if (CodeLifetimeMinutes is < 5 or > 240 || MaxCodeAttempts is < 1 or > 20 || MaxCodeRequestsPerHour is < 1 or > 20)
        {
            throw new InvalidOperationException("Account settings are out of range (CodeLifetimeMinutes 5-240, MaxCodeAttempts 1-20, MaxCodeRequestsPerHour 1-20).");
        }
    }
}

/// <summary>Configuration section "Registration".</summary>
public sealed class RegistrationSettings
{
    public const string SectionName = "Registration";

    /// <summary>Organization new customers join. When empty, the only active organization is used; several active ones is a configuration error.</summary>
    public Guid? OrganizationId { get; set; }
}

public enum EmailMode
{
    /// <summary>Development only: write the message to the log and a local outbox folder. Rejected outside Development.</summary>
    Log,
    Smtp
}

/// <summary>Configuration section "Email". The SMTP password must come from an environment variable or secret store.</summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public EmailMode Mode { get; set; } = EmailMode.Log;
    public string From { get; set; } = "no-reply@quickcart.invalid";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";

    public void ValidateForNonDevelopment()
    {
        if (Mode != EmailMode.Smtp)
        {
            throw new InvalidOperationException("Email:Mode must be Smtp outside Development (Log mode would write reset codes to logs).");
        }

        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(From))
        {
            throw new InvalidOperationException("Email:Host and Email:From must be configured for Smtp.");
        }
    }
}
