using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICartService
{
    Task<CartResponse?> GetCartAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<CartOperationResult> AddItemAsync(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken = default);
    /// <param name="variantId">Which pack size of the product. Omitted: the product's only line, or its default variant when there are several.</param>
    Task<CartOperationResult> UpdateItemAsync(Guid storeId, Guid productId, UpdateCartItemRequest request, Guid? variantId = null, CancellationToken cancellationToken = default);
    Task<CartOperationResult> RemoveItemAsync(Guid storeId, Guid productId, Guid? variantId = null, CancellationToken cancellationToken = default);
}