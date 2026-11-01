using System.Text.RegularExpressions;

namespace QuickCommerce.Application.Services;

/// <summary>Where offer banners live. A request may only use a path this API made.</summary>
public static partial class OfferImages
{
    public const string Folder = "offers";

    [GeneratedRegex("^/media/offers/[a-z0-9-]{1,80}\\.(jpg|png|webp)$")]
    private static partial Regex OwnPath();

    public static bool IsOwnPath(string? path) => path is not null && OwnPath().IsMatch(path);
}
