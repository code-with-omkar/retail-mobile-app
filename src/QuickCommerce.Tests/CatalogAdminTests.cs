using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Caching;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Media;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P16: administrators edit the shared catalogue (details, Marathi text, sizes, picture); store staff set their own store's stock.</summary>
public sealed class CatalogAdminTests
{
    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; } = context;
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class MemoryImages : IImageStore
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Task<string> SaveAsync(string name, string extension, byte[] content, CancellationToken cancellationToken = default)
        {
            var path = $"/media/products/{name}.{extension}";
            Files[path] = content;
            return Task.FromResult(path);
        }

        public Task<string> SaveAsync(string folder, string name, string extension, byte[] content, CancellationToken cancellationToken = default)
        {
            var path = $"/media/{folder}/{name}.{extension}";
            Files[path] = content;
            return Task.FromResult(path);
        }

        public Task DeleteAsync(string? publicPath, CancellationToken cancellationToken = default)
        {
            if (publicPath is not null)
            {
                Files.Remove(publicPath);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DictionaryCache : ICacheService
    {
        public Dictionary<string, object> Items { get; } = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(Items.TryGetValue(key, out var value) ? (T?)value : default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) { Items[key] = value!; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { Items.Remove(key); return Task.CompletedTask; }
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required FixedUser Who { get; init; }
        public required MemoryImages Images { get; init; }
        public required DictionaryCache Cache { get; init; }
        public required Guid Organization { get; init; }
        public required Store A { get; init; }
        public required Store B { get; init; }
        public required Category Veg { get; init; }
        public required Category Dairy { get; init; }
        public required CatalogAdminService Catalogue { get; init; }
        public required StockService Stock { get; init; }

        public void SignInAdmin(Role role = Role.Admin) => Who.Context = new UserContext(Guid.NewGuid(), Organization, null, role);
        public void SignInStaff(Store? store) => Who.Context = new UserContext(Guid.NewGuid(), Organization, store?.Id, Role.StoreStaff);
    }

    private static Rig Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var who = new FixedUser(null);
        var images = new MemoryImages();
        var cache = new DictionaryCache();
        var rig = new Rig
        {
            Data = data,
            Who = who,
            Images = images,
            Cache = cache,
            Organization = data.Organizations.Single().Id,
            A = data.Stores[0],
            B = data.Stores[1],
            Veg = data.Categories.First(category => category.Name == "Vegetables"),
            Dairy = data.Categories.First(category => category.Name == "Dairy"),
            Catalogue = new CatalogAdminService(data, who, images, cache),
            Stock = new StockService(data, who)
        };
        rig.SignInAdmin();
        return rig;
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 4, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 0, 0];

    private static async Task<AdminProductResponse> New(Rig rig, string sku = "NEW-1", decimal price = 40, decimal? mrp = 50, Guid? category = null)
    {
        var result = await rig.Catalogue.CreateAsync(new CreateProductRequest(sku, "Spinach", "Fresh palak", category ?? rig.Veg.Id, "250 g", price, mrp));
        Assert.Equal(CatalogAdminStatus.Succeeded, result.Status);
        return result.Value!;
    }

    private static async Task<CatalogAdminResult<AdminProductResponse>> Upload(Rig rig, Guid id, byte[] bytes, long? length = null)
    {
        await using var stream = new MemoryStream(bytes);
        return await rig.Catalogue.SetImageAsync(id, stream, length ?? bytes.Length);
    }

    // ---------------- who may do what ----------------

    [Fact]
    public async Task Only_administrators_can_change_the_catalogue()
    {
        var rig = Create();
        var product = await New(rig);

        foreach (var signIn in new Action[] { () => rig.SignInStaff(rig.A), () => rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, null, Role.Customer), () => rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, rig.A.Id, Role.DeliveryPartner), () => rig.Who.Context = null })
        {
            signIn();
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.ListAsync(null, 1, 20)).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.GetAsync(product.Id)).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.CreateAsync(new CreateProductRequest("X-1", "X", "", rig.Veg.Id, "1 kg", 10))).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.UpdateAsync(product.Id, new UpdateProductRequest("Hack", "", rig.Veg.Id, "1 kg", true))).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.SaveTranslationAsync(product.Id, "mr", new SaveTranslationRequest("x"))).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("V-1", "1 kg", 5, null))).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.MakeDefaultAsync(product.Id, product.Variants[0].Id)).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await Upload(rig, product.Id, Jpeg)).Status);
            Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Catalogue.RemoveImageAsync(product.Id)).Status);
        }

        rig.SignInAdmin();
        Assert.Equal("Spinach", (await rig.Catalogue.GetAsync(product.Id)).Value!.Name);
        rig.SignInAdmin(Role.ApplicationAdmin);
        Assert.Equal(CatalogAdminStatus.Succeeded, (await rig.Catalogue.ListAsync(null, 1, 20)).Status);
    }

    // ---------------- creating and editing ----------------

    [Fact]
    public async Task A_new_product_gets_its_first_size_with_the_unit_and_price()
    {
        var rig = Create();

        var product = await New(rig, price: 40, mrp: 50);

        Assert.Equal(("NEW-1", "Vegetables", 40m, 50m), (product.Sku, product.CategoryName, product.Price, product.Mrp));
        var size = Assert.Single(product.Variants);
        Assert.Equal(("250 g", 40m, 50m, true, true), (size.Label, size.Price, size.Mrp, size.IsDefault, size.IsActive));
        Assert.Contains(rig.Data.Products, item => item.Id == product.Id);
    }

    [Theory]
    [InlineData("", "Name", "Dairy", 10, null)]
    [InlineData("S 1", "Name", "Dairy", 10, null)]
    [InlineData("S-1", "  ", "Dairy", 10, null)]
    [InlineData("S-1", "Name", "Dairy", 0, null)]
    [InlineData("S-1", "Name", "Dairy", -5, null)]
    [InlineData("S-1", "Name", "Dairy", 100001, null)]
    [InlineData("S-1", "Name", "Dairy", 10.555, null)]
    [InlineData("S-1", "Name", "Dairy", 40, 39.0)]
    [InlineData("S-1", "Name", "Dairy", 40, 100001.0)]
    public async Task Nonsense_details_are_refused_and_nothing_is_created(string sku, string name, string unit, double price, double? mrp)
    {
        var rig = Create();
        var before = rig.Data.Products.Count;

        var result = await rig.Catalogue.CreateAsync(new CreateProductRequest(sku, name, "d", rig.Veg.Id, unit, (decimal)price, (decimal?)mrp));

        Assert.Equal(CatalogAdminStatus.Invalid, result.Status);
        Assert.Equal(before, rig.Data.Products.Count);
    }

    [Fact]
    public async Task A_too_long_name_description_or_unit_and_an_unknown_category_are_refused()
    {
        var rig = Create();
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.CreateAsync(new CreateProductRequest("L-1", new string('n', 161), "", rig.Veg.Id, "1 kg", 10))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.CreateAsync(new CreateProductRequest("L-2", "n", new string('d', 2001), rig.Veg.Id, "1 kg", 10))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.CreateAsync(new CreateProductRequest("L-3", "n", "", rig.Veg.Id, new string('u', 33), 10))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.CreateAsync(new CreateProductRequest("L-4", "n", "", Guid.NewGuid(), "1 kg", 10))).Status);
    }

    [Fact]
    public async Task A_sku_already_used_by_a_product_or_a_size_is_a_conflict()
    {
        var rig = Create();
        await New(rig, "DUP-1");
        var existingVariantSku = rig.Data.Variants.First().Sku;

        Assert.Equal(CatalogAdminStatus.Conflict, (await rig.Catalogue.CreateAsync(new CreateProductRequest("DUP-1", "Again", "", rig.Veg.Id, "1 kg", 10))).Status);
        Assert.Equal(CatalogAdminStatus.Conflict, (await rig.Catalogue.CreateAsync(new CreateProductRequest(existingVariantSku, "Again", "", rig.Veg.Id, "1 kg", 10))).Status);
    }

    [Fact]
    public async Task Editing_changes_the_details_but_never_the_sku_and_switching_off_hides_it_from_customers()
    {
        var rig = Create();
        var product = await New(rig);

        var edited = (await rig.Catalogue.UpdateAsync(product.Id, new UpdateProductRequest("Palak", "Green leaves", rig.Dairy.Id, "500 g", false))).Value!;

        Assert.Equal(("NEW-1", "Palak", "Green leaves", "Dairy", "500 g", false), (edited.Sku, edited.Name, edited.Description, edited.CategoryName, edited.UnitOfMeasure, edited.IsActive));
        var (customerPage, _) = await rig.Data.GetActiveProductPageAsync(null, null, 0, 100);
        Assert.DoesNotContain(customerPage, item => item.Id == product.Id);

        await rig.Catalogue.UpdateAsync(product.Id, new UpdateProductRequest("Palak", "Green leaves", rig.Dairy.Id, "500 g", true));
        (customerPage, _) = await rig.Data.GetActiveProductPageAsync(null, null, 0, 100);
        Assert.Contains(customerPage, item => item.Id == product.Id);
    }

    [Fact]
    public async Task Editing_an_unknown_product_or_into_an_unknown_category_is_refused()
    {
        var rig = Create();
        var product = await New(rig);

        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Catalogue.UpdateAsync(Guid.NewGuid(), new UpdateProductRequest("n", "", rig.Veg.Id, "1 kg", true))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.UpdateAsync(product.Id, new UpdateProductRequest("n", "", Guid.NewGuid(), "1 kg", true))).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Catalogue.GetAsync(Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task The_list_is_searched_by_name_or_sku_and_paged_and_includes_switched_off_products()
    {
        var rig = Create();
        var product = await New(rig, "ZZ-LIST");
        await rig.Catalogue.UpdateAsync(product.Id, new UpdateProductRequest("Spinach", "", rig.Veg.Id, "250 g", false));

        var bySku = (await rig.Catalogue.ListAsync("zz-list", 1, 20)).Value!;
        var all = (await rig.Catalogue.ListAsync(null, 1, 2)).Value!;

        Assert.Equal(product.Id, Assert.Single(bySku.Items).Id);
        Assert.False(bySku.Items[0].IsActive);
        Assert.Equal(2, all.Items.Count);
        Assert.True(all.TotalCount > 2);
    }

    // ---------------- Marathi text ----------------

    [Fact]
    public async Task Marathi_text_is_added_replaced_and_removed_and_other_languages_are_refused()
    {
        var rig = Create();
        var product = await New(rig);

        var added = (await rig.Catalogue.SaveTranslationAsync(product.Id, "MR", new SaveTranslationRequest("पालक", "ताजी पालेभाजी"))).Value!;
        Assert.Equal(("mr", "पालक", "ताजी पालेभाजी"), (added.Translations[0].Locale, added.Translations[0].Name, added.Translations[0].Description));

        var replaced = (await rig.Catalogue.SaveTranslationAsync(product.Id, "mr", new SaveTranslationRequest("पालक भाजी"))).Value!;
        Assert.Equal(("पालक भाजी", null), (Assert.Single(replaced.Translations).Name, replaced.Translations[0].Description));

        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.SaveTranslationAsync(product.Id, "fr", new SaveTranslationRequest("x"))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.SaveTranslationAsync(product.Id, "mr", new SaveTranslationRequest(" "))).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Catalogue.SaveTranslationAsync(Guid.NewGuid(), "mr", new SaveTranslationRequest("x"))).Status);

        Assert.Empty((await rig.Catalogue.DeleteTranslationAsync(product.Id, "mr")).Value!.Translations);
    }

    // ---------------- sizes ----------------

    [Fact]
    public async Task A_new_size_is_added_not_default_and_its_sku_must_be_new()
    {
        var rig = Create();
        var product = await New(rig);

        var added = (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("NEW-1-500", "500 g", 70, 80, 2))).Value!;

        Assert.Equal(2, added.Variants.Count);
        var size = added.Variants.Single(variant => variant.Sku == "NEW-1-500");
        Assert.Equal((70m, 80m, false, true), (size.Price, size.Mrp, size.IsDefault, size.IsActive));
        Assert.Equal(CatalogAdminStatus.Conflict, (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("NEW-1-500", "1 kg", 99, null))).Status);
        Assert.Equal(CatalogAdminStatus.Conflict, (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("NEW-1", "1 kg", 99, null))).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("N-2", "1 kg", 99, 50))).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Catalogue.AddVariantAsync(Guid.NewGuid(), new SaveVariantRequest("N-3", "1 kg", 99, null))).Status);
    }

    [Fact]
    public async Task Changing_the_default_sizes_price_changes_the_products_own_price_too()
    {
        var rig = Create();
        var product = await New(rig, price: 40, mrp: 50);
        var size = product.Variants[0];

        var edited = (await rig.Catalogue.UpdateVariantAsync(product.Id, size.Id, new SaveVariantRequest(size.Sku, "250 g", 45, 55))).Value!;

        Assert.Equal((45m, 55m), (edited.Price, edited.Mrp));
        Assert.Equal((45m, 55m), (rig.Data.Products.Single(item => item.Id == product.Id).Price, rig.Data.Products.Single(item => item.Id == product.Id).Mrp));
    }

    [Fact]
    public async Task Changing_another_sizes_price_leaves_the_products_price_alone()
    {
        var rig = Create();
        var product = await New(rig, price: 40, mrp: null);
        var other = (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("N-500", "500 g", 70, null, 1))).Value!.Variants.Single(variant => !variant.IsDefault);

        var edited = (await rig.Catalogue.UpdateVariantAsync(product.Id, other.Id, new SaveVariantRequest("N-500", "500 g", 75, null, 1))).Value!;

        Assert.Equal(40m, edited.Price);
    }

    [Fact]
    public async Task The_default_size_cannot_be_switched_off_until_another_is_made_the_default()
    {
        var rig = Create();
        var product = await New(rig, price: 40, mrp: null);
        var first = product.Variants[0];
        var second = (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("N-500", "500 g", 70, null, 1))).Value!.Variants.Single(variant => !variant.IsDefault);

        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.UpdateVariantAsync(product.Id, first.Id, new SaveVariantRequest(first.Sku, first.Label, 40, null, 0, false))).Status);
        Assert.True(rig.Data.Variants.Single(variant => variant.Id == first.Id).IsActive);

        var swapped = (await rig.Catalogue.MakeDefaultAsync(product.Id, second.Id)).Value!;
        Assert.Equal(second.Id, Assert.Single(swapped.Variants, variant => variant.IsDefault).Id);
        Assert.Equal(70m, swapped.Price);

        var off = (await rig.Catalogue.UpdateVariantAsync(product.Id, first.Id, new SaveVariantRequest(first.Sku, first.Label, 40, null, 0, false))).Value!;
        Assert.False(off.Variants.Single(variant => variant.Id == first.Id).IsActive);
    }

    [Fact]
    public async Task A_switched_off_or_unknown_size_cannot_be_made_the_default()
    {
        var rig = Create();
        var product = await New(rig);
        var other = (await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("N-500", "500 g", 70, null, 1, false))).Value!.Variants.Single(variant => !variant.IsDefault);

        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Catalogue.MakeDefaultAsync(product.Id, other.Id)).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Catalogue.MakeDefaultAsync(product.Id, Guid.NewGuid())).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Catalogue.MakeDefaultAsync(Guid.NewGuid(), other.Id)).Status);
        Assert.Single(rig.Data.Variants.Where(variant => variant.ProductId == product.Id && variant.IsDefault));
    }

    [Fact]
    public async Task A_size_of_another_product_cannot_be_edited_through_this_one()
    {
        var rig = Create();
        var one = await New(rig, "ONE-1");
        var two = await New(rig, "TWO-1");

        var result = await rig.Catalogue.UpdateVariantAsync(one.Id, two.Variants[0].Id, new SaveVariantRequest("TWO-1", "250 g", 1, null));

        Assert.Equal(CatalogAdminStatus.NotFound, result.Status);
        Assert.Equal(40m, rig.Data.Variants.Single(variant => variant.Id == two.Variants[0].Id).Price);
    }

    [Fact]
    public async Task Existing_orders_keep_the_price_they_were_placed_at()
    {
        var rig = Create();
        var product = await New(rig, price: 40, mrp: null);
        var order = new Order { OrderNumber = "P-1", UserId = Guid.NewGuid(), StoreId = rig.A.Id, DeliveryAddress = "x", TotalAmount = 40 };
        order.Items.Add(new OrderItem { ProductId = product.Id, ProductNameSnapshot = "Spinach", UnitPrice = 40, Quantity = 1 });
        rig.Data.Orders.Add(order);

        await rig.Catalogue.UpdateVariantAsync(product.Id, product.Variants[0].Id, new SaveVariantRequest("NEW-1", "250 g", 99, null));

        Assert.Equal(40m, rig.Data.Orders.Single(item => item.Id == order.Id).Items[0].UnitPrice);
    }

    [Fact]
    public async Task Customers_are_not_shown_a_stale_copy_after_an_edit()
    {
        var rig = Create();
        var product = await New(rig);
        rig.Cache.Items[CatalogCacheKeys.Product(product.Id)] = "old";
        rig.Cache.Items[CatalogCacheKeys.CatalogProduct(product.Id)] = "old";
        rig.Cache.Items[CatalogCacheKeys.Categories] = "unrelated";

        await rig.Catalogue.UpdateAsync(product.Id, new UpdateProductRequest("Palak", "", rig.Veg.Id, "250 g", true));

        Assert.False(rig.Cache.Items.ContainsKey(CatalogCacheKeys.Product(product.Id)));
        Assert.False(rig.Cache.Items.ContainsKey(CatalogCacheKeys.CatalogProduct(product.Id)));
        Assert.True(rig.Cache.Items.ContainsKey(CatalogCacheKeys.Categories));
    }

    // ---------------- pictures ----------------

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task A_real_picture_is_stored_under_a_name_we_make_and_set_on_the_product(string kind)
    {
        var rig = Create();
        var product = await New(rig);
        var bytes = kind switch { "jpg" => Jpeg, "png" => Png, _ => Webp };

        var result = (await Upload(rig, product.Id, bytes)).Value!;

        Assert.Matches($"^/media/products/{product.Id:N}-[0-9a-f]{{8}}\\.{kind}$", result.ImageUrl!);
        Assert.Equal(bytes, rig.Images.Files[result.ImageUrl!]);
    }

    [Fact]
    public async Task Replacing_or_removing_a_picture_deletes_the_old_file()
    {
        var rig = Create();
        var product = await New(rig);
        var first = (await Upload(rig, product.Id, Jpeg)).Value!.ImageUrl!;

        var second = (await Upload(rig, product.Id, Png)).Value!.ImageUrl!;
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(first, rig.Images.Files.Keys);

        Assert.Null((await rig.Catalogue.RemoveImageAsync(product.Id)).Value!.ImageUrl);
        Assert.Empty(rig.Images.Files);
    }

    [Fact]
    public async Task Something_that_is_not_a_picture_is_refused_whatever_it_is_called()
    {
        var rig = Create();
        var product = await New(rig);

        foreach (var bytes in new[] { "<script>alert(1)</script>"u8.ToArray(), "GIF89a......"u8.ToArray(), [0x4D, 0x5A, 0x90, 0x00], [0xFF, 0xD8], [], "RIFF....AVI "u8.ToArray() })
        {
            var result = await Upload(rig, product.Id, bytes, length: Math.Max(bytes.Length, 1));
            Assert.Equal(CatalogAdminStatus.Invalid, result.Status);
        }

        Assert.Empty(rig.Images.Files);
        Assert.Null(rig.Data.Products.Single(item => item.Id == product.Id).ImageUrl);
    }

    [Fact]
    public async Task A_picture_over_two_megabytes_is_refused_and_one_exactly_at_the_limit_is_accepted()
    {
        var rig = Create();
        var product = await New(rig);
        var big = new byte[CatalogAdminService.MaxImageBytes + 1];
        Jpeg.CopyTo(big, 0);
        var limit = new byte[CatalogAdminService.MaxImageBytes];
        Jpeg.CopyTo(limit, 0);

        Assert.Equal(CatalogAdminStatus.Invalid, (await Upload(rig, product.Id, big)).Status);
        Assert.Equal(CatalogAdminStatus.Invalid, (await Upload(rig, product.Id, Jpeg, length: big.Length)).Status);
        Assert.Equal(CatalogAdminStatus.Succeeded, (await Upload(rig, product.Id, limit)).Status);
    }

    [Fact]
    public async Task A_picture_for_an_unknown_product_stores_nothing()
    {
        var rig = Create();

        Assert.Equal(CatalogAdminStatus.NotFound, (await Upload(rig, Guid.NewGuid(), Jpeg)).Status);
        Assert.Empty(rig.Images.Files);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, "jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, null)]
    public void Pictures_are_told_apart_by_their_first_bytes(byte[] bytes, string? expected) => Assert.Equal(expected, CatalogAdminService.ImageType(bytes));

    // ---------------- the local folder ----------------

    [Fact]
    public async Task The_local_folder_saves_under_its_root_and_only_deletes_its_own_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "qc-media-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "qc-outside-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            var store = new LocalImageStore(root);
            await File.WriteAllBytesAsync(outside, Jpeg);

            var path = await store.SaveAsync("abc-123", "jpg", Jpeg);
            Assert.Equal("/media/products/abc-123.jpg", path);
            Assert.True(File.Exists(Path.Combine(root, "products", "abc-123.jpg")));

            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("../evil", "jpg", Jpeg));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("ok", "exe", Jpeg));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("A/B", "png", Jpeg));

            await store.DeleteAsync("https://example.com/x.jpg");
            await store.DeleteAsync("/media/products/../../" + Path.GetFileName(outside));
            await store.DeleteAsync(null);
            Assert.True(File.Exists(outside));
            Assert.True(File.Exists(Path.Combine(root, "products", "abc-123.jpg")));

            await store.DeleteAsync(path);
            Assert.False(File.Exists(Path.Combine(root, "products", "abc-123.jpg")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            File.Delete(outside);
        }
    }

    // ---------------- stock ----------------

    [Fact]
    public async Task Store_staff_see_and_change_only_their_own_stores_stock_even_if_they_name_another_store()
    {
        var rig = Create();
        var product = await New(rig);
        var variantId = product.Variants[0].Id;
        rig.SignInStaff(rig.A);

        var set = await rig.Stock.SetAsync(rig.B.Id, variantId, new SetStockRequest(true, 12, 3));

        Assert.Equal((true, 12, 3), (set.Value!.Carried, set.Value.Available, set.Value.ReorderThreshold));
        Assert.Contains(rig.Data.Inventory, row => row.StoreId == rig.A.Id && row.VariantId == variantId && row.AvailableQuantity == 12);
        Assert.DoesNotContain(rig.Data.Inventory, row => row.StoreId == rig.B.Id && row.VariantId == variantId);
        var list = (await rig.Stock.ListAsync(rig.B.Id)).Value!;
        Assert.Equal(12, list.Single(row => row.VariantId == variantId).Available);
    }

    [Fact]
    public async Task The_stock_list_has_every_active_size_with_not_carried_ones_at_zero()
    {
        var rig = Create();
        var product = await New(rig);
        await rig.Catalogue.AddVariantAsync(product.Id, new SaveVariantRequest("N-OFF", "1 kg", 99, null, 5, false));
        rig.SignInStaff(rig.A);

        var rows = (await rig.Stock.ListAsync(null)).Value!;

        var row = rows.Single(item => item.VariantId == product.Variants[0].Id);
        Assert.Equal((false, 0, "Spinach", "250 g"), (row.Carried, row.Available, row.ProductName, row.VariantLabel));
        Assert.DoesNotContain(rows, item => item.VariantLabel == "1 kg" && item.ProductName == "Spinach");
    }

    [Fact]
    public async Task Not_carried_removes_the_row_and_carried_again_brings_it_back()
    {
        var rig = Create();
        var variantId = (await New(rig)).Variants[0].Id;
        rig.SignInStaff(rig.A);
        await rig.Stock.SetAsync(null, variantId, new SetStockRequest(true, 8));

        var off = (await rig.Stock.SetAsync(null, variantId, new SetStockRequest(false, 99))).Value!;
        Assert.Equal((false, 0), (off.Carried, off.Available));
        Assert.DoesNotContain(rig.Data.Inventory, row => row.StoreId == rig.A.Id && row.VariantId == variantId);

        var on = (await rig.Stock.SetAsync(null, variantId, new SetStockRequest(true, 20, 4))).Value!;
        Assert.Equal((true, 20, 4), (on.Carried, on.Available, on.ReorderThreshold));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(100001, 5)]
    [InlineData(5, -1)]
    public async Task Impossible_quantities_are_refused(int available, int threshold)
    {
        var rig = Create();
        var variantId = (await New(rig)).Variants[0].Id;
        rig.SignInStaff(rig.A);

        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Stock.SetAsync(null, variantId, new SetStockRequest(true, available, threshold))).Status);
        Assert.DoesNotContain(rig.Data.Inventory, row => row.StoreId == rig.A.Id && row.VariantId == variantId);
    }

    [Fact]
    public async Task An_administrator_must_name_a_store_of_the_organization_and_others_cannot_manage_stock()
    {
        var rig = Create();
        var variantId = (await New(rig)).Variants[0].Id;

        Assert.Equal(CatalogAdminStatus.Invalid, (await rig.Stock.ListAsync(null)).Status);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Stock.ListAsync(Guid.NewGuid())).Status);
        Assert.Equal(CatalogAdminStatus.Succeeded, (await rig.Stock.SetAsync(rig.B.Id, variantId, new SetStockRequest(true, 3))).Status);
        Assert.Contains(rig.Data.Inventory, row => row.StoreId == rig.B.Id && row.VariantId == variantId);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Stock.SetAsync(rig.B.Id, Guid.NewGuid(), new SetStockRequest(true, 3))).Status);

        rig.SignInStaff(null);
        Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Stock.ListAsync(null)).Status);
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, null, Role.Customer);
        Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Stock.ListAsync(rig.A.Id)).Status);
        Assert.Equal(CatalogAdminStatus.Forbidden, (await rig.Stock.SetAsync(rig.A.Id, variantId, new SetStockRequest(true, 3))).Status);
        rig.Who.Context = new UserContext(Guid.NewGuid(), Guid.NewGuid(), null, Role.Admin);
        Assert.Equal(CatalogAdminStatus.NotFound, (await rig.Stock.ListAsync(rig.A.Id)).Status);
    }

    [Fact]
    public async Task A_product_added_here_can_be_found_by_customers_of_a_store_that_carries_it()
    {
        var rig = Create();
        var product = await New(rig);
        await rig.Stock.SetAsync(rig.A.Id, product.Variants[0].Id, new SetStockRequest(true, 5));

        var (carried, _) = await rig.Data.GetStoreProductPageAsync(rig.A.Id, null, null, 0, 100);
        var (otherStore, _) = await rig.Data.GetStoreProductPageAsync(rig.B.Id, null, null, 0, 100);

        Assert.Contains(carried, item => item.Id == product.Id);
        Assert.DoesNotContain(otherStore, item => item.Id == product.Id);
    }
}

