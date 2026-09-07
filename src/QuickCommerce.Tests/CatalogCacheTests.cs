using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using QuickCommerce.Application;
using QuickCommerce.Application.Caching;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Caching;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CatalogCacheTests
{
    [Fact]
    public async Task Catalog_reads_use_cached_categories_products_details_and_stores()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var cache = new MemoryCacheService();
        var service = new CatalogService(data, new StoreSelectionService(), cache);

        var categories = await service.GetCategoriesAsync();
        var products = await service.GetProductsAsync("tom", null);
        var product = await service.GetProductAsync(data.Products.First().Id);
        var stores = await service.GetStoresAsync();
        data.Products.Clear();
        data.Categories.Clear();
        data.Stores.Clear();

        Assert.Equal(4, categories.Count);
        Assert.Single(products);
        Assert.NotNull(product);
        Assert.Equal(3, stores.Count);
        Assert.Equal(4, cache.SetCalls);
        Assert.Equal(4, cache.GetCalls);
    }

    [Fact]
    public void Catalog_keys_are_stable_and_distinguish_query_shapes()
    {
        Assert.Equal(CatalogCacheKeys.Products(" Milk ", null), CatalogCacheKeys.Products("milk", null));
        Assert.NotEqual(CatalogCacheKeys.Products("milk", null), CatalogCacheKeys.Products("milk", Guid.NewGuid()));
        Assert.Equal("quickcommerce:catalog:product:v1:00000000-0000-0000-0000-000000000001", CatalogCacheKeys.Product(Guid.Parse("00000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public async Task Redis_adapter_falls_back_when_distributed_cache_is_unavailable()
    {
        var cache = new DistributedCacheService(new ThrowingDistributedCache(), NullLogger<DistributedCacheService>.Instance);

        var value = await cache.GetAsync<ProductResponse>("catalog:test");
        await cache.SetAsync("catalog:test", new ProductResponse(Guid.NewGuid(), "sku", "name", "description", 1, "each", Guid.NewGuid(), null, true), TimeSpan.FromMinutes(1));
        await cache.RemoveAsync("catalog:test");

        Assert.Null(value);
    }

    private sealed class MemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object> values = [];
        public int GetCalls { get; private set; }
        public int SetCalls { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(values.TryGetValue(key, out var value) ? (T?)value : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            SetCalls++;
            values[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis unavailable");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable");
        public void Refresh(string key) => throw new InvalidOperationException("Redis unavailable");
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable");
        public void Remove(string key) => throw new InvalidOperationException("Redis unavailable");
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("Redis unavailable");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable");
    }
}