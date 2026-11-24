using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>What makes an ad acceptable, and when it is showing.</summary>
public static class AdRules
{
    public const int TitleMax = 80;
    public const int UrlMax = 300;
    public const int SortMax = 1000;

    /// <summary>The first thing wrong with a request, or null when it is fine.</summary>
    public static string? Problem(SaveAdRequest request, DateTime startsAt)
    {
        if (!AdPlacements.All.Contains(request.Placement))
        {
            return "Choose where the ad is shown.";
        }

        if (request.Title is { } title && title.Trim().Length > TitleMax)
        {
            return $"The title can be at most {TitleMax} letters.";
        }

        if (!OfferImages.IsOwnPath(request.ImageUrl))
        {
            return "Upload a picture for the ad first.";
        }

        if (!AdTargets.All.Contains(request.TargetType))
        {
            return "Choose what a tap on the ad opens.";
        }

        var target = request.Target?.Trim();
        switch (request.TargetType)
        {
            case AdTargets.None:
                break;
            case AdTargets.Url:
                if (string.IsNullOrEmpty(target) || target.Length > UrlMax || !Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    return "The web link must start with https://.";
                }

                break;
            default:
                if (!Guid.TryParse(target, out _))
                {
                    return $"Choose the {request.TargetType.ToLowerInvariant()} a tap should open.";
                }

                break;
        }

        if (request.EndsAt is { } endsAt && endsAt <= startsAt)
        {
            return "The ad must end after it starts.";
        }

        if (request.SortOrder is < 0 or > SortMax)
        {
            return $"The order must be between 0 and {SortMax}.";
        }

        return null;
    }

    /// <summary>The stored form of a target: trimmed, and empty for "opens nothing".</summary>
    public static string? NormalizeTarget(string targetType, string? target) =>
        targetType == AdTargets.None || string.IsNullOrWhiteSpace(target) ? null : target.Trim();

    public static bool IsLive(Ad ad, DateTime now) => ad.IsActive && ad.StartsAt <= now && (ad.EndsAt is null || ad.EndsAt > now);

    public static string Status(Ad ad, DateTime now)
    {
        if (!ad.IsActive)
        {
            return "Off";
        }

        if (ad.StartsAt > now)
        {
            return "Scheduled";
        }

        return ad.EndsAt is { } end && end <= now ? "Ended" : "Live";
    }
}
