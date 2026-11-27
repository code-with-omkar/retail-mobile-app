using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>What one store pays: its own percent if it has one, otherwise the default.</summary>
public sealed record StoreCommission(Guid StoreId, string Name, decimal? OverridePercent, decimal EffectivePercent);

public sealed record CommissionResponse(decimal DefaultPercent, IReadOnlyList<StoreCommission> Stores);

/// <summary>A percent from 0 to 100 with at most 2 decimals; for a store, null removes its own setting so the default applies again.</summary>
public sealed record SetCommissionRequest(decimal? Percent);

public interface ICommissionStore
{
    Task<decimal> GetDefaultAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, decimal>> GetOverridesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the default (no store) or one store's own percent; a null percent removes a store's own setting.</summary>
    Task SetAsync(Guid? storeId, decimal? percent, string? by, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

public enum CommissionStatus { Succeeded, Forbidden, Invalid, NotFound }

public sealed record CommissionResult(CommissionStatus Status, CommissionResponse? Value = null, string? Message = null);

public interface ICommissionService
{
    /// <summary>Administrators only: the default and every store's percent.</summary>
    Task<CommissionResult> GetAsync(CancellationToken cancellationToken = default);

    Task<CommissionResult> SetDefaultAsync(decimal percent, CancellationToken cancellationToken = default);

    Task<CommissionResult> SetStoreAsync(Guid storeId, decimal? percent, CancellationToken cancellationToken = default);
}
