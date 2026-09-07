using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CartService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver,
    IValidator<AddCartItemRequest> addItemValidator,
    IValidator<UpdateCartItemRequest> updateItemValidator) : ICartService
{
    public async Task<CartResponse?> GetCartAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return null;
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        return cart is null ? null : Map(cart);
    }

    public async Task<CartOperationResult> AddItemAsync(Guid storeId, AddCartItemRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await addItemValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CartOperationResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        var product = await data.GetProductAsync(request.ProductId, cancellationToken);
        if (product is null || !product.IsActive)
        {
            return CartOperationResult.NotFound("Product not found");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { CustomerId = scope.Value.Customer.Id, StoreId = storeId };
            cart.Items.Add(CreateItem(product, request.Quantity));
            await data.AddCartAsync(cart, cancellationToken);
        }
        else
        {
            var item = cart.Items.FirstOrDefault(item => item.ProductId == product.Id);
            if (item is null)
            {
                cart.Items.Add(CreateItem(product, request.Quantity));
            }
            else if (item.Quantity + request.Quantity > 1000)
            {
                return CartOperationResult.Conflict("Cart item quantity cannot exceed 1000");
            }
            else
            {
                item.Quantity += request.Quantity;
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await data.SaveCartAsync(cart, cancellationToken);
        }

        return CartOperationResult.Succeeded(Map(cart));
    }

    public async Task<CartOperationResult> UpdateItemAsync(Guid storeId, Guid productId, UpdateCartItemRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await updateItemValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CartOperationResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        var item = cart?.Items.FirstOrDefault(item => item.ProductId == productId);
        if (item is null)
        {
            return CartOperationResult.NotFound("Cart item not found");
        }

        item.Quantity = request.Quantity;
        cart!.UpdatedAt = DateTime.UtcNow;
        await data.SaveCartAsync(cart, cancellationToken);
        return CartOperationResult.Succeeded(Map(cart));
    }

    public async Task<CartOperationResult> RemoveItemAsync(Guid storeId, Guid productId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        var item = cart?.Items.FirstOrDefault(item => item.ProductId == productId);
        if (item is null)
        {
            return CartOperationResult.NotFound("Cart item not found");
        }

        cart!.Items.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;
        await data.SaveCartAsync(cart, cancellationToken);
        return CartOperationResult.Succeeded(Map(cart));
    }

    private async Task<(Customer Customer, UserContext Context)?> ResolveScopeAsync(Guid storeId, CancellationToken cancellationToken)
    {
        if (storeId == Guid.Empty)
        {
            return null;
        }

        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return null;
        }

        var customer = await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken);
        var store = await data.GetStoreAsync(storeId, cancellationToken);
        return customer is { IsActive: true } && store is { IsActive: true } && store.OrganizationId == context.OrganizationId
            ? (customer, context)
            : null;
    }

    private static CartItem CreateItem(Product product, int quantity) => new()
    {
        ProductId = product.Id,
        ProductNameSnapshot = product.Name,
        UnitPriceSnapshot = product.Price,
        Quantity = quantity
    };

    private static CartResponse Map(Cart cart) => new(
        cart.Id,
        cart.CustomerId,
        cart.StoreId,
        cart.CreatedAt,
        cart.UpdatedAt,
        cart.Items.Select(item => new CartItemResponse(item.ProductId, item.ProductNameSnapshot, item.UnitPriceSnapshot, item.Quantity, item.TotalPrice)).ToArray(),
        cart.Items.Sum(item => item.TotalPrice));
}