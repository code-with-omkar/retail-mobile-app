using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface IOrderService
{
    Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(CancellationToken cancellationToken = default);
    Task<OrderResponse?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
}
