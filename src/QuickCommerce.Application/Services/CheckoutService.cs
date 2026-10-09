using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed partial class CheckoutService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver,
    IValidator<CheckoutRequest> validator,
    PricingSettings? pricingSettings = null,
    IAddressStore? addresses = null,
    IDeliveryEstimator? deliveryEstimator = null) : ICheckoutService
{
    private const int MaxDeliveryAddressLength = 500;
    private readonly PricingSettings pricing = pricingSettings ?? new PricingSettings();

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex KeyPattern();

    public async Task<CheckoutOperationResult> CheckoutAsync(Guid storeId, CheckoutRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CheckoutOperationResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        if (idempotencyKey is not null && !KeyPattern().IsMatch(idempotencyKey))
        {
            return CheckoutOperationResult.Invalid("The Idempotency-Key must be 8 to 64 letters, digits, dashes or underscores.");
        }

        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return CheckoutOperationResult.Unauthorized("The authenticated user cannot checkout");
        }

        var customer = await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken);
        var store = await data.GetStoreAsync(storeId, cancellationToken);
        if (customer is not { IsActive: true } || store is not { IsActive: true } || store.OrganizationId != context.OrganizationId)
        {
            return CheckoutOperationResult.Unauthorized("The authenticated user cannot checkout from this store");
        }

        var delivery = await ResolveDeliveryAsync(customer.Id, request, cancellationToken);
        if (delivery is null)
        {
            return request.AddressId.HasValue ? CheckoutOperationResult.NotFound("Address not found") : CheckoutOperationResult.Invalid("Delivery address is required");
        }

        // The store must actually deliver to the address. Checked here, on the server, before anything in the cart or stock changes.
        if (!ServiceabilityRules.StoreCovers(store, delivery.Latitude, delivery.Longitude))
        {
            return CheckoutOperationResult.Conflict("This store does not deliver to the delivery address.", ServiceabilityReasons.OutsideServiceArea);
        }

        // The estimate the customer is given now stays on the order, whatever the store's estimate becomes later.
        int? estimate = deliveryEstimator is null ? null : deliveryEstimator.EstimateMinutes(StoreSelectionService.DistanceKm(delivery.Latitude, delivery.Longitude, store.Latitude, store.Longitude));
        var commit = new CheckoutCommit(delivery, pricing, idempotencyKey, idempotencyKey is null ? null : HashOf(storeId, delivery), estimate);
        var result = await data.TryCheckoutCartAsync(customer.Id, storeId, commit, cancellationToken);
        return result.Status switch
        {
            CheckoutCommitStatus.Succeeded => CheckoutOperationResult.Succeeded(CustomerOrderService.WithStore(result.Order!, store), result.Replayed),
            CheckoutCommitStatus.CartNotFound => CheckoutOperationResult.NotFound("Cart not found"),
            CheckoutCommitStatus.CartEmpty => CheckoutOperationResult.Conflict("Cart is empty", CheckoutReasons.CartEmpty),
            CheckoutCommitStatus.ProductUnavailable => CheckoutOperationResult.Conflict("A cart product is no longer available", CheckoutReasons.ProductUnavailable, result.Issues),
            CheckoutCommitStatus.PriceChanged => CheckoutOperationResult.Conflict("A cart product price has changed", CheckoutReasons.PriceChanged, result.Issues),
            CheckoutCommitStatus.InventoryConflict => CheckoutOperationResult.Conflict("Inventory changed while checkout was in progress", CheckoutReasons.InventoryConflict, result.Issues),
            CheckoutCommitStatus.KeyReused => CheckoutOperationResult.Conflict("This Idempotency-Key was already used for a different order. Use a new key for a new order.", CheckoutReasons.IdempotencyKeyReused),
            _ => CheckoutOperationResult.Conflict("Checkout could not be completed")
        };
    }

    /// <summary>The saved address (the customer's own; anyone else's is simply not found) or the typed one.</summary>
    private async Task<CheckoutDelivery?> ResolveDeliveryAsync(Guid customerId, CheckoutRequest request, CancellationToken cancellationToken)
    {
        if (request.AddressId is { } addressId)
        {
            var address = addresses is null ? null : await addresses.GetAsync(customerId, addressId, cancellationToken);
            return address is null ? null : new CheckoutDelivery(Format(address), address.Latitude, address.Longitude, address.Id, address.ReceiverName, address.ReceiverPhone);
        }

        return string.IsNullOrWhiteSpace(request.DeliveryAddress) || request.Latitude is null || request.Longitude is null
            ? null
            : new CheckoutDelivery(request.DeliveryAddress.Trim(), request.Latitude.Value, request.Longitude.Value);
    }

    /// <summary>One line for the rider: flat, then the address, then the landmark.</summary>
    private static string Format(CustomerAddress address)
    {
        var parts = new[] { address.FlatOrBuilding, address.Line }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim());
        var text = string.Join(", ", parts);
        if (!string.IsNullOrWhiteSpace(address.Landmark))
        {
            text += $" (Landmark: {address.Landmark.Trim()})";
        }

        return text.Length <= MaxDeliveryAddressLength ? text : text[..MaxDeliveryAddressLength];
    }

    /// <summary>The same store and place always give the same hash, so a retry matches and a different order does not.</summary>
    private static string HashOf(Guid storeId, CheckoutDelivery delivery)
    {
        var where = delivery.AddressId is { } id ? id.ToString("N") : $"{delivery.Address}|{delivery.Latitude:F5}|{delivery.Longitude:F5}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{storeId:N}|{where}")));
    }
}
