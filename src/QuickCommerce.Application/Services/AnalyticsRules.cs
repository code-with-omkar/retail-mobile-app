using System.Text.RegularExpressions;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// What an analytics event may contain and how the funnel is worked out. Events come from anyone's phone and are not signed in, so everything
/// is checked here: only known step names, a session id that looks like one, and a detail of a few safe words. Nothing personal is kept.
/// </summary>
public static partial class AnalyticsRules
{
    public const int MaxBatch = 25;
    public const int RetentionDays = 90;
    public const int DetailMax = 40;
    public const int TopTerms = 10;

    [GeneratedRegex("^[A-Za-z0-9_-]{16,64}$")]
    private static partial Regex SessionPattern();

    [GeneratedRegex("[^a-z0-9 +_.-]")]
    private static partial Regex Unsafe();

    /// <summary>7 or more digits anywhere in the text, with spaces or dashes between them or not: a phone number, a card or an id.</summary>
    private static bool HasManyDigits(string text) => text.Count(char.IsDigit) >= 7;

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();

    public static bool IsKnown(string? name) => name is not null && AnalyticsNames.All.Contains(name);

    public static bool IsSession(string? id) => id is not null && SessionPattern().IsMatch(id);

    /// <summary>
    /// A safe, short detail: lower case, letters, digits, spaces and a few marks, at most 40 characters. Anything that looks personal (an email
    /// address, or 7 or more digits in all, such as a phone number however it is spaced) is dropped altogether rather than cleaned.
    /// Empty means no detail.
    /// </summary>
    public static string? CleanDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail) || detail.Contains('@') || HasManyDigits(detail))
        {
            return null;
        }

        var text = Spaces().Replace(Unsafe().Replace(detail.Trim().ToLowerInvariant(), string.Empty), " ").Trim();
        if (text.Length > DetailMax)
        {
            text = text[..DetailMax].TrimEnd();
        }

        return text.Length == 0 ? null : text;
    }

    /// <summary>Steps of the buying funnel with the share of the first step's sessions that got that far.</summary>
    public static IReadOnlyList<FunnelStep> Funnel(IReadOnlyDictionary<string, int> sessionsByStep)
    {
        var first = sessionsByStep.GetValueOrDefault(AnalyticsNames.Funnel[0]);
        var steps = new List<FunnelStep>();
        var previous = first;
        foreach (var name in AnalyticsNames.Funnel)
        {
            var sessions = sessionsByStep.GetValueOrDefault(name);
            steps.Add(new FunnelStep(
                name,
                sessions,
                first == 0 ? 0 : Percent(sessions, first),
                steps.Count == 0 ? (first == 0 ? 0 : 100) : (previous == 0 ? 0 : Percent(sessions, previous))));
            previous = sessions;
        }

        return steps;
    }

    /// <summary>A whole percent, rounded, never above 100 (a step can have more sessions than the one before it when events arrive out of order or one step is skipped).</summary>
    public static int Percent(int part, int whole) => whole <= 0 ? 0 : Math.Min(100, (int)Math.Round(part * 100.0 / whole, MidpointRounding.AwayFromZero));

    /// <summary>The day an event belongs to: India time, like the rest of the reports.</summary>
    public static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc) + SlotRules.India);
}
