using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class OrderOperationsService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver,
    IValidator<ChangeOrderStatusRequest> validator) : IOrderOperationsService
{
    public async Task<OrderOperationResult> ChangeStatusAsync(Guid orderId, ChangeOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OrderOperationResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin))
        {
            return OrderOperationResult.Unauthorized("The authenticated user cannot operate store orders");
        }

        if (context.Role == Role.StoreStaff && !context.StoreId.HasValue)
        {
            return OrderOperationResult.Unauthorized("Store staff must be assigned to a store");
        }

        var result = await data.TryTransitionOrderAsync(orderId, context.OrganizationId, context.StoreId, request.Status, cancellationToken);
        return result.Status switch
        {
            OrderLifecycleStatus.Succeeded => OrderOperationResult.Succeeded(result.Order!),
            OrderLifecycleStatus.NotFound => OrderOperationResult.NotFound("Order not found in the authorized store scope"),
            OrderLifecycleStatus.InvalidTransition => OrderOperationResult.InvalidTransition("The requested order status transition is invalid"),
            OrderLifecycleStatus.ConcurrencyConflict => OrderOperationResult.Conflict("The order changed before this operation completed"),
            _ => OrderOperationResult.Conflict("Order status could not be changed")
        };
    }
}