using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QuickCommerce.Api.Security;
using QuickCommerce.Application;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P20: an offer can carry a percent off chosen products and a banner picture. The server decides every price.</summary>
public sealed class OfferDiscountTests
{
    private sealed class FixedScope(bool admin, Guid organizationId) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), organizationId, new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(context);
    }

    private sealed class MemoryImages : IImageStore
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Task<string> SaveAsync(string name, string extension, byte[] content, CancellationToken cancellationToken = default) => SaveAsync("products", name, extension, content, cancellationToken);

        public Task<string> SaveAsync(string folder, string name, string extension, byte[] content, CancellationToken cancellationToken = default)
        {
            var path = $"/media/{folder}/{name}.{extension}";
            Files[path] = content;
            return Task.FromResult(path);
        }

        public Task DeleteAsync(string? publicPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required TestClock Clock { get; init; }
        public required MemoryImages Images { get; init; }
        public required Store Harbor { get; init; }
        public required Store Other { get; init; }
        public required Product Tomato { get; init; }
        public required ProductVariant Kilo { get; init; }
        public required CampaignAdminService Admin { get; init; }
        public required CampaignAdminService StaffOfHarbor { get; init; }
        public required CampaignAdminService StaffOfOther { get; init; }
        public required CatalogService Catalogue { get; init; }
        public required CartService Cart { get; init; }
        public required CheckoutService Checkout { get; init; }

        public DateTime Now => Clock.Now.UtcDateTime;

        public CampaignRequest Discount(int percent, IReadOnlyList<Guid>? stores = null, bool notify = true, int days = 3, IReadOnlyList<Guid>? products = null) =>
            new("Tomato week", notify ? "Fresh tomatoes for less." : null, StoreIds: stores, DiscountPercent: percent, EndsAt: Now.AddDays(days), ProductIds: products ?? [Tomato.Id], Notify: notify);
    }

    private static Rig Create(decimal kiloPrice = 100)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
        var images = new MemoryImages();
        var harbor = data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
        var other = data.Stores.First(store => store.Id != harbor.Id);
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        var kilo = data.DefaultVariantOf(tomato);
        kilo.Price = kiloPrice;
        kilo.Mrp = null;
        var organization = data.Organizations.Single().Id;
        var user = data.Users.Single();
        CampaignAdminService Staff(Store store) => new(data, new FixedScope(false, organization), new FixedUser(new UserContext(user.Id, organization, store.Id, Role.StoreStaff)), clock, images: images);
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", organization.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        return new Rig
        {
            Data = data,
            Clock = clock,
            Images = images,
            Harbor = harbor,
            Other = other,
            Tomato = tomato,
            Kilo = kilo,
            Admin = new CampaignAdminService(data, new FixedScope(true, organization), new FixedUser(null), clock, images: images),
            StaffOfHarbor = Staff(harbor),
            StaffOfOther = Staff(other),
            Catalogue = new CatalogService(data, new StoreSelectionService(), new NoCache(), new DeliveryEstimator(new DeliverySettings()), null, clock, data),
            Cart = new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator(), null, data, clock),
            Checkout = new CheckoutService(data, resolver, new CheckoutRequestValidator(), timeProvider: clock)
        };
    }

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static async Task<CatalogProductResponse> ShownAt(Rig rig, Store store) =>
        (await rig.Catalogue.GetCatalogProductForStoreAsync(rig.Tomato.Id, store.Id)).Product!;

    // ---------------- the price rule ----------------

    [Theory]
    [InlineData(100, 20, 80)]
    [InlineData(33, 15, 28.05)]
    [InlineData(99.99, 10, 89.99)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 90, 1)]
    public void The_price_after_a_percent_is_rounded_to_the_paisa(double price, int percent, double expected) =>
        Assert.Equal((decimal)expected, OfferPricing.Apply((decimal)price, percent));

    [Fact]
    public void Offers_never_add_up_the_highest_percent_is_used() => Assert.Equal(25, OfferPricing.Best([10, 25, 5]));

    // ---------------- who may make a discount, and what it needs ----------------

    [Fact]
    public async Task An_administrator_creates_a_discount_with_a_message_a_picture_and_products()
    {
        var rig = Create();
        var upload = await rig.Admin.UploadImageAsync(new MemoryStream(Png()), Png().Length);
        Assert.Equal(CampaignOperationStatus.Succeeded, upload.Status);
        Assert.Matches("^/media/offers/[a-z0-9]+\\.png$", upload.Value!);

        var result = await rig.Admin.CreateAsync(rig.Discount(20) with { ImageUrl = upload.Value });

        Assert.Equal(CampaignOperationStatus.Succeeded, result.Status);
        var offer = result.Value!;
        Assert.Equal((20, true, upload.Value), (offer.DiscountPercent, offer.Notify, offer.ImageUrl));
        Assert.Equal(rig.Now.AddDays(3), offer.EndsAt);
        Assert.Equal(["Tomato"], offer.Products!.Select(product => product.Name));
        Assert.True(offer.AllStores);
    }

    [Fact]
    public async Task A_discount_can_run_quietly_with_no_message()
    {
        var rig = Create();

        var result = await rig.Admin.CreateAsync(rig.Discount(10, notify: false));

        Assert.Equal(CampaignOperationStatus.Succeeded, result.Status);
        Assert.False(result.Value!.Notify);
        Assert.Equal(string.Empty, result.Value.BodyEn);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task A_discount_must_be_between_1_and_90_percent(int percent)
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(percent))).Status);
        Assert.Empty(rig.Data.Campaigns);
    }

    [Fact]
    public async Task A_discount_needs_products_a_later_end_and_no_more_than_90_days()
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(10, products: []))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(10) with { EndsAt = null })).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(10) with { EndsAt = rig.Now })).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(10, days: 91))).Status);
        Assert.Equal(CampaignOperationStatus.Succeeded, (await rig.Admin.CreateAsync(rig.Discount(10, days: 90))).Status);
    }

    [Fact]
    public async Task A_made_up_or_switched_off_product_is_refused()
    {
        var rig = Create();
        var off = rig.Data.Products.First(product => product.Id != rig.Tomato.Id);
        off.IsActive = false;

        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(10, products: [Guid.NewGuid()]))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(rig.Discount(10, products: [off.Id]))).Status);
    }

    [Fact]
    public async Task A_message_only_offer_still_needs_its_message_and_a_quiet_offer_needs_a_discount()
    {
        var rig = Create();

        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(new CampaignRequest("Hello"))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await rig.Admin.CreateAsync(new CampaignRequest("Hello", Notify: false))).Status);
        var message = await rig.Admin.CreateAsync(new CampaignRequest("Hello", "A message."));
        Assert.Equal(CampaignOperationStatus.Succeeded, message.Status);
        Assert.Equal((0, null), (message.Value!.DiscountPercent, message.Value.EndsAt));
    }

    [Theory]
    [InlineData("/media/products/abc.png")]
    [InlineData("https://evil.example/x.png")]
    [InlineData("/media/offers/../../secret.png")]
    [InlineData("/media/offers/abc.svg")]
    public async Task Only_a_picture_this_api_saved_can_be_used(string path)
    {
        var rig = Create();

        var result = await rig.Admin.CreateAsync(rig.Discount(10) with { ImageUrl = path });

        Assert.Equal(CampaignOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task The_picture_is_judged_by_its_content_and_size()
    {
        var rig = Create();

        var text = await rig.Admin.UploadImageAsync(new MemoryStream("<svg onload=alert(1)>"u8.ToArray()), 21);
        var big = new byte[CatalogAdminService.MaxImageBytes + 1];
        Png().CopyTo(big, 0);
        var tooBig = await rig.Admin.UploadImageAsync(new MemoryStream(big), big.Length);
        var empty = await rig.Admin.UploadImageAsync(new MemoryStream([]), 0);

        Assert.All(new[] { text, tooBig, empty }, result => Assert.Equal(CampaignOperationStatus.InvalidRequest, result.Status));
        Assert.Empty(rig.Images.Files);
    }

    [Fact]
    public async Task A_customer_or_unknown_caller_cannot_upload_pictures_or_search_products()
    {
        var rig = Create();
        var stranger = new CampaignAdminService(rig.Data, new FixedScope(false, rig.Data.Organizations.Single().Id), new FixedUser(null), rig.Clock, images: rig.Images);

        Assert.Equal(CampaignOperationStatus.Unauthorized, (await stranger.UploadImageAsync(new MemoryStream(Png()), Png().Length)).Status);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await stranger.SearchProductsAsync("tom")).Status);
    }

    [Fact]
    public async Task Staff_discount_only_their_own_store_and_cannot_end_another_stores_offer()
    {
        var rig = Create();

        var own = (await rig.StaffOfHarbor.CreateAsync(rig.Discount(20, stores: [rig.Other.Id]))).Status;
        var offer = (await rig.StaffOfHarbor.CreateAsync(rig.Discount(20))).Value!;

        Assert.Equal(CampaignOperationStatus.Unauthorized, own);
        Assert.Equal([rig.Harbor.Id], offer.Stores.Select(store => store.Id));
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.StaffOfOther.EndDiscountAsync(offer.Id)).Status);
    }

    [Fact]
    public async Task The_product_picker_finds_active_products_by_name()
    {
        var rig = Create();

        var found = (await rig.Admin.SearchProductsAsync("toma")).Value!;
        var none = (await rig.Admin.SearchProductsAsync("no such thing")).Value!;

        Assert.Contains(found, product => product.Id == rig.Tomato.Id);
        Assert.Empty(none);
    }

    // ---------------- when a discount is on ----------------

    [Fact]
    public async Task A_discount_runs_from_its_start_to_its_end_and_never_when_cancelled()
    {
        var rig = Create();
        var offer = (await rig.Admin.CreateAsync(rig.Discount(20) with { StartsAt = rig.Now.AddHours(2) })).Value!;
        var campaign = rig.Data.Campaigns.Single(item => item.Id == offer.Id);

        Assert.False(OfferRules.DiscountRunning(campaign, rig.Now));
        Assert.True(OfferRules.DiscountRunning(campaign, rig.Now.AddHours(3)));
        Assert.False(OfferRules.DiscountRunning(campaign, rig.Now.AddDays(3)));
        await rig.Admin.CancelAsync(offer.Id);
        Assert.False(OfferRules.DiscountRunning(campaign, rig.Now.AddHours(3)));
    }

    [Fact]
    public async Task An_offer_with_an_unpaid_fee_gives_no_discount()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20));
        var campaign = rig.Data.Campaigns.Single();

        campaign.PaymentStatus = PaymentState.Created;
        Assert.False(OfferRules.DiscountRunning(campaign, rig.Now));
        campaign.PaymentStatus = PaymentState.Paid;
        Assert.True(OfferRules.DiscountRunning(campaign, rig.Now));
    }

    [Fact]
    public async Task Ending_a_running_discount_stops_it_and_ending_twice_is_a_conflict()
    {
        var rig = Create();
        var offer = (await rig.Admin.CreateAsync(rig.Discount(20))).Value!;
        Assert.Equal(80m, (await ShownAt(rig, rig.Harbor)).Price);

        var ended = await rig.Admin.EndDiscountAsync(offer.Id);

        Assert.Equal(CampaignOperationStatus.Succeeded, ended.Status);
        Assert.Equal(100m, (await ShownAt(rig, rig.Harbor)).Price);
        var again = await rig.Admin.EndDiscountAsync(offer.Id);
        Assert.Equal((CampaignOperationStatus.Conflict, CampaignReasons.DiscountAlreadyEnded), (again.Status, again.Reason));
        Assert.Equal(CampaignOperationStatus.NotFound, (await rig.Admin.EndDiscountAsync(Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task A_message_only_offer_has_no_discount_to_end()
    {
        var rig = Create();
        var offer = (await rig.Admin.CreateAsync(new CampaignRequest("Hello", "A message."))).Value!;

        Assert.Equal(CampaignOperationStatus.Conflict, (await rig.Admin.EndDiscountAsync(offer.Id)).Status);
    }

    // ---------------- the catalogue shows it ----------------

    [Fact]
    public async Task The_catalogue_shows_the_lower_price_with_the_normal_price_struck_through()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20));

        var product = await ShownAt(rig, rig.Harbor);

        Assert.Equal((80m, 100m, 20, 20), (product.Price, product.Mrp, product.DiscountPercent, product.OfferPercent));
        var variant = Assert.Single(product.Variants!, item => item.IsDefault);
        Assert.Equal((80m, 100m, 20, 20), (variant.Price, variant.Mrp, variant.DiscountPercent, variant.OfferPercent));
    }

    [Fact]
    public async Task A_real_mrp_stays_the_struck_through_figure_and_the_saving_grows()
    {
        var rig = Create();
        rig.Kilo.Mrp = 125;
        await rig.Admin.CreateAsync(rig.Discount(20));

        var product = await ShownAt(rig, rig.Harbor);

        Assert.Equal((80m, 125m, 36), (product.Price, product.Mrp, product.DiscountPercent));
    }

    [Fact]
    public async Task Every_size_of_the_product_is_discounted()
    {
        var rig = Create();
        var half = new ProductVariant { ProductId = rig.Tomato.Id, Sku = "TOM-500", Label = "500 g", Price = 60, SortOrder = 1 };
        rig.Data.Variants.Add(half);
        rig.Data.Inventory.Add(new StoreVariantInventory { StoreId = rig.Harbor.Id, VariantId = half.Id, AvailableQuantity = 10 });
        await rig.Admin.CreateAsync(rig.Discount(50));

        var product = await ShownAt(rig, rig.Harbor);

        Assert.Equal([50m, 30m], product.Variants!.Select(variant => variant.Price).OrderByDescending(price => price));
    }

    [Fact]
    public async Task An_offer_for_one_store_does_not_change_prices_at_another_and_all_stores_means_all()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20, stores: [rig.Harbor.Id]));

        Assert.Equal(80m, (await ShownAt(rig, rig.Harbor)).Price);
        Assert.Equal(100m, (await ShownAt(rig, rig.Other)).Price);

        await rig.Admin.CreateAsync(rig.Discount(10));
        Assert.Equal(90m, (await ShownAt(rig, rig.Other)).Price);
    }

    [Fact]
    public async Task Where_two_offers_cover_a_product_the_higher_percent_wins_and_they_do_not_stack()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(10));
        await rig.Admin.CreateAsync(rig.Discount(30));
        await rig.Admin.CreateAsync(rig.Discount(20));

        Assert.Equal(70m, (await ShownAt(rig, rig.Harbor)).Price);
    }

    [Fact]
    public async Task A_discount_ends_by_the_clock()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20, days: 1));

        Assert.Equal(80m, (await ShownAt(rig, rig.Harbor)).Price);
        rig.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        Assert.Equal(100m, (await ShownAt(rig, rig.Harbor)).Price);
    }

    [Fact]
    public async Task Other_products_keep_their_prices()
    {
        var rig = Create();
        var potato = rig.Data.Products.First(product => product.Id != rig.Tomato.Id);
        var before = (await rig.Catalogue.GetCatalogProductAsync(potato.Id))!.Price;
        await rig.Admin.CreateAsync(rig.Discount(50));

        var page = (await rig.Catalogue.GetCatalogProductForStoreAsync(potato.Id, rig.Harbor.Id)).Product!;

        Assert.Equal((before, 0), (page.Price, page.OfferPercent));
    }

    // ---------------- the cart and checkout charge it ----------------

    [Fact]
    public async Task The_cart_takes_the_discounted_price_and_shows_the_normal_one()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20));

        var cart = (await rig.Cart.AddItemAsync(rig.Harbor.Id, new AddCartItemRequest(rig.Tomato.Id, 2))).Cart!;

        var line = Assert.Single(cart.Items);
        Assert.Equal((80m, 80m, 100m, 20, 160m), (line.UnitPriceSnapshot, line.CurrentUnitPrice, line.OriginalUnitPrice, line.OfferPercent, line.TotalPrice));
    }

    [Fact]
    public async Task Checkout_charges_the_discounted_price_and_the_receipt_keeps_the_normal_price_as_the_mrp()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20));
        await rig.Cart.AddItemAsync(rig.Harbor.Id, new AddCartItemRequest(rig.Tomato.Id, 2));

        var result = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal(CheckoutOperationStatus.Succeeded, result.Status);
        var item = Assert.Single(rig.Data.Orders.Single().Items);
        Assert.Equal((80m, 100m, 160m), (item.UnitPrice, item.UnitMrpSnapshot, item.TotalPrice));
        Assert.Equal(160m, rig.Data.Orders.Single().SubtotalAmount);
    }

    [Fact]
    public async Task When_the_discount_ends_before_checkout_the_customer_is_told_the_price_changed()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20, days: 1));
        await rig.Cart.AddItemAsync(rig.Harbor.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        rig.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));

        var cart = (await rig.Cart.GetCartAsync(rig.Harbor.Id))!;
        var refused = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal((80m, 100m, null), (cart.Items[0].UnitPriceSnapshot, cart.Items[0].CurrentUnitPrice, cart.Items[0].OriginalUnitPrice));
        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.PriceChanged), (refused.Status, refused.Reason));
        Assert.Empty(rig.Data.Orders);

        await rig.Cart.RepriceAsync(rig.Harbor.Id);
        var accepted = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));
        Assert.Equal(CheckoutOperationStatus.Succeeded, accepted.Status);
        Assert.Equal(100m, rig.Data.Orders.Single().Items.Single().UnitPrice);
    }

    [Fact]
    public async Task A_discount_that_starts_after_the_item_was_added_is_applied_by_repricing_and_never_by_the_clients_say_so()
    {
        var rig = Create();
        await rig.Cart.AddItemAsync(rig.Harbor.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        await rig.Admin.CreateAsync(rig.Discount(25));

        var refused = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));
        await rig.Cart.RepriceAsync(rig.Harbor.Id);
        var accepted = await rig.Checkout.CheckoutAsync(rig.Harbor.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87));

        Assert.Equal(CheckoutReasons.PriceChanged, refused.Reason);
        Assert.Equal(CheckoutOperationStatus.Succeeded, accepted.Status);
        Assert.Equal(75m, rig.Data.Orders.Single().Items.Single().UnitPrice);
    }

    [Fact]
    public async Task A_cart_at_a_store_without_the_offer_pays_the_normal_price()
    {
        var rig = Create();
        await rig.Admin.CreateAsync(rig.Discount(20, stores: [rig.Harbor.Id]));

        var cart = (await rig.Cart.AddItemAsync(rig.Other.Id, new AddCartItemRequest(rig.Tomato.Id, 1))).Cart!;

        Assert.Equal((100m, null), (cart.Items[0].UnitPriceSnapshot, cart.Items[0].OriginalUnitPrice));
    }

    // ---------------- sending and banners ----------------

    [Fact]
    public async Task A_quiet_discount_sends_no_notification_but_is_marked_done()
    {
        var rig = Create();
        var customer = rig.Data.Customers.First();
        customer.OffersEnabled = true;
        await rig.Admin.CreateAsync(rig.Discount(10, notify: false));

        var sent = await rig.Data.PublishDueCampaignsAsync(rig.Now);

        Assert.Equal(1, sent);
        Assert.Empty(rig.Data.Notifications.Where(item => item.Category == NotificationCategories.Offer));
        Assert.NotNull(rig.Data.Campaigns.Single().PublishedAt);
        Assert.Equal(0, rig.Data.Campaigns.Single().RecipientCount);
    }

    [Fact]
    public async Task A_discount_with_a_message_notifies_customers_and_the_notification_carries_the_picture()
    {
        var rig = Create();
        var customer = rig.Data.Customers.First();
        customer.OffersEnabled = true;
        var picture = (await rig.Admin.UploadImageAsync(new MemoryStream(Png()), Png().Length)).Value!;
        await rig.Admin.CreateAsync(rig.Discount(10) with { ImageUrl = picture });

        await rig.Data.PublishDueCampaignsAsync(rig.Now);

        var note = Assert.Single(rig.Data.Notifications, item => item.Category == NotificationCategories.Offer);
        Assert.Equal(picture, note.Campaign!.ImageUrl);
    }

    [Fact]
    public async Task Home_shows_running_discounts_and_picture_messages_for_a_week_and_nothing_cancelled()
    {
        var rig = Create();
        var picture = (await rig.Admin.UploadImageAsync(new MemoryStream(Png()), Png().Length)).Value!;
        var discount = (await rig.Admin.CreateAsync(rig.Discount(20, stores: [rig.Harbor.Id]))).Value!;
        var message = (await rig.Admin.CreateAsync(new CampaignRequest("Diwali", "Sweets in store.", ImageUrl: picture))).Value!;
        await rig.Admin.CreateAsync(new CampaignRequest("Plain", "No picture."));
        var cancelled = (await rig.Admin.CreateAsync(rig.Discount(5) with { StartsAt = rig.Now.AddHours(1) })).Value!;
        await rig.Admin.CancelAsync(cancelled.Id);

        var atHarbor = await rig.Data.GetActiveOffersAsync(rig.Harbor.Id, rig.Now);
        var atOther = await rig.Data.GetActiveOffersAsync(rig.Other.Id, rig.Now);

        Assert.Equal(new[] { discount.Id, message.Id }.Order(), atHarbor.Select(offer => offer.Id).Order());
        Assert.Equal([message.Id], atOther.Select(offer => offer.Id));
        Assert.Equal([rig.Tomato.Id], atHarbor.Single(offer => offer.Id == discount.Id).ProductIds);
        Assert.Equal(picture, atHarbor.Single(offer => offer.Id == message.Id).ImageUrl);
        Assert.Empty(await rig.Data.GetActiveOffersAsync(rig.Harbor.Id, rig.Now.AddDays(8)));
    }

    [Fact]
    public async Task The_offer_list_shows_the_new_fields_to_the_people_who_may_see_them()
    {
        var rig = Create();
        var mine = (await rig.StaffOfHarbor.CreateAsync(rig.Discount(20))).Value!;
        await rig.StaffOfOther.CreateAsync(rig.Discount(10));

        var staffList = (await rig.StaffOfHarbor.ListAsync()).Value!;
        var adminList = (await rig.Admin.ListAsync()).Value!;

        Assert.Equal([mine.Id], staffList.Select(item => item.Id));
        Assert.Equal(2, adminList.Count);
        Assert.Equal(("Tomato", 20), (staffList[0].Products![0].Name, staffList[0].DiscountPercent));
    }

    [Fact]
    public async Task An_offer_can_be_opened_once_it_has_started_and_not_when_cancelled_or_unpaid_or_scheduled()
    {
        var rig = Create();
        var live = (await rig.Admin.CreateAsync(rig.Discount(20, stores: [rig.Harbor.Id]))).Value!;
        var later = (await rig.Admin.CreateAsync(rig.Discount(20) with { StartsAt = rig.Now.AddHours(2) })).Value!;
        var cancelled = (await rig.Admin.CreateAsync(rig.Discount(20) with { StartsAt = rig.Now.AddHours(2) })).Value!;
        await rig.Admin.CancelAsync(cancelled.Id);

        var opened = await rig.Data.GetOfferAsync(live.Id, rig.Now);

        Assert.True(opened!.Running);
        Assert.Equal([rig.Harbor.Id], opened.Stores.Select(store => store.Id));
        Assert.Equal([rig.Tomato.Id], opened.ProductIds);
        Assert.Null(await rig.Data.GetOfferAsync(later.Id, rig.Now));
        Assert.Null(await rig.Data.GetOfferAsync(cancelled.Id, rig.Now));
        Assert.Null(await rig.Data.GetOfferAsync(Guid.NewGuid(), rig.Now));
        Assert.False((await rig.Data.GetOfferAsync(live.Id, rig.Now.AddDays(4)))!.Running);
        rig.Data.Campaigns.Single(item => item.Id == live.Id).PaymentStatus = PaymentState.Created;
        Assert.Null(await rig.Data.GetOfferAsync(live.Id, rig.Now));
    }

    [Fact]
    public async Task The_notification_tells_the_app_which_offer_to_open()
    {
        var rig = Create();
        rig.Data.Customers.First().OffersEnabled = true;
        var offer = (await rig.Admin.CreateAsync(rig.Discount(10))).Value!;

        await rig.Data.PublishDueCampaignsAsync(rig.Now);

        Assert.Equal(offer.Id, Assert.Single(rig.Data.Notifications, item => item.Category == NotificationCategories.Offer).CampaignId);
    }
}
