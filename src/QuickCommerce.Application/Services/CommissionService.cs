using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Application.Services;

/// <summary>The one rule for commission: a percent of the product total after discounts (delivery and handling fees are the store's).</summary>
public static class CommissionMath
{
    public static decimal Amount(decimal productTotal, decimal percent) => Math.Round(productTotal * percent / 100m, 2, MidpointRounding.AwayFromZero);

    public static bool IsValid(decimal percent) => percent is >= 0 and <= 100 && decimal.Round(percent, 2) == percent;
}

/// <summary>Commission settings. Only an administrator reads or changes them.</summary>
public sealed class CommissionService(
    ICommissionStore rates,
    IAuthorizationScopeService scopeService,
    TimeProvider? timeProvider = null) : ICommissionService
{
    private const string NotAllowed = "Only an administrator can see or change commission.";
    private const string Bad = "The commission must be between 0 and 100 percent, with at most 2 decimals.";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>The percent for a store's orders: its own setting, else the default.</summary>
    public static async Task<decimal> EffectiveAsync(ICommissionStore rates, Guid storeId, CancellationToken cancellationToken = default) =>
        (await rates.GetOverridesAsync(cancellationToken)).TryGetValue(storeId, out var own) ? own : await rates.GetDefaultAsync(cancellationToken);

    public async Task<CommissionResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        return scope?.IsApplicationAdmin == true
            ? new(CommissionStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken))
            : new(CommissionStatus.Forbidden, Message: NotAllowed);
    }

    public async Task<CommissionResult> SetDefaultAsync(decimal percent, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CommissionStatus.Forbidden, Message: NotAllowed);
        }

        if (!CommissionMath.IsValid(percent))
        {
            return new(CommissionStatus.Invalid, Message: Bad);
        }

        await rates.SetAsync(null, percent, scope.UserId.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(CommissionStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken));
    }

    public async Task<CommissionResult> SetStoreAsync(Guid storeId, decimal? percent, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CommissionStatus.Forbidden, Message: NotAllowed);
        }

        if (percent is { } value && !CommissionMath.IsValid(value))
        {
            return new(CommissionStatus.Invalid, Message: Bad);
        }

        if ((await rates.GetStoresAsync(scope.OrganizationId, cancellationToken)).All(store => store.Id != storeId))
        {
            return new(CommissionStatus.NotFound, Message: "Store not found.");
        }

        await rates.SetAsync(storeId, percent, scope.UserId.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(CommissionStatus.Succeeded, await BuildAsync(scope.OrganizationId, cancellationToken));
    }

    private async Task<CommissionResponse> BuildAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var defaultPercent = await rates.GetDefaultAsync(cancellationToken);
        var overrides = await rates.GetOverridesAsync(cancellationToken);
        var stores = (await rates.GetStoresAsync(organizationId, cancellationToken))
            .OrderBy(store => store.Name)
            .Select(store => new StoreCommission(store.Id, store.Name, overrides.TryGetValue(store.Id, out var own) ? own : null, overrides.TryGetValue(store.Id, out var effective) ? effective : defaultPercent))
            .ToArray();
        return new CommissionResponse(defaultPercent, stores);
    }
}
