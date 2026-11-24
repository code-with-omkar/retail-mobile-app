using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Sub-categories: administrators put one category under another, and a top-level category lists its sub-categories' products too.</summary>
public sealed class CategoryAdminTests
{
    private sealed class FakeStore : ICategoryAdminStore
    {
        public List<Category> Items { get; } = [];

        public Task<IReadOnlyList<(Category Category, int ProductCount)>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(Category, int)>>(Items.Select(category => (category, 0)).ToArray());

        public Task AddAsync(Category category, CancellationToken cancellationToken = default)
        {
            Items.Add(category);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Guid id, string name, Guid? parentCategoryId, bool isActive, CancellationToken cancellationToken = default)
        {
            var category = Items.FirstOrDefault(item => item.Id == id);
            if (category is null)
            {
                return Task.FromResult(false);
            }

            category.Name = name;
            category.ParentCategoryId = parentCategoryId;
            category.IsActive = isActive;
            return Task.FromResult(true);
        }
    }

    private sealed class Who(Role? role) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UserContext?>(role is null ? null : new UserContext(Guid.NewGuid(), Guid.NewGuid(), null, role.Value));
    }

    private static (CategoryAdminService Service, FakeStore Store) Make(Role? role = Role.Admin)
    {
        var store = new FakeStore();
        return (new CategoryAdminService(store, new Who(role)), store);
    }

    [Fact]
    public async Task An_administrator_adds_a_category_and_a_sub_category_under_it()
    {
        var (service, store) = Make();
        var top = (await service.CreateAsync(new SaveCategoryRequest("  Dairy  "))).Value!;
        var sub = await service.CreateAsync(new SaveCategoryRequest("Milk", top.Id));

        Assert.Equal("Dairy", top.Name);
        Assert.Equal(CatalogAdminStatus.Succeeded, sub.Status);
        Assert.Equal(top.Id, store.Items.Single(category => category.Name == "Milk").ParentCategoryId);
    }

    [Fact]
    public async Task Only_an_administrator_may_change_categories()
    {
        foreach (var role in new Role?[] { Role.StoreStaff, Role.Customer, Role.DeliveryPartner, null })
        {
            var (service, _) = Make(role);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await service.CreateAsync(new SaveCategoryRequest("X"))).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await service.ListAsync()).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await service.UpdateAsync(Guid.NewGuid(), new SaveCategoryRequest("X"))).Status);
        }
    }

    [Fact]
    public async Task The_tree_is_two_levels_deep_with_no_loops_and_no_repeated_names()
    {
        var (service, _) = Make();
        var top = (await service.CreateAsync(new SaveCategoryRequest("Dairy"))).Value!;
        var sub = (await service.CreateAsync(new SaveCategoryRequest("Milk", top.Id))).Value!;
        var other = (await service.CreateAsync(new SaveCategoryRequest("Snacks"))).Value!;

        Assert.Equal(CatalogAdminStatus.Invalid, (await service.CreateAsync(new SaveCategoryRequest("Skimmed", sub.Id))).Status); // under a sub-category
        Assert.Equal(CatalogAdminStatus.Invalid, (await service.UpdateAsync(top.Id, new SaveCategoryRequest("Dairy", top.Id))).Status); // its own parent
        Assert.Equal(CatalogAdminStatus.Invalid, (await service.UpdateAsync(top.Id, new SaveCategoryRequest("Dairy", other.Id))).Status); // has children, cannot become one
        Assert.Equal(CatalogAdminStatus.Invalid, (await service.CreateAsync(new SaveCategoryRequest("Cheese", Guid.NewGuid()))).Status); // unknown parent
        Assert.Equal(CatalogAdminStatus.Conflict, (await service.CreateAsync(new SaveCategoryRequest("milk", top.Id))).Status); // same name beside it
        Assert.Equal(CatalogAdminStatus.Succeeded, (await service.CreateAsync(new SaveCategoryRequest("Milk", other.Id))).Status); // same name elsewhere is fine
    }

    [Fact]
    public async Task A_name_is_required_and_short_and_an_unknown_category_is_not_found()
    {
        var (service, _) = Make();
        Assert.Equal(CatalogAdminStatus.Invalid, (await service.CreateAsync(new SaveCategoryRequest("   "))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await service.CreateAsync(new SaveCategoryRequest(new string('a', 61)))).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await service.UpdateAsync(Guid.NewGuid(), new SaveCategoryRequest("Fine"))).Status);
    }

    [Fact]
    public async Task A_category_can_be_moved_under_another_and_back_to_the_top_or_switched_off()
    {
        var (service, store) = Make();
        var top = (await service.CreateAsync(new SaveCategoryRequest("Dairy"))).Value!;
        var loose = (await service.CreateAsync(new SaveCategoryRequest("Milk"))).Value!;

        Assert.Equal(CatalogAdminStatus.Succeeded, (await service.UpdateAsync(loose.Id, new SaveCategoryRequest("Milk", top.Id))).Status);
        Assert.Equal(top.Id, store.Items.Single(category => category.Id == loose.Id).ParentCategoryId);

        Assert.Equal(CatalogAdminStatus.Succeeded, (await service.UpdateAsync(loose.Id, new SaveCategoryRequest("Milk", null, false))).Status);
        var moved = store.Items.Single(category => category.Id == loose.Id);
        Assert.Null(moved.ParentCategoryId);
        Assert.False(moved.IsActive);
    }

    [Fact]
    public async Task A_top_level_category_lists_its_sub_categories_products_and_a_sub_category_only_its_own()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var dairy = data.Categories.First(category => category.Name == "Dairy");
        var milk = new Category { Name = "Milk", ParentCategoryId = dairy.Id };
        var curd = new Category { Name = "Curd", ParentCategoryId = dairy.Id };
        data.Categories.AddRange([milk, curd]);
        var farmMilk = data.Products.First(product => product.Sku == "DAI-MILK");
        farmMilk.CategoryId = milk.Id;

        var (underTop, topCount) = await data.GetActiveProductPageAsync(null, dairy.Id, 0, 50);
        var (underMilk, _) = await data.GetActiveProductPageAsync(null, milk.Id, 0, 50);
        var (underCurd, curdCount) = await data.GetActiveProductPageAsync(null, curd.Id, 0, 50);

        Assert.Contains(underTop, product => product.Id == farmMilk.Id);
        Assert.Equal(1, topCount);
        Assert.Equal([farmMilk.Id], underMilk.Select(product => product.Id));
        Assert.Empty(underCurd);
        Assert.Equal(0, curdCount);
    }
}
