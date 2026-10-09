using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICustomerOrderService
{
    Task<IReadOnlyList<OrderResponse>?> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task<OrderResponse?> GetDetailsAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the customer's own order while the shop has not accepted it.</summary>
    Task<CancelOrderResult> CancelAsync(Guid orderId, CancellationToken cancellationToken = default);
}