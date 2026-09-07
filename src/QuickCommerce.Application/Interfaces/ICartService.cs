using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICartService
{
    Task<CartResponse?> GetCartAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<CartOperationResult> AddItemAsync(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken = default);
    Task<CartOperationResult> UpdateItemAsync(Guid storeId, Guid productId, UpdateCartItemRequest request, CancellationToken cancellationToken = default);
    Task<CartOperationResult> RemoveItemAsync(Guid storeId, Guid productId, CancellationToken cancellationToken = default);
}