using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Interfaces;

public interface ICartService
{
    Task<CartResponse?> GetCartAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>The customer's cart in whichever store it is (the app keeps one), or null when there is none.</summary>
    Task<CartResponse?> GetCurrentCartAsync(CancellationToken cancellationToken = default);

    Task<CartOperationResult> AddItemAsync(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken = default);
    /// <param name="variantId">Which pack size of the product. Omitted: the product's only line, or its default variant when there are several.</param>
    Task<CartOperationResult> UpdateItemAsync(Guid storeId, Guid productId, UpdateCartItemRequest request, Guid? variantId = null, CancellationToken cancellationToken = default);
    Task<CartOperationResult> RemoveItemAsync(Guid storeId, Guid productId, Guid? variantId = null, CancellationToken cancellationToken = default);

    /// <summary>Empties the cart (deletes it). Succeeds when there was none.</summary>
    Task<CartOperationResult> ClearAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Adds a device-side cart to the server cart: quantities are added, capped at what the store can supply, and what could not be added is reported.</summary>
    Task<CartOperationResult> MergeAsync(Guid storeId, MergeCartRequest request, CancellationToken cancellationToken = default);

    /// <summary>Brings every line's price up to date with the shop's current price, after the customer has seen and accepted the change.</summary>
    Task<CartOperationResult> RepriceAsync(Guid storeId, CancellationToken cancellationToken = default);
}
