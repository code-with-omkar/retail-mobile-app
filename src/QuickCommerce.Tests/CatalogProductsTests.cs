using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Api.Security;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CatalogProductsTests
{
    private static (CatalogService Service, InMemoryCommerceStore Data, CountingCache Cache) Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var cache = new CountingCache();
        return (new CatalogService(data, new StoreSelectionService(), cache), data, cache);
    }

    private static async Task<PagedResult<CatalogProductResponse>> Page(CatalogService service, string? search = null, Guid? categoryId = null, int page = 1, int pageSize = 20)
    {
        var result = await service.GetCatalogProductsAsync(new CatalogProductQuery(search, categoryId, page, pageSize));
        Assert.Equal(CatalogQueryStatus.Succeeded, result.Status);
        return result.Page!;
    }

    [Fact]
    public async Task Lists_only_active_products_in_stable_name_order()
    {
        var (service, data, _) = Create();
        data.Products.Add(new Product { Sku = "OLD-1", Name = "Aardvark Retired", Description = "inactive", Price = 1, UnitOfMeasure = "each", CategoryId = data.Categories.First().Id, IsActive = false });

        var page = await Page(service);

        Assert.Equal(5, page.TotalCount);
        Assert.DoesNotContain(page.Items, item => item.Name == "Aardvark Retired");
        Assert.Equal(page.Items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Id), page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Pages_do_not_overlap_and_report_totals()
    {
        var (service, _, _) = Create();

        var first = await Page(service, page: 1, pageSize: 2);
        var second = await Page(service, page: 2, pageSize: 2);
        var third = await Page(service, page: 3, pageSize: 2);
        var beyond = await Page(service, page: 4, pageSize: 2);

        Assert.Equal([2, 2, 1, 0], new[] { first.Items.Count, second.Items.Count, third.Items.Count, beyond.Items.Count });
        Assert.All(new[] { first, second, third }, page => Assert.Equal(5, page.TotalCount));
        Assert.Equal(5, first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).Distinct().Count());
        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(third.HasMore);
    }

    [Fact]
    public async Task Filters_by_search_text_and_category()
    {
        var (service, data, _) = Create();
        var dairy = data.Products.Single(product => product.Name == "Farm Milk").CategoryId;

        var bySearch = await Page(service, search: "  TOMATO ");
        var byCategory = await Page(service, categoryId: dairy);
        var none = await Page(service, search: "no such product");

        Assert.Equal(["Tomato"], bySearch.Items.Select(item => item.Name));
        Assert.Equal(["Farm Milk"], byCategory.Items.Select(item => item.Name));
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, "Page")]
    [InlineData(-1, 20, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 51, "PageSize")]
    public async Task Rejects_invalid_paging(int page, int pageSize, string expectedMessagePart)
    {
        var (service, _, _) = Create();

        var result = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, null, page, pageSize));

        Assert.Equal(CatalogQueryStatus.InvalidRequest, result.Status);
        Assert.Contains(expectedMessagePart, result.Message);
        Assert.Null(result.Page);
    }

    [Fact]
    public async Task Rejects_overlong_search_and_empty_category_id()
    {
        var (service, _, _) = Create();

        var longSearch = await service.GetCatalogProductsAsync(new CatalogProductQuery(new string('a', CatalogProductQuery.MaxSearchLength + 1), null));
        var emptyCategory = await service.GetCatalogProductsAsync(new CatalogProductQuery(null, Guid.Empty));
        var maxLength = await service.GetCatalogProductsAsync(new CatalogProductQuery(new string('a', CatalogProductQuery.MaxSearchLength), null));

        Assert.Equal(CatalogQueryStatus.InvalidRequest, longSearch.Status);
        Assert.Equal(CatalogQueryStatus.InvalidRequest, emptyCategory.Status);
        Assert.Equal(CatalogQueryStatus.Succeeded, maxLength.Status);
    }

    [Fact]
    public async Task Caches_browse_pages_but_not_free_text_searches()
    {
        var (service, data, cache) = Create();

        var first = await Page(service, page: 1, pageSize: 3);
        data.Products.Clear();
        var again = await Page(service, page: 1, pageSize: 3);
        var searched = await Page(service, search: "tomato");
        var searchedAgain = await Page(service, search: "tomato");

        Assert.Equal(first.Items.Select(item => item.Id), again.Items.Select(item => item.Id));
        Assert.Equal(1, cache.SetCalls);
        Assert.Empty(searched.Items);
        Assert.Empty(searchedAgain.Items);
    }

    [Fact]
    public void Customer_response_does_not_expose_admin_or_internal_fields()
    {
        var names = typeof(CatalogProductResponse).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Sku", names);
        Assert.DoesNotContain("IsActive", names);
        Assert.Equal(["CategoryId", "Description", "DiscountPercent", "Id", "ImageUrl", "InStock", "LowStock", "Mrp", "Name", "Price", "Translations", "UnitOfMeasure", "Variants"], names.Order().ToArray());
    }

    [Fact]
    public void Public_route_is_anonymous_and_the_admin_product_list_stays_protected()
    {
        var publicAction = typeof(CatalogController).GetMethod(nameof(CatalogController.CatalogProducts))!;
        var adminAction = typeof(CatalogController).GetMethod(nameof(CatalogController.Products))!;

        Assert.Equal("catalog/products", publicAction.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Empty(publicAction.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Empty(typeof(CatalogController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(SecurityPolicies.AdminDataRead, adminAction.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    private sealed class CountingCache : ICacheService
    {
        private readonly Dictionary<string, object> values = [];
        public int SetCalls { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
        {
            SetCalls++;
            values[key!] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
