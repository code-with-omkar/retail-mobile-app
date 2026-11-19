using System.Globalization;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Scheduled-delivery settings (administrators) and the windows a customer can pick (signed-in customers).</summary>
public sealed class SlotService(
    ISlotStore slots,
    ICommerceStore commerce,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    TimeProvider? timeProvider = null) : ISlotService
{
    private const string NotAllowed = "Only an administrator can see or change delivery slots.";
    private const string BadTime = "Give the times as HH:mm, for example 09:00.";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    public static int? Minutes(string? text)
    {
        if (text is null || text.Length != 5 || text[2] != ':' || !int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hour) || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minute) || minute > 59)
        {
            return null;
        }

        return hour is >= 0 and <= 24 ? hour * 60 + minute : null;
    }

    public static SlotSettingsDto ToDto(SlotSettings s) => new(s.Enabled, Clock(s.OpenMinutes), Clock(s.CloseMinutes), s.WindowMinutes, s.Capacity, s.CutoffMinutes, s.DaysAhead, s.AsapAllowed);

    /// <summary>The settings for a store: its own, else the default, else scheduling off.</summary>
    public static async Task<SlotSettings> EffectiveAsync(ISlotStore slots, Guid storeId, CancellationToken cancellationToken = default)
    {
        var overrides = await slots.GetOverridesAsync(cancellationToken);
        if (overrides.TryGetValue(storeId, out var own))
        {
            return SlotSettings.From(own);
        }

        return await slots.GetDefaultAsync(cancellationToken) is { } fallback ? SlotSettings.From(fallback) : SlotSettings.Off;
    }

    public async Task<SlotResult<SlotRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        return scope?.IsApplicationAdmin == true
            ? new(SlotServiceStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken))
            : new(SlotServiceStatus.Forbidden, Message: NotAllowed);
    }

    public async Task<SlotResult<SlotRulesResponse>> SetDefaultAsync(SlotSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(SlotServiceStatus.Forbidden, Message: NotAllowed);
        }

        var parsed = Parse(settings);
        if (parsed is null)
        {
            return new(SlotServiceStatus.Invalid, Message: BadTime);
        }

        if (SlotRules.Validate(parsed) is { } problem)
        {
            return new(SlotServiceStatus.Invalid, Message: problem);
        }

        await slots.SetAsync(null, parsed, scope.UserId.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(SlotServiceStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken));
    }

    public async Task<SlotResult<SlotRulesResponse>> SetStoreAsync(Guid storeId, SlotSettingsDto? settings, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(SlotServiceStatus.Forbidden, Message: NotAllowed);
        }

        if ((await slots.GetStoresAsync(scope.OrganizationId, cancellationToken)).All(store => store.Id != storeId))
        {
            return new(SlotServiceStatus.NotFound, Message: "Store not found.");
        }

        SlotSettings? parsed = null;
        if (settings is not null)
        {
            parsed = Parse(settings);
            if (parsed is null)
            {
                return new(SlotServiceStatus.Invalid, Message: BadTime);
            }

            if (SlotRules.Validate(parsed) is { } problem)
            {
                return new(SlotServiceStatus.Invalid, Message: problem);
            }
        }

        await slots.SetAsync(storeId, parsed, scope.UserId.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(SlotServiceStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken));
    }

    public async Task<SlotResult<SlotsResponse>> ListAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var context = await userContext.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return new(SlotServiceStatus.Forbidden, Message: "Only a customer can choose a delivery time.");
        }

        var store = await commerce.GetStoreAsync(storeId, cancellationToken);
        if (store is not { IsActive: true } || store.OrganizationId != context.OrganizationId)
        {
            return new(SlotServiceStatus.NotFound, Message: "Store not found.");
        }

        var settings = await EffectiveAsync(slots, storeId, cancellationToken);
        if (!settings.Enabled)
        {
            return new(SlotServiceStatus.Succeeded, new SlotsResponse(false, true, []));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var booked = await slots.GetBookedAsync(storeId, now, now.AddDays(settings.DaysAhead + 1), cancellationToken);
        var windows = SlotRules.Windows(settings, now, start => booked.GetValueOrDefault(start))
            .Select(window => ToResponse(window, now))
            .ToArray();
        return new(SlotServiceStatus.Succeeded, new SlotsResponse(true, settings.AsapAllowed, windows));
    }

    private static SlotWindowResponse ToResponse(SlotWindow window, DateTime now)
    {
        var label = SlotRules.Label(window.StartUtc, window.EndUtc, now);
        var split = label.IndexOf(", ", StringComparison.Ordinal);
        return new SlotWindowResponse(
            window.StartUtc, window.EndUtc, (window.StartUtc + SlotRules.India).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            label[..split], label[(split + 2)..], window.Available, window.Left);
    }

    private static SlotSettings? Parse(SlotSettingsDto dto) =>
        Minutes(dto.Open) is { } open && Minutes(dto.Close) is { } close
            ? new SlotSettings(dto.Enabled, open, close, dto.WindowMinutes, dto.Capacity, dto.CutoffMinutes, dto.DaysAhead, dto.AsapAllowed)
            : null;

    private async Task<SlotRulesResponse> BuildAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var fallback = await slots.GetDefaultAsync(cancellationToken) is { } row ? SlotSettings.From(row) : SlotSettings.Off;
        var overrides = await slots.GetOverridesAsync(cancellationToken);
        var stores = (await slots.GetStoresAsync(organizationId, cancellationToken))
            .OrderBy(store => store.Name)
            .Select(store => overrides.TryGetValue(store.Id, out var own)
                ? new StoreSlotRules(store.Id, store.Name, ToDto(SlotSettings.From(own)), ToDto(SlotSettings.From(own)))
                : new StoreSlotRules(store.Id, store.Name, null, ToDto(fallback)))
            .ToArray();
        return new SlotRulesResponse(ToDto(fallback), stores);
    }
}
