namespace QuickCommerce.Application.Services;

/// <summary>
/// The looks an administrator can give an offer's card: eight festival designs (colours, bilingual name, motif and button text are drawn by the
/// apps and the portal from the key). No design means the plain banner. The keys are the same in the customer app and the portal.
/// </summary>
public static class OfferCardDesigns
{
    public static readonly string[] All = ["diwali", "ganesh", "navratri", "holi", "sankranti", "padwa", "eid", "christmas"];

    /// <summary>The stored form: lower case, and null for "no design" (empty, or the word plain).</summary>
    public static string? Normalize(string? design)
    {
        var value = design?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) || value == "plain" ? null : value;
    }

    /// <summary>Whether a normalized design is allowed: none, or one of the eight.</summary>
    public static bool IsKnown(string? normalized) => normalized is null || All.Contains(normalized);
}
