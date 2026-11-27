namespace QuickCommerce.Application.Services;

/// <summary>
/// The ribbon and animation a coupon or an offer can carry. The apps and the portal draw them from these keys; a new animation is added
/// here, in the apps and in the portal.
/// </summary>
public static class CardExtras
{
    public const int RibbonLength = 20;

    public static readonly string[] Animations = ["confetti", "sparkle", "gift", "balloons"];

    /// <summary>The stored form: lower case, and null for "no animation" (empty, or the word none).</summary>
    public static string? NormalizeAnimation(string? animation)
    {
        var value = animation?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) || value == "none" ? null : value;
    }

    public static bool IsKnownAnimation(string? normalized) => normalized is null || Animations.Contains(normalized);

    public static string? NormalizeRibbon(string? ribbon) => string.IsNullOrWhiteSpace(ribbon) ? null : ribbon.Trim();

    /// <summary>Why this ribbon and animation cannot be used; null when they can.</summary>
    public static string? Problem(string? ribbon, string? animation)
    {
        if (NormalizeRibbon(ribbon) is { } text && (text.Length > RibbonLength || text.Any(c => c is '<' or '>' || char.IsControl(c))))
        {
            return "The ribbon can be up to 20 characters and cannot contain < or >.";
        }

        return IsKnownAnimation(NormalizeAnimation(animation)) ? null : "That animation is not known.";
    }
}
