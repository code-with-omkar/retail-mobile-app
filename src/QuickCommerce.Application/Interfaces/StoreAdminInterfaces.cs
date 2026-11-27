namespace QuickCommerce.Application.Interfaces;

/// <summary>What an administrator sends to add or change a store. <see cref="Code"/> starts the store's order numbers.</summary>
public sealed record SaveStoreRequest(string Name, string Address, double Latitude, double Longitude, double ServiceRadiusKm, string Code, string? PhoneNumber = null, bool IsActive = true);

public enum StoreAdminStatus { Succeeded, Forbidden, NotFound, Invalid, Conflict }

public sealed record StoreAdminResult(StoreAdminStatus Status, DTOs.StoreResponse? Store = null, string? Message = null);

public interface IStoreAdminService
{
    /// <summary>Administrators only.</summary>
    Task<StoreAdminResult> CreateAsync(SaveStoreRequest request, CancellationToken cancellationToken = default);

    Task<StoreAdminResult> UpdateAsync(Guid id, SaveStoreRequest request, CancellationToken cancellationToken = default);
}
