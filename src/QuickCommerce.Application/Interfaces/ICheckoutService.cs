using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICheckoutService
{
    Task<CheckoutOperationResult> CheckoutAsync(Guid storeId, CheckoutRequest request, CancellationToken cancellationToken = default);
}