using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>The signed-in customer's saved addresses. The customer is taken from the token, never from the request.</summary>
public interface IAddressService
{
    Task<AccountResult<IReadOnlyList<CustomerAddressResponse>>> ListAsync(CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerAddressResponse>> CreateAsync(CustomerAddressRequest request, CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerAddressResponse>> UpdateAsync(Guid addressId, CustomerAddressRequest request, CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerAddressResponse>> SetDefaultAsync(Guid addressId, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> DeleteAsync(Guid addressId, CancellationToken cancellationToken = default);
}

/// <summary>Whether a delivery point can be served, shared by the public check, saved addresses and checkout.</summary>
public interface IServiceabilityService
{
    /// <summary>One answer per point, in order. With an organization only its stores count; without one every active store counts.</summary>
    Task<IReadOnlyList<ServiceabilityResponse>> CheckAsync(IReadOnlyList<GeoPoint> points, Guid? organizationId, CancellationToken cancellationToken = default);
}

public sealed record AddressOwner(Guid CustomerId, Guid OrganizationId);

/// <param name="Label">See <see cref="CustomerAddress"/>.</param>
public sealed record AddressFields(string Label, string Line, string FlatOrBuilding, string Landmark, double Latitude, double Longitude, string ReceiverName, string ReceiverPhone);

public sealed record AddAddressResult(CustomerAddress? Address, bool LimitReached);

/// <summary>Persistence for saved addresses. Every method is scoped to one customer, so another customer's address is simply not found.</summary>
public interface IAddressStore
{
    /// <summary>The active customer behind a user id, or null when the user is not an active customer.</summary>
    Task<AddressOwner?> FindOwnerAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Default first, then most recently changed.</summary>
    Task<IReadOnlyList<CustomerAddress>> ListAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<CustomerAddress?> GetAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken = default);

    /// <summary>Adds the address unless the customer is at the limit. The first address becomes the default. Safe against two requests at once.</summary>
    Task<AddAddressResult> AddAsync(Guid customerId, CustomerAddress address, int maxPerCustomer, CancellationToken cancellationToken = default);

    Task<CustomerAddress?> UpdateAsync(Guid customerId, Guid addressId, AddressFields fields, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Makes the address the default and clears the previous default in one transaction. False when it is not the customer's.</summary>
    Task<bool> SetDefaultAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Deletes the address; deleting the default promotes the most recently changed remaining one. False when it is not the customer's.</summary>
    Task<bool> DeleteAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default);
}
