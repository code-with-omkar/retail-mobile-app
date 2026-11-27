using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>Slot settings as the portal sends and reads them. Times are India time as "HH:mm".</summary>
public sealed record SlotSettingsDto(bool Enabled, string Open, string Close, int WindowMinutes, int Capacity, int CutoffMinutes, int DaysAhead, bool AsapAllowed);

public sealed record StoreSlotRules(Guid StoreId, string Name, SlotSettingsDto? Override, SlotSettingsDto Effective);

public sealed record SlotRulesResponse(SlotSettingsDto Default, IReadOnlyList<StoreSlotRules> Stores);

/// <summary>One window a customer can choose. <see cref="Day"/> is the India date (yyyy-MM-dd) to group by.</summary>
public sealed record SlotWindowResponse(DateTime StartUtc, DateTime EndUtc, string Day, string DayLabel, string TimeLabel, bool Available, int Left);

/// <summary>What a customer can choose for a store. Scheduling off means no windows and only "as soon as possible".</summary>
public sealed record SlotsResponse(bool Enabled, bool AsapAllowed, IReadOnlyList<SlotWindowResponse> Windows);

public interface ISlotStore
{
    /// <summary>The default row, or null when none was ever set.</summary>
    Task<DeliverySlotSetting?> GetDefaultAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, DeliverySlotSetting>> GetOverridesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the default (no store) or one store's own settings; null settings remove a store's own row.</summary>
    Task SetAsync(Guid? storeId, Services.SlotSettings? settings, string? by, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>How many orders each window of the store holds, by window start (UTC), for windows starting from <paramref name="fromUtc"/> up to <paramref name="toUtc"/>.</summary>
    Task<IReadOnlyDictionary<DateTime, int>> GetBookedAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

public enum SlotServiceStatus { Succeeded, Forbidden, NotFound, Invalid }

public sealed record SlotResult<T>(SlotServiceStatus Status, T? Value = default, string? Message = null);

public interface ISlotService
{
    /// <summary>Administrators only: the default and every store's own settings.</summary>
    Task<SlotResult<SlotRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<SlotResult<SlotRulesResponse>> SetDefaultAsync(SlotSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>One store's own settings, or null to use the default again.</summary>
    Task<SlotResult<SlotRulesResponse>> SetStoreAsync(Guid storeId, SlotSettingsDto? settings, CancellationToken cancellationToken = default);

    /// <summary>The windows a signed-in customer can choose in a store, with what is left in each.</summary>
    Task<SlotResult<SlotsResponse>> ListAsync(Guid storeId, CancellationToken cancellationToken = default);
}
