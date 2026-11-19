namespace QuickCommerce.Application.Services;

/// <summary>The price after an offer's percent off. The server is the only place that decides it.</summary>
public static class OfferPricing
{
    public const int MaxPercent = 90;

    /// <summary>The price less <paramref name="percent"/>, rounded to the paisa (half up). No discount returns the price unchanged.</summary>
    public static decimal Apply(decimal price, int percent) =>
        percent <= 0 ? price : Math.Round(price * (100 - Math.Min(percent, MaxPercent)) / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>The highest percent among the offers covering a product (offers never add up).</summary>
    public static int Best(IEnumerable<int> percents) => percents.DefaultIfEmpty(0).Max();
}
