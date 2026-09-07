using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CheckoutService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver,
    IValidator<CheckoutRequest> validator) : ICheckoutService
{
    public async Task<CheckoutOperationResult> CheckoutAsync(Guid storeId, CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CheckoutOperationResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
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

        var result = await data.TryCheckoutCartAsync(customer.Id, storeId, request, cancellationToken);
        return result.Status switch
        {
            CheckoutCommitStatus.Succeeded => CheckoutOperationResult.Succeeded(result.Order!),
            CheckoutCommitStatus.CartNotFound => CheckoutOperationResult.NotFound("Cart not found"),
            CheckoutCommitStatus.CartEmpty => CheckoutOperationResult.Conflict("Cart is empty"),
            CheckoutCommitStatus.ProductUnavailable => CheckoutOperationResult.Conflict("A cart product is no longer available"),
            CheckoutCommitStatus.PriceChanged => CheckoutOperationResult.Conflict("A cart product price has changed"),
            CheckoutCommitStatus.InventoryConflict => CheckoutOperationResult.Conflict("Inventory changed while checkout was in progress"),
            _ => CheckoutOperationResult.Conflict("Checkout could not be completed")
        };
    }
}