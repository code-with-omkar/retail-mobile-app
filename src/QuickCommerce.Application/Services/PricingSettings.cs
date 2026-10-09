namespace QuickCommerce.Application.Services;

/// <summary>
/// What the customer pays on top of the products. One set for the whole organisation for now; the app shows what the server returns
/// and never has its own copy. The defaults charge nothing, so only an environment that configures fees charges any.
/// </summary>
public sealed class PricingSettings
{
    public const string SectionName = "Pricing";

    /// <summary>Charged on an order below the free-delivery threshold.</summary>
    public decimal DeliveryFee { get; set; }

    public decimal HandlingFee { get; set; }

    /// <summary>A subtotal at or above this ships free. Zero means delivery is never free by threshold.</summary>
    public decimal FreeDeliveryThreshold { get; set; }

    /// <summary>Throws at startup when the configuration is unusable.</summary>
    public void Validate()
    {
        if (DeliveryFee is < 0 or > 10_000 || HandlingFee is < 0 or > 10_000 || FreeDeliveryThreshold is < 0 or > 1_000_000)
        {
            throw new InvalidOperationException("Pricing:DeliveryFee, HandlingFee and FreeDeliveryThreshold must be between zero and a sensible maximum.");
        }
    }
}

public sealed record PriceBreakdown(decimal Subtotal, decimal DeliveryFee, decimal HandlingFee, decimal Total, decimal FreeDeliveryThreshold, decimal AmountToFreeDelivery);

/// <summary>The only place fees are worked out, so the cart the customer sees and the order they place always agree.</summary>
public static class PricingCalculator
{
    public static PriceBreakdown Compute(PricingSettings settings, decimal subtotal)
    {
        if (subtotal <= 0)
        {
            return new PriceBreakdown(0, 0, 0, 0, settings.FreeDeliveryThreshold, 0);
        }

        var free = settings.FreeDeliveryThreshold > 0 && subtotal >= settings.FreeDeliveryThreshold;
        var delivery = free ? 0 : settings.DeliveryFee;
        var toFree = settings.FreeDeliveryThreshold > 0 && !free ? settings.FreeDeliveryThreshold - subtotal : 0;
        return new PriceBreakdown(subtotal, delivery, settings.HandlingFee, subtotal + delivery + settings.HandlingFee, settings.FreeDeliveryThreshold, toFree);
    }
}
