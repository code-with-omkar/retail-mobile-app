using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Saved products: a customer keeps a list to come back to; nobody else can see or change it.</summary>
public sealed class FavouriteTests
{
    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; } = context;
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class MemoryFavourites : IFavouriteStore
    {
        public List<(Guid Customer, Guid Product, DateTime At)> Rows { get; } = [];

        public Task<IReadOnlyList<Guid>> ListAsync(Guid customerId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Rows.Where(row => row.Customer == customerId).OrderByDescending(row => row.At).Take(take).Select(row => row.Product).ToArray());

        public Task<int> CountAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(Rows.Count(row => row.Customer == customerId));

        public Task<bool> AddAsync(Guid customerId, Guid productId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (Rows.Any(row => row.Customer == customerId && row.Product == productId))
            {
                return Task.FromResult(false);
            }

            Rows.Add((customerId, productId, nowUtc));
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.RemoveAll(row => row.Customer == customerId && row.Product == productId) > 0);
    }

    /// <summary>Knows the products of the seeded shop; a product can be taken off sale by id.</summary>
    private sealed class Catalogue(InMemoryCommerceStore data) : ICatalogService
    {
        public HashSet<Guid> OffSale { get; } = [];
        public Guid? StoreSeen { get; private set; }

        private CatalogProductResponse? Build(Guid id)
        {
            var product = data.Products.FirstOrDefault(item => item.Id == id && item.IsActive && !OffSale.Contains(item.Id));
            return product is null ? null : new CatalogProductResponse(product.Id, product.Name, product.Description, product.Price, product.Price, 0, product.UnitOfMeasure, product.CategoryId, product.ImageUrl, new Dictionary<string, CatalogTranslation>());
        }

        public Task<CatalogProductResponse?> GetCatalogProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Build(id));

        public Task<CatalogProductDetailResult> GetCatalogProductForStoreAsync(Guid id, Guid storeId, CancellationToken cancellationToken = default)
        {
            StoreSeen = storeId;
            if (data.Stores.All(store => store.Id != storeId))
            {
                return Task.FromResult(new CatalogProductDetailResult(CatalogQueryStatus.StoreNotFound));
            }

            var product = Build(id);
            return Task.FromResult(product is null ? new CatalogProductDetailResult(CatalogQueryStatus.NotFound) : new CatalogProductDetailResult(CatalogQueryStatus.Succeeded, product with { InStock = true }));
        }

        public Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogCategoryResponse>> GetCatalogCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<NearestStoreResult> FindNearestStoreForCustomerAsync(NearestStoreQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogCategoryListResult> GetCatalogCategoriesForStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceableStoreListResult> GetServiceableStoresAsync(NearestStoreQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogProductPageResult> GetCatalogProductsAsync(CatalogProductQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductResponse?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoreResponse>> GetStoresAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoreResponse?> FindNearestStoreAsync(double latitude, double longitude, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required MemoryFavourites Store { get; init; }
        public required Catalogue Catalogue { get; init; }
        public required FixedUser Who { get; init; }
        public required FavouriteService Service { get; init; }
        public required Customer Me { get; init; }
        public required TestClock Clock { get; init; }
        public required Guid Organization { get; init; }
    }

    private static Rig Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var organization = data.Organizations.Single().Id;
        var me = data.Customers.Single();
        var store = new MemoryFavourites();
        var catalogue = new Catalogue(data);
        var who = new FixedUser(new UserContext(me.UserId, organization, null, Role.Customer));
        var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.Zero));
        return new Rig { Data = data, Store = store, Catalogue = catalogue, Who = who, Me = me, Clock = clock, Organization = organization, Service = new FavouriteService(store, data, catalogue, who, clock) };
    }

    // ---------------- who may ----------------

    [Theory]
    [InlineData(Role.StoreStaff)]
    [InlineData(Role.Admin)]
    [InlineData(Role.DeliveryPartner)]
    public async Task Only_a_customer_has_a_list(Role role)
    {
        var rig = Create();
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, Guid.NewGuid(), role);
        var product = rig.Data.Products[0].Id;

        Assert.Equal(FavouriteStatus.Forbidden, (await rig.Service.ListAsync(null)).Status);
        Assert.Equal(FavouriteStatus.Forbidden, (await rig.Service.IdsAsync()).Status);
        Assert.Equal(FavouriteStatus.Forbidden, (await rig.Service.AddAsync(product)).Status);
        Assert.Equal(FavouriteStatus.Forbidden, (await rig.Service.RemoveAsync(product)).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Fact]
    public async Task Nobody_signed_in_and_a_user_without_a_customer_record_have_no_list()
    {
        var rig = Create();
        rig.Who.Context = null;
        Assert.Equal(FavouriteStatus.Forbidden, (await rig.Service.AddAsync(rig.Data.Products[0].Id)).Status);

        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, null, Role.Customer);
        Assert.Equal(FavouriteStatus.Forbidden, (await rig.Service.IdsAsync()).Status);
    }

    // ---------------- saving and removing ----------------

    [Fact]
    public async Task A_product_is_saved_once_however_often_it_is_saved()
    {
        var rig = Create();
        var id = rig.Data.Products[0].Id;

        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.AddAsync(id)).Status);
        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.AddAsync(id)).Status);

        Assert.Single(rig.Store.Rows);
        Assert.Equal([id], (await rig.Service.IdsAsync()).Value);
    }

    [Fact]
    public async Task A_product_that_does_not_exist_or_is_off_sale_cannot_be_saved()
    {
        var rig = Create();
        var off = rig.Data.Products[1];
        off.IsActive = false;

        Assert.Equal(FavouriteStatus.NotFound, (await rig.Service.AddAsync(Guid.NewGuid())).Status);
        Assert.Equal(FavouriteStatus.NotFound, (await rig.Service.AddAsync(off.Id)).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Fact]
    public async Task Removing_takes_it_off_and_removing_what_is_not_there_is_fine()
    {
        var rig = Create();
        var id = rig.Data.Products[0].Id;
        await rig.Service.AddAsync(id);

        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.RemoveAsync(id)).Status);
        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.RemoveAsync(id)).Status);
        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.RemoveAsync(Guid.NewGuid())).Status);
        Assert.Empty((await rig.Service.IdsAsync()).Value!);
    }

    [Fact]
    public async Task The_list_holds_at_most_two_hundred_and_a_saved_one_can_still_be_saved_again_at_the_limit()
    {
        var rig = Create();
        var kept = rig.Data.Products[0].Id;
        rig.Store.Rows.Add((rig.Me.Id, kept, rig.Clock.GetUtcNow().UtcDateTime));
        for (var i = 1; i < IFavouriteService.MaxFavourites; i++)
        {
            rig.Store.Rows.Add((rig.Me.Id, Guid.NewGuid(), rig.Clock.GetUtcNow().UtcDateTime.AddMinutes(-i)));
        }

        var other = rig.Data.Products[1].Id;
        var refused = await rig.Service.AddAsync(other);

        Assert.Equal(FavouriteStatus.LimitReached, refused.Status);
        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.AddAsync(kept)).Status);
        Assert.Equal(IFavouriteService.MaxFavourites, rig.Store.Rows.Count);
        await rig.Service.RemoveAsync(kept);
        Assert.Equal(FavouriteStatus.Succeeded, (await rig.Service.AddAsync(other)).Status);
    }

    [Fact]
    public async Task One_customers_list_is_not_anothers()
    {
        var rig = Create();
        var id = rig.Data.Products[0].Id;
        await rig.Service.AddAsync(id);
        var stranger = new Customer { UserId = Guid.NewGuid() };
        rig.Data.Customers.Add(stranger);
        rig.Who.Context = new UserContext(stranger.UserId, rig.Organization, null, Role.Customer);

        Assert.Empty((await rig.Service.IdsAsync()).Value!);
        await rig.Service.RemoveAsync(id);
        rig.Who.Context = new UserContext(rig.Me.UserId, rig.Organization, null, Role.Customer);
        Assert.Equal([id], (await rig.Service.IdsAsync()).Value);
    }

    // ---------------- reading the list ----------------

    [Fact]
    public async Task The_list_shows_the_newest_saved_first()
    {
        var rig = Create();
        var first = rig.Data.Products[0].Id;
        var second = rig.Data.Products[1].Id;
        await rig.Service.AddAsync(first);
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        await rig.Service.AddAsync(second);

        var list = (await rig.Service.ListAsync(null)).Value!;

        Assert.Equal([second, first], list.Select(product => product.Id));
    }

    [Fact]
    public async Task A_product_taken_off_sale_drops_out_of_the_list_but_comes_back_with_it()
    {
        var rig = Create();
        var id = rig.Data.Products[0].Id;
        await rig.Service.AddAsync(id);

        rig.Catalogue.OffSale.Add(id);
        Assert.Empty((await rig.Service.ListAsync(null)).Value!);
        Assert.Equal([id], (await rig.Service.IdsAsync()).Value);

        rig.Catalogue.OffSale.Clear();
        Assert.Single((await rig.Service.ListAsync(null)).Value!);
    }

    [Fact]
    public async Task With_a_store_the_products_carry_that_stores_stock_and_an_unknown_store_is_not_found()
    {
        var rig = Create();
        await rig.Service.AddAsync(rig.Data.Products[0].Id);
        var store = rig.Data.Stores[0].Id;

        var list = (await rig.Service.ListAsync(store)).Value!;

        Assert.Equal(store, rig.Catalogue.StoreSeen);
        Assert.True(list.Single().InStock);
        Assert.Equal(FavouriteStatus.StoreNotFound, (await rig.Service.ListAsync(Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task The_list_shows_at_most_sixty_but_the_ids_cover_all_of_them()
    {
        var rig = Create();
        var now = rig.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 70; i++)
        {
            var product = new Product { Sku = "FAV-" + i, Name = "P" + i, Description = "d", Price = 10, UnitOfMeasure = "1", CategoryId = rig.Data.Categories[0].Id };
            rig.Data.Products.Add(product);
            rig.Store.Rows.Add((rig.Me.Id, product.Id, now.AddMinutes(i)));
        }

        Assert.Equal(60, (await rig.Service.ListAsync(null)).Value!.Count);
        Assert.Equal(70, (await rig.Service.IdsAsync()).Value!.Count);
    }
}

[Collection("SqlServer")]
public sealed class FavouriteEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    [Fact]
    public async Task Saving_lists_newest_first_and_removing_work_on_sql()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 5);
        Guid customer, other;
        await using (var db = world.Db())
        {
            customer = await db.Customers.Where(item => item.User.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
            other = await db.Customers.Where(item => item.User.ExternalSubject == world.Emails[1]).Select(item => item.Id).SingleAsync();
        }

        var second = new Product { Sku = "F2" + world.Token, Name = "Second", Description = "d", Price = 5, UnitOfMeasure = "1", CategoryId = world.Category.Id };
        try
        {
            await using (var db = world.Db())
            {
                db.Products.Add(second);
                await db.SaveChangesAsync();
            }

            var t0 = DateTime.UtcNow;
            await using (var db = world.Db())
            {
                var store = new EfFavouriteStore(db);
                Assert.True(await store.AddAsync(customer, world.Product.Id, t0));
                Assert.False(await store.AddAsync(customer, world.Product.Id, t0.AddMinutes(1)));
                Assert.True(await store.AddAsync(customer, second.Id, t0.AddMinutes(2)));
                Assert.True(await store.AddAsync(other, world.Product.Id, t0));
            }

            await using (var db = world.Db())
            {
                var store = new EfFavouriteStore(db);
                Assert.Equal([second.Id, world.Product.Id], await store.ListAsync(customer, 10));
                Assert.Equal([second.Id], await store.ListAsync(customer, 1));
                Assert.Equal(2, await store.CountAsync(customer));
                Assert.Equal([world.Product.Id], await store.ListAsync(other, 10));
                Assert.True(await store.RemoveAsync(customer, world.Product.Id));
                Assert.False(await store.RemoveAsync(customer, world.Product.Id));
                Assert.Equal([second.Id], await store.ListAsync(customer, 10));
                Assert.Equal([world.Product.Id], await store.ListAsync(other, 10));
            }
        }
        finally
        {
            await using var db = world.Db();
            await db.Favourites.Where(item => item.ProductId == second.Id || item.ProductId == world.Product.Id).ExecuteDeleteAsync();
            await db.Products.Where(item => item.Id == second.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Saving_the_same_product_at_the_same_moment_makes_one_row_and_a_made_up_product_is_refused()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        Guid customer;
        await using (var db = world.Db())
        {
            customer = await db.Customers.Where(item => item.User.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
        }

        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var db = world.Db();
                return await new EfFavouriteStore(db).AddAsync(customer, world.Product.Id, DateTime.UtcNow);
            }));
            Assert.Equal(1, results.Count(added => added));

            await using var verify = world.Db();
            Assert.Equal(1, await verify.Favourites.CountAsync(item => item.CustomerId == customer));
            await Assert.ThrowsAsync<DbUpdateException>(() => new EfFavouriteStore(verify).AddAsync(customer, Guid.NewGuid(), DateTime.UtcNow));
        }
        finally
        {
            await using var db = world.Db();
            await db.Favourites.Where(item => item.CustomerId == customer).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Deleting_the_account_erases_the_list_and_leaves_other_customers_lists()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 5);
        Guid user, customer, other;
        await using (var db = world.Db())
        {
            user = await db.Users.Where(item => item.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
            customer = await db.Customers.Where(item => item.UserId == user).Select(item => item.Id).SingleAsync();
            other = await db.Customers.Where(item => item.User.ExternalSubject == world.Emails[1]).Select(item => item.Id).SingleAsync();
            db.Favourites.AddRange(new FavouriteProduct { CustomerId = customer, ProductId = world.Product.Id }, new FavouriteProduct { CustomerId = other, ProductId = world.Product.Id });
            await db.SaveChangesAsync();
        }

        try
        {
            await using (var db = world.Db())
            {
                Assert.Equal(DeleteOutcome.Deleted, await new EfAccountStore(db).DeleteCustomerAsync(user, DateTime.UtcNow));
            }

            await using var verify = world.Db();
            Assert.False(await verify.Favourites.AnyAsync(item => item.CustomerId == customer));
            Assert.True(await verify.Favourites.AnyAsync(item => item.CustomerId == other));
        }
        finally
        {
            await using var db = world.Db();
            await db.Favourites.Where(item => item.ProductId == world.Product.Id).ExecuteDeleteAsync();
        }
    }
}