[Collection("SqlServer")]
public sealed class CatalogAdminEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static string Sku() => "T" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<(Product Product, ProductVariant Variant)> SeedAsync(CheckoutEfIntegrationTests.World world, string? sku = null, decimal price = 40)
    {
        sku ??= Sku();
        var product = new Product { Sku = sku, Name = "T " + sku, Description = "d", Price = price, UnitOfMeasure = "1 kg", CategoryId = world.Category.Id };
        var variant = new ProductVariant { ProductId = product.Id, Sku = sku, Label = "1 kg", Price = price, IsDefault = true };
        await using var db = world.Db();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        return (product, variant);
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, params Guid[] productIds)
    {
        await using var db = world.Db();
        var variantIds = await db.ProductVariants.Where(variant => productIds.Contains(variant.ProductId)).Select(variant => variant.Id).ToListAsync();
        await db.StoreVariantInventory.Where(row => variantIds.Contains(row.VariantId)).ExecuteDeleteAsync();
        await db.ProductTranslations.Where(item => productIds.Contains(item.ProductId)).ExecuteDeleteAsync();
        await db.ProductVariants.Where(variant => productIds.Contains(variant.ProductId)).ExecuteDeleteAsync();
        await db.Products.Where(product => productIds.Contains(product.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_product_is_created_edited_translated_and_listed_on_sql()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var sku = Sku();
        var ids = new List<Guid>();
        try
        {
            await using (var db = world.Db())
            {
                var product = new Product { Sku = sku, Name = "Created " + sku, Description = "d", Price = 30, Mrp = 35, UnitOfMeasure = "500 g", CategoryId = world.Category.Id };
                ids.Add(product.Id);
                Assert.Equal(CatalogChange.Succeeded, await new EfCatalogAdminStore(db).CreateProductAsync(product, new ProductVariant { ProductId = product.Id, Sku = sku, Label = "500 g", Price = 30, Mrp = 35, IsDefault = true }));
            }

            var id = ids[0];
            await using (var db = world.Db())
            {
                var store = new EfCatalogAdminStore(db);
                Assert.Equal(CatalogChange.Succeeded, await store.UpdateProductAsync(id, "Renamed", "new text", world.Category.Id, "500 g", true));
                Assert.Equal(CatalogChange.Succeeded, await store.SaveTranslationAsync(id, "mr", "नवे नाव", null));
                Assert.Equal(CatalogChange.Succeeded, await store.SaveTranslationAsync(id, "mr", "दुसरे नाव", "वर्णन"));
            }

            await using (var db = world.Db())
            {
                var store = new EfCatalogAdminStore(db);
                var bundle = (await store.GetProductAsync(id))!;
                Assert.Equal(("Renamed", world.Category.Name, 1), (bundle.Product.Name, bundle.CategoryName, bundle.Variants.Count));
                Assert.Equal(("mr", "दुसरे नाव", "वर्णन"), (bundle.Translations.Single().Locale, bundle.Translations.Single().Name, bundle.Translations.Single().Description));
                var (found, total) = await store.ListProductsAsync(sku.ToLowerInvariant(), 0, 10);
                Assert.Equal((1, id), (total, found.Single().Product.Id));
                Assert.Equal(CatalogChange.Succeeded, await store.DeleteTranslationAsync(id, "mr"));
                Assert.Empty((await store.GetProductAsync(id))!.Translations);
            }
        }
        finally
        {
            await CleanAsync(world, ids.ToArray());
        }
    }

    [Fact]
    public async Task Duplicate_skus_and_a_missing_category_are_refused_by_the_store_and_leave_nothing_behind()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (existing, _) = await SeedAsync(world);
        var ids = new List<Guid> { existing.Id };
        try
        {
            await using var db = world.Db();
            var store = new EfCatalogAdminStore(db);
            var dup = new Product { Sku = existing.Sku, Name = "d", Description = "d", Price = 1, UnitOfMeasure = "u", CategoryId = world.Category.Id };
            Assert.Equal(CatalogChange.DuplicateSku, await store.CreateProductAsync(dup, new ProductVariant { ProductId = dup.Id, Sku = existing.Sku, Label = "u", Price = 1, IsDefault = true }));
            var orphan = new Product { Sku = Sku(), Name = "d", Description = "d", Price = 1, UnitOfMeasure = "u", CategoryId = Guid.NewGuid() };
            Assert.Equal(CatalogChange.CategoryMissing, await store.CreateProductAsync(orphan, new ProductVariant { ProductId = orphan.Id, Sku = orphan.Sku, Label = "u", Price = 1, IsDefault = true }));
            Assert.Equal(CatalogChange.DuplicateSku, await store.AddVariantAsync(existing.Id, new ProductVariant { ProductId = existing.Id, Sku = existing.Sku, Label = "2 kg", Price = 2 }));

            await using var verify = world.Db();
            Assert.False(await verify.Products.AnyAsync(product => product.Id == dup.Id || product.Id == orphan.Id));
        }
        finally
        {
            await CleanAsync(world, ids.ToArray());
        }
    }

    [Fact]
    public async Task Making_another_size_the_default_swaps_it_in_one_step_and_keeps_the_products_price_in_line()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (product, first) = await SeedAsync(world, price: 40);
        try
        {
            var second = new ProductVariant { ProductId = product.Id, Sku = Sku(), Label = "2 kg", Price = 75, Mrp = 80, SortOrder = 1 };
            await using (var db = world.Db())
            {
                Assert.Equal(CatalogChange.Succeeded, await new EfCatalogAdminStore(db).AddVariantAsync(product.Id, second));
            }

            await using (var db = world.Db())
            {
                Assert.Equal(CatalogChange.Succeeded, await new EfCatalogAdminStore(db).MakeDefaultAsync(product.Id, second.Id));
            }

            await using (var db = world.Db())
            {
                var store = new EfCatalogAdminStore(db);
                Assert.Equal(CatalogChange.CannotDeactivateDefault, await store.UpdateVariantAsync(product.Id, second.Id, second.Sku, "2 kg", 75, 80, 1, false));
                Assert.Equal(CatalogChange.Succeeded, await store.UpdateVariantAsync(product.Id, first.Id, first.Sku, "1 kg", 41, null, 0, false));
            }

            await using var verify = world.Db();
            var bundle = (await new EfCatalogAdminStore(verify).GetProductAsync(product.Id))!;
            Assert.Equal(second.Id, Assert.Single(bundle.Variants, variant => variant.IsDefault).Id);
            Assert.Equal((75m, 80m), (bundle.Product.Price, bundle.Product.Mrp));
            Assert.False(bundle.Variants.Single(variant => variant.Id == first.Id).IsActive);
        }
        finally
        {
            await CleanAsync(world, product.Id);
        }
    }

    [Fact]
    public async Task The_picture_path_is_set_and_the_previous_one_is_returned()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (product, _) = await SeedAsync(world);
        try
        {
            await using (var db = world.Db())
            {
                Assert.Equal((CatalogChange.Succeeded, null), await new EfCatalogAdminStore(db).SetImageAsync(product.Id, "/media/products/a.jpg"));
            }

            await using (var db = world.Db())
            {
                Assert.Equal((CatalogChange.Succeeded, "/media/products/a.jpg"), await new EfCatalogAdminStore(db).SetImageAsync(product.Id, null));
                Assert.Equal(CatalogChange.NotFound, (await new EfCatalogAdminStore(db).SetImageAsync(Guid.NewGuid(), "/x")).Change);
            }
        }
        finally
        {
            await CleanAsync(world, product.Id);
        }
    }

    [Fact]
    public async Task Stock_is_listed_set_and_removed_per_store_and_never_touches_another_store()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (product, variant) = await SeedAsync(world);
        var other = new Store { Name = "Other " + Sku(), Address = "x", Latitude = 40, Longitude = 40, ServiceRadiusKm = 5, OrganizationId = world.Store.OrganizationId };
        try
        {
            await using (var db = world.Db())
            {
                db.Stores.Add(other);
                await db.SaveChangesAsync();
            }

            await using (var db = world.Db())
            {
                var store = new EfCatalogAdminStore(db);
                Assert.False((await store.ListStockAsync(world.Store.Id)).Single(row => row.VariantId == variant.Id).Carried);
                var row = await store.SetStockAsync(world.Store.Id, variant.Id, true, 9, 2);
                Assert.Equal((true, 9, 2), (row!.Carried, row.Available, row.ReorderThreshold));
            }

            await using (var db = world.Db())
            {
                var store = new EfCatalogAdminStore(db);
                Assert.Equal(9, (await store.ListStockAsync(world.Store.Id)).Single(row => row.VariantId == variant.Id).Available);
                Assert.False((await store.ListStockAsync(other.Id)).Single(row => row.VariantId == variant.Id).Carried);
                await store.SetStockAsync(world.Store.Id, variant.Id, true, 14, 2);
                Assert.Null(await store.SetStockAsync(world.Store.Id, Guid.NewGuid(), true, 1, 1));
                await store.SetStockAsync(world.Store.Id, variant.Id, false, 0, 5);
            }

            await using var verify = world.Db();
            Assert.False(await verify.StoreVariantInventory.AnyAsync(row => row.VariantId == variant.Id));
        }
        finally
        {
            await CleanAsync(world, product.Id);
            await using var db = world.Db();
            await db.Stores.Where(store => store.Id == other.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Setting_stock_while_orders_take_it_ends_with_the_last_value_set_and_no_failure()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 500);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var db = world.Db();
                if (index % 2 == 0)
                {
                    return await new EfCatalogAdminStore(db).SetStockAsync(world.Store.Id, world.Kilo.Id, true, 100 + index, 5) is not null;
                }

                // Taking stock is one atomic update; like any real request, it asks again if SQL picks it as a deadlock victim.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await db.StoreVariantInventory.Where(row => row.StoreId == world.Store.Id && row.VariantId == world.Kilo.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.AvailableQuantity, row => row.AvailableQuantity - 1));
                        return true;
                    }
                    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && attempt < 5)
                    {
                    }
                }
            }));

            Assert.All(results, Assert.True);
            await using var verify = world.Db();
            var finalRow = await verify.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.StoreId == world.Store.Id && row.VariantId == world.Kilo.Id);
            Assert.InRange(finalRow.AvailableQuantity, 90, 110);
        }
        finally
        {
            await CleanAsync(world);
        }
    }
}
