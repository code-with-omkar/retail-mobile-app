using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class AddressService(
    IAddressStore store,
    IServiceabilityService serviceability,
    ICurrentUser currentUser,
    IValidator<CustomerAddressRequest> validator,
    AddressSettings settings,
    TimeProvider clock) : IAddressService
{
    private const string NotFound = "Address not found.";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AccountResult<IReadOnlyList<CustomerAddressResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        if (owner is null)
        {
            return AccountResult<IReadOnlyList<CustomerAddressResponse>>.Unauthorized("Authentication is required.");
        }

        var addresses = await store.ListAsync(owner.CustomerId, cancellationToken);
        var answers = await serviceability.CheckAsync(addresses.Select(address => new GeoPoint(address.Latitude, address.Longitude)).ToArray(), owner.OrganizationId, cancellationToken);
        var responses = addresses.Select((address, index) => Map(address, answers[index])).ToArray();
        return AccountResult<IReadOnlyList<CustomerAddressResponse>>.Ok(responses);
    }

    public async Task<AccountResult<CustomerAddressResponse>> CreateAsync(CustomerAddressRequest request, CancellationToken cancellationToken = default)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        if (owner is null)
        {
            return AccountResult<CustomerAddressResponse>.Unauthorized("Authentication is required.");
        }

        var invalid = await ValidateAsync(request, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        var fields = Normalize(request);
        var added = await store.AddAsync(owner.CustomerId, new CustomerAddress
        {
            Label = fields.Label,
            Line = fields.Line,
            FlatOrBuilding = fields.FlatOrBuilding,
            Landmark = fields.Landmark,
            Latitude = fields.Latitude,
            Longitude = fields.Longitude,
            ReceiverName = fields.ReceiverName,
            ReceiverPhone = fields.ReceiverPhone,
            CreatedAt = Now,
            UpdatedAt = Now
        }, settings.MaxPerCustomer, cancellationToken);

        return added.LimitReached
            ? AccountResult<CustomerAddressResponse>.Conflict($"You can save up to {settings.MaxPerCustomer} addresses. Delete one to add another.")
            : AccountResult<CustomerAddressResponse>.Ok(await MapAsync(added.Address!, owner, cancellationToken));
    }

    public async Task<AccountResult<CustomerAddressResponse>> UpdateAsync(Guid addressId, CustomerAddressRequest request, CancellationToken cancellationToken = default)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        if (owner is null)
        {
            return AccountResult<CustomerAddressResponse>.Unauthorized("Authentication is required.");
        }

        var invalid = await ValidateAsync(request, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        var updated = await store.UpdateAsync(owner.CustomerId, addressId, Normalize(request), Now, cancellationToken);
        return updated is null
            ? AccountResult<CustomerAddressResponse>.NotFound(NotFound)
            : AccountResult<CustomerAddressResponse>.Ok(await MapAsync(updated, owner, cancellationToken));
    }

    public async Task<AccountResult<CustomerAddressResponse>> SetDefaultAsync(Guid addressId, CancellationToken cancellationToken = default)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        if (owner is null)
        {
            return AccountResult<CustomerAddressResponse>.Unauthorized("Authentication is required.");
        }

        if (!await store.SetDefaultAsync(owner.CustomerId, addressId, Now, cancellationToken))
        {
            return AccountResult<CustomerAddressResponse>.NotFound(NotFound);
        }

        var address = await store.GetAsync(owner.CustomerId, addressId, cancellationToken);
        return address is null
            ? AccountResult<CustomerAddressResponse>.NotFound(NotFound)
            : AccountResult<CustomerAddressResponse>.Ok(await MapAsync(address, owner, cancellationToken));
    }

    public async Task<AccountResult<bool>> DeleteAsync(Guid addressId, CancellationToken cancellationToken = default)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        if (owner is null)
        {
            return AccountResult<bool>.Unauthorized("Authentication is required.");
        }

        return await store.DeleteAsync(owner.CustomerId, addressId, Now, cancellationToken)
            ? AccountResult<bool>.Ok(true)
            : AccountResult<bool>.NotFound(NotFound);
    }

    private async Task<AddressOwner?> ResolveOwnerAsync(CancellationToken cancellationToken) =>
        currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out var userId)
            ? await store.FindOwnerAsync(userId, cancellationToken)
            : null;

    private async Task<AccountResult<CustomerAddressResponse>?> ValidateAsync(CustomerAddressRequest request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid)
        {
            return null;
        }

        var errors = validation.Errors.Select(error => error.ErrorMessage).Distinct().ToArray();
        return AccountResult<CustomerAddressResponse>.Invalid(string.Join(" ", errors), errors);
    }

    private static AddressFields Normalize(CustomerAddressRequest request) => new(
        request.Label.Trim(),
        request.Line.Trim(),
        request.FlatOrBuilding?.Trim() ?? string.Empty,
        request.Landmark?.Trim() ?? string.Empty,
        request.Latitude,
        request.Longitude,
        request.ReceiverName.Trim(),
        AccountRules.NormalizePhone(request.ReceiverPhone)!);

    private async Task<CustomerAddressResponse> MapAsync(CustomerAddress address, AddressOwner owner, CancellationToken cancellationToken)
    {
        var answer = (await serviceability.CheckAsync([new GeoPoint(address.Latitude, address.Longitude)], owner.OrganizationId, cancellationToken))[0];
        return Map(address, answer);
    }

    private static CustomerAddressResponse Map(CustomerAddress address, ServiceabilityResponse answer) => new(
        address.Id,
        address.Label,
        address.Line,
        address.FlatOrBuilding,
        address.Landmark,
        address.Latitude,
        address.Longitude,
        address.ReceiverName,
        address.ReceiverPhone,
        address.IsDefault,
        answer.Serviceable,
        answer.Reason);
}
