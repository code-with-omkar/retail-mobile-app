namespace QuickCommerce.Application.Services;

/// <summary>The price chain: the shared price, then the store's own price (set by its manager), then an offer's percent off.</summary>
public static class StorePricing
{
    /// <summary>What the customer pays for one unit at this store.</summary>
    public static decimal Final(decimal basePrice, decimal? storePrice, int offerPercent) => OfferPricing.Apply(storePrice ?? basePrice, offerPercent);

    /// <summary>
    /// The MRP to keep on an order line: the original figure (the MRP, or the shared price) when the customer paid less than it, so the receipt
    /// shows the saving; otherwise the variant's own MRP.
    /// </summary>
    public static decimal? MrpSnapshot(decimal unitPrice, decimal basePrice, decimal? mrp)
    {
        var original = CatalogPricing.EffectiveMrp(basePrice, mrp);
        return unitPrice < original ? original : mrp;
    }
}
