using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICheckoutService
{
    /// <param name="idempotencyKey">Chosen by the app for one attempt to order. The same key returns the order it produced instead of a second one.</param>
    Task<CheckoutOperationResult> CheckoutAsync(Guid storeId, CheckoutRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default);
}
