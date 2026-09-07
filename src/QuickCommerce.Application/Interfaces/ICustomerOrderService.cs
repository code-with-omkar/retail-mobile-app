using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICustomerOrderService
{
    Task<IReadOnlyList<OrderResponse>?> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task<OrderResponse?> GetDetailsAsync(Guid orderId, CancellationToken cancellationToken = default);
}