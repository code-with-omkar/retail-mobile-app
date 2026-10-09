using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed class CartService(
    ICommerceStore data,
    ICurrentUserContextResolver currentUserContextResolver,
    IValidator<AddCartItemRequest> addItemValidator,
    IValidator<UpdateCartItemRequest> updateItemValidator,
    PricingSettings? pricingSettings = null) : ICartService
{
    public async Task<CartResponse?> GetCartAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return null;
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        return cart is null ? null : await MapAsync(cart, cancellationToken);
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

        var variant = await ResolveVariantAsync(product.Id, request.VariantId, cancellationToken);
        if (variant is null)
        {
            return CartOperationResult.NotFound(request.VariantId.HasValue ? "Variant not found" : "Product has no sellable variant");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { CustomerId = scope.Value.Customer.Id, StoreId = storeId };
            cart.Items.Add(CreateItem(product, variant, request.Quantity));
            await data.AddCartAsync(cart, cancellationToken);
        }
        else
        {
            var item = cart.Items.FirstOrDefault(item => item.VariantId == variant.Id);
            if (item is null)
            {
                cart.Items.Add(CreateItem(product, variant, request.Quantity));
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

        return CartOperationResult.Succeeded(await MapAsync(cart, cancellationToken));
    }

    public async Task<CartOperationResult> UpdateItemAsync(Guid storeId, Guid productId, UpdateCartItemRequest request, Guid? variantId = null, CancellationToken cancellationToken = default)
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
        var item = cart is null ? null : await FindLineAsync(cart, productId, variantId, cancellationToken);
        if (item is null)
        {
            return CartOperationResult.NotFound("Cart item not found");
        }

        item.Quantity = request.Quantity;
        cart!.UpdatedAt = DateTime.UtcNow;
        await data.SaveCartAsync(cart, cancellationToken);
        return CartOperationResult.Succeeded(await MapAsync(cart, cancellationToken));
    }

    public async Task<CartOperationResult> RemoveItemAsync(Guid storeId, Guid productId, Guid? variantId = null, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        var item = cart is null ? null : await FindLineAsync(cart, productId, variantId, cancellationToken);
        if (item is null)
        {
            return CartOperationResult.NotFound("Cart item not found");
        }

        cart!.Items.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;
        await data.SaveCartAsync(cart, cancellationToken);
        return CartOperationResult.Succeeded(await MapAsync(cart, cancellationToken));
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

    /// <summary>The requested variant of the product (it must belong to it and be active), or its default variant when none was named.</summary>
    private async Task<ProductVariant?> ResolveVariantAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken)
    {
        var variants = (await data.GetVariantsAsync([productId], cancellationToken)).Where(variant => variant.IsActive);
        return variantId.HasValue ? variants.FirstOrDefault(variant => variant.Id == variantId.Value) : variants.FirstOrDefault(variant => variant.IsDefault);
    }

    /// <summary>
    /// A client written before variants names only the product: that is its only line of the product, or the default variant's line
    /// when the cart holds several pack sizes of it.
    /// </summary>
    private async Task<CartItem?> FindLineAsync(Cart cart, Guid productId, Guid? variantId, CancellationToken cancellationToken)
    {
        var lines = cart.Items.Where(item => item.ProductId == productId).ToArray();
        if (variantId.HasValue)
        {
            return lines.FirstOrDefault(item => item.VariantId == variantId.Value);
        }

        if (lines.Length <= 1)
        {
            return lines.FirstOrDefault();
        }

        var defaultVariant = (await data.GetVariantsAsync([productId], cancellationToken)).FirstOrDefault(variant => variant.IsDefault);
        return lines.FirstOrDefault(item => item.VariantId == defaultVariant?.Id);
    }

    private static CartItem CreateItem(Product product, ProductVariant variant, int quantity) => new()
    {
        ProductId = product.Id,
        VariantId = variant.Id,
        ProductNameSnapshot = product.Name,
        VariantLabelSnapshot = variant.Label,
        UnitPriceSnapshot = variant.Price,
        Quantity = quantity
    };

    private readonly PricingSettings pricing = pricingSettings ?? new PricingSettings();

    public async Task<CartResponse?> GetCurrentCartAsync(CancellationToken cancellationToken = default)
    {
        var customer = await ResolveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var cart = await data.GetCurrentCartAsync(customer.Value.Customer.Id, cancellationToken);
        if (cart is null)
        {
            return null;
        }

        // A cart in a store the customer's organisation no longer has is not offered back.
        var store = await data.GetStoreAsync(cart.StoreId, cancellationToken);
        return store is { IsActive: true } && store.OrganizationId == customer.Value.Context.OrganizationId ? await MapAsync(cart, cancellationToken) : null;
    }

    public async Task<CartOperationResult> ClearAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        await data.DeleteCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        return CartOperationResult.Succeeded(Empty(scope.Value.Customer.Id, storeId));
    }

    public async Task<CartOperationResult> MergeAsync(Guid storeId, MergeCartRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Count > MaxMergeLines)
        {
            return CartOperationResult.Invalid($"Send between 1 and {MaxMergeLines} items");
        }

        foreach (var line in request.Items)
        {
            var validation = await addItemValidator.ValidateAsync(line, cancellationToken);
            if (!validation.IsValid)
            {
                return CartOperationResult.Invalid(string.Join("; ", validation.Errors.Select(error => error.ErrorMessage)));
            }
        }

        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        var isNew = cart is null;
        cart ??= new Cart { CustomerId = scope.Value.Customer.Id, StoreId = storeId };

        // The same pack asked for twice is one line.
        var wanted = new List<(Product Product, ProductVariant Variant, int Quantity)>();
        var notes = new List<CartNote>();
        foreach (var line in request.Items)
        {
            var product = await data.GetProductAsync(line.ProductId, cancellationToken);
            var variant = product is { IsActive: true } ? await ResolveVariantAsync(product.Id, line.VariantId, cancellationToken) : null;
            if (product is null || variant is null)
            {
                notes.Add(new CartNote(line.ProductId, line.VariantId, product?.Name ?? string.Empty, null, CartNoteKinds.Unavailable, line.Quantity));
                continue;
            }

            var at = wanted.FindIndex(item => item.Variant.Id == variant.Id);
            if (at >= 0)
            {
                wanted[at] = (product, variant, wanted[at].Quantity + line.Quantity);
            }
            else
            {
                wanted.Add((product, variant, line.Quantity));
            }
        }

        var stock = (await data.GetStoreVariantInventoryAsync(storeId, wanted.Select(item => item.Variant.Id).ToArray(), cancellationToken))
            .ToDictionary(row => row.VariantId, row => row.AvailableQuantity);
        foreach (var (product, variant, quantity) in wanted)
        {
            var existing = cart.Items.FirstOrDefault(item => item.VariantId == variant.Id);
            var have = existing?.Quantity ?? 0;
            var room = Math.Min(MaxLineQuantity, stock.GetValueOrDefault(variant.Id)) - have;
            var add = Math.Min(quantity, room);
            if (add <= 0)
            {
                notes.Add(new CartNote(product.Id, variant.Id, product.Name, variant.Label, CartNoteKinds.OutOfStock, quantity));
                continue;
            }

            if (add < quantity)
            {
                notes.Add(new CartNote(product.Id, variant.Id, product.Name, variant.Label, CartNoteKinds.Reduced, add));
            }

            if (existing is null)
            {
                cart.Items.Add(CreateItem(product, variant, add));
            }
            else
            {
                existing.Quantity += add;
            }
        }

        cart.UpdatedAt = DateTime.UtcNow;
        if (isNew)
        {
            await data.AddCartAsync(cart, cancellationToken);
        }
        else
        {
            await data.SaveCartAsync(cart, cancellationToken);
        }

        return CartOperationResult.Succeeded(await MapAsync(cart, cancellationToken), notes);
    }

    public async Task<CartOperationResult> RepriceAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(storeId, cancellationToken);
        if (scope is null)
        {
            return CartOperationResult.Unauthorized("The authenticated user cannot access this store");
        }

        var cart = await data.GetCartAsync(scope.Value.Customer.Id, storeId, cancellationToken);
        if (cart is null)
        {
            return CartOperationResult.NotFound("Cart not found");
        }

        foreach (var item in cart.Items)
        {
            var product = await data.GetProductAsync(item.ProductId, cancellationToken);
            var variant = product is { IsActive: true } ? (await data.GetVariantsAsync([item.ProductId], cancellationToken)).FirstOrDefault(v => v.Id == item.VariantId && v.IsActive) : null;
            if (variant is null)
            {
                continue; // a line that is gone stays, marked unavailable, for the customer to remove
            }

            item.UnitPriceSnapshot = variant.Price;
            item.ProductNameSnapshot = product!.Name;
            item.VariantLabelSnapshot = variant.Label;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await data.SaveCartAsync(cart, cancellationToken);
        return CartOperationResult.Succeeded(await MapAsync(cart, cancellationToken));
    }

    private const int MaxMergeLines = 100;
    private const int MaxLineQuantity = 1000;

    /// <summary>The customer behind the token, without needing a store.</summary>
    private async Task<(Customer Customer, UserContext Context)?> ResolveCustomerAsync(CancellationToken cancellationToken)
    {
        var context = await currentUserContextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return null;
        }

        var customer = await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken);
        return customer is { IsActive: true } ? (customer, context) : null;
    }

    private CartResponse Empty(Guid customerId, Guid storeId)
    {
        var price = PricingCalculator.Compute(pricing, 0);
        return new CartResponse(Guid.Empty, customerId, storeId, DateTime.UtcNow, DateTime.UtcNow, [], 0, 0, 0, 0, 0, price.FreeDeliveryThreshold, 0);
    }

    /// <summary>The cart with fees from the settings and, per line, what the store can supply and what the shop charges now.</summary>
    private async Task<CartResponse> MapAsync(Cart cart, CancellationToken cancellationToken)
    {
        var productIds = cart.Items.Select(item => item.ProductId).Distinct().ToArray();
        var variants = productIds.Length == 0 ? [] : (await data.GetVariantsAsync(productIds, cancellationToken)).ToDictionary(variant => variant.Id);
        var stock = cart.Items.Count == 0
            ? []
            : (await data.GetStoreVariantInventoryAsync(cart.StoreId, cart.Items.Select(item => item.VariantId).Distinct().ToArray(), cancellationToken))
                .ToDictionary(row => row.VariantId, row => row.AvailableQuantity);
        var activeProducts = new Dictionary<Guid, bool>();
        foreach (var productId in productIds)
        {
            activeProducts[productId] = (await data.GetProductAsync(productId, cancellationToken)) is { IsActive: true };
        }

        var items = cart.Items.Select(item =>
        {
            var variant = variants.GetValueOrDefault(item.VariantId);
            var sellable = variant is { IsActive: true } && activeProducts.GetValueOrDefault(item.ProductId);
            return new CartItemResponse(
                item.ProductId, item.ProductNameSnapshot, item.UnitPriceSnapshot, item.Quantity, item.TotalPrice, item.VariantId, item.VariantLabelSnapshot,
                Available: stock.GetValueOrDefault(item.VariantId),
                Unavailable: !sellable,
                CurrentUnitPrice: sellable ? variant!.Price : null);
        }).ToArray();

        var subtotal = items.Sum(item => item.TotalPrice);
        var price = PricingCalculator.Compute(pricing, subtotal);
        return new CartResponse(
            cart.Id, cart.CustomerId, cart.StoreId, cart.CreatedAt, cart.UpdatedAt, items, subtotal,
            price.Subtotal, price.DeliveryFee, price.HandlingFee, price.Total, price.FreeDeliveryThreshold, price.AmountToFreeDelivery);
    }
}
