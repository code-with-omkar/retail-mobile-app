namespace QuickCommerce.Application.Services;

/// <summary>
/// Online payment settings (configuration section "Payments"). The secrets live only in server configuration (environment variables or
/// user secrets): never in source, never in the app, never in logs.
/// </summary>
public sealed class PaymentSettings
{
    public const string SectionName = "Payments";

    /// <summary>Off by default. While off, the app is told there is no online payment and an order with <c>Online</c> is refused.</summary>
    public bool Enabled { get; set; }

    /// <summary>The public id of the account. Not a secret: the app needs it to open the payment screen.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Signs requests to the provider and checks the payment result the app sends. Secret.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>Checks that a notification really comes from the provider. Secret. Optional: while it is empty every notification is refused, and payments are settled by the app's confirmation and the background lookup instead.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>How long the items of an unpaid order are held.</summary>
    public int HoldMinutes { get; set; } = 15;

    /// <summary>A payment still unsettled this long after it was started is looked up with the provider.</summary>
    public int ReconcileAfterMinutes { get; set; } = 20;

    /// <summary>How often the background work (release holds, start refunds, look up unsettled payments) runs.</summary>
    public int JobIntervalSeconds { get; set; } = 60;

    /// <summary>Throws at startup when the configuration is unusable.</summary>
    public void Validate()
    {
        if (HoldMinutes is < 5 or > 60)
        {
            throw new InvalidOperationException("Payments:HoldMinutes must be between 5 and 60.");
        }

        if (ReconcileAfterMinutes is < 1 or > 1440 || JobIntervalSeconds is < 10 or > 3600)
        {
            throw new InvalidOperationException("Payments:ReconcileAfterMinutes and Payments:JobIntervalSeconds are out of range.");
        }

        if (!Enabled)
        {
            return;
        }

        if (!KeyId.StartsWith("rzp_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payments:KeyId must be the account's Key Id (it starts with rzp_) when payments are enabled.");
        }

        if (KeySecret.Length < 16 || (WebhookSecret.Length > 0 && WebhookSecret.Length < 16))
        {
            throw new InvalidOperationException("Payments:KeySecret must be set (at least 16 characters) when payments are enabled, and Payments:WebhookSecret must be empty or at least 16 characters. Put them in environment variables or user secrets.");
        }
    }
}
