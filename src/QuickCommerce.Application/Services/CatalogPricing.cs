namespace QuickCommerce.Application.Services;

/// <summary>Customer-facing MRP and discount rules.</summary>
public static class CatalogPricing
{
    /// <summary>The MRP to show: the stored MRP when it is above the price, otherwise the price (no discount).</summary>
    public static decimal EffectiveMrp(decimal price, decimal? mrp) => mrp is { } value && value > price ? value : price;

    /// <summary>Rounded discount percentage, 0 when MRP is not above the price.</summary>
    public static int DiscountPercent(decimal price, decimal? mrp)
    {
        var effective = EffectiveMrp(price, mrp);
        return effective > price ? (int)Math.Round((effective - price) / effective * 100m, MidpointRounding.AwayFromZero) : 0;
    }
}
