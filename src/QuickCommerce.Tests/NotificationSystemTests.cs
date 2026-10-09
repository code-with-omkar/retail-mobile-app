using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P11: the notification catalogue (types, English and Marathi), order and payment events, offers and preferences.</summary>
public sealed class NotificationSystemTests
{
    private sealed class FixedScope(bool admin) : IAuthorizationScopeService
    {
        private AuthorizationScope Scope => new(Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" }, new HashSet<Guid>(), new HashSet<string>());
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(Scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Rig
    {
        public required PaymentRig Payments { get; init; }
        public required NotificationService Service { get; init; }
        public InMemoryCommerceStore Data => Payments.Data;
        public Customer Customer => Payments.Customer;
    }

    private static Rig Create()
    {
        var rig = PaymentRig.Create();
        var user = rig.Data.Users.Single();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ExternalSubject), new("organization_id", rig.Data.Organizations.Single().Id.ToString()) };
        var current = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } });
        return new Rig { Payments = rig, Service = new NotificationService(rig.Data, new CurrentUserContextResolver(current, rig.Data)) };
    }

    private static CampaignAdminService Admin(InMemoryCommerceStore data, bool admin = true, TimeProvider? clock = null) => new(data, new FixedScope(admin), clock);

    // ---------------- the catalogue ----------------

    public static IEnumerable<object[]> AllTypes() => typeof(NotificationTypes).GetFields().Where(field => field.IsLiteral && (string)field.GetRawConstantValue()! != NotificationTypes.Offer).Select(field => new object[] { field.GetRawConstantValue()! });

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_type_has_english_and_marathi_words_that_carry_the_order_number(string type)
    {
        Assert.True(NotificationCatalog.Knows(type));
        var note = NotificationCatalog.Note(type, "KHG-261009-0042");
        var notification = NotificationCatalog.From(Guid.NewGuid(), Guid.NewGuid(), note);

        var english = NotificationCatalog.Render(notification, "en");
        var marathi = NotificationCatalog.Render(notification, "mr");

        Assert.Contains("KHG-261009-0042", english.Message);
        Assert.Contains("KHG-261009-0042", marathi.Message);
        Assert.NotEqual(english.Title, marathi.Title);
        Assert.DoesNotContain("{", english.Message + marathi.Message + english.Title + marathi.Title);
        Assert.Equal((note.Title, note.Message), english);
        Assert.Contains(note.Category, new[] { NotificationCategories.Order, NotificationCategories.Payment });
    }

    [Fact]
    public void A_notification_the_catalogue_does_not_know_is_shown_as_stored_in_any_language()
    {
        var old = new Notification { CustomerId = Guid.NewGuid(), Type = "OrderStatusChanged", Title = "Order status updated", Message = "Your order is now Accepted." };

        Assert.Equal(("Order status updated", "Your order is now Accepted."), NotificationCatalog.Render(old, "mr"));
        Assert.Equal(("Order status updated", "Your order is now Accepted."), NotificationCatalog.Render(old, null));
    }

    [Fact]
    public void A_known_type_with_damaged_data_falls_back_to_the_stored_words()
    {
        var note = new Notification { CustomerId = Guid.NewGuid(), Type = NotificationTypes.OrderAccepted, Title = "Order accepted", Message = "stored", DataJson = "{not json" };

        Assert.Equal(("Order accepted", "stored"), NotificationCatalog.Render(note, "mr"));
    }

    [Theory]
    [InlineData(OrderStatus.Accepted, NotificationTypes.OrderAccepted)]
    [InlineData(OrderStatus.Preparing, NotificationTypes.OrderPacking)]
    [InlineData(OrderStatus.Ready, NotificationTypes.OutForDelivery)]
    [InlineData(OrderStatus.Completed, NotificationTypes.OrderDelivered)]
    [InlineData(OrderStatus.Rejected, NotificationTypes.OrderRejected)]
    [InlineData(OrderStatus.Cancelled, NotificationTypes.OrderCancelled)]
    public void Each_order_status_maps_to_its_notification_type(OrderStatus status, string type) => Assert.Equal(type, NotificationCatalog.TypeFor(status));

    // ---------------- order events ----------------

    [Fact]
    public async Task Placing_a_cash_order_and_every_step_the_shop_takes_tell_the_customer_in_their_language()
    {
        var rig = Create();
        var order = await rig.Payments.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery);
        var organization = rig.Data.Organizations.Single().Id;
        foreach (var step in new[] { OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed })
        {
            await rig.Data.TryTransitionOrderAsync(order.Id, organization, null, step);
        }

        var english = await rig.Service.GetAsync(false, "en");
        var marathi = await rig.Service.GetAsync(false, "mr");

        Assert.Equal(
            [NotificationTypes.OrderPlaced, NotificationTypes.OrderAccepted, NotificationTypes.OrderPacking, NotificationTypes.OutForDelivery, NotificationTypes.OrderDelivered],
            english!.OrderBy(item => item.CreatedAt).ThenBy(item => item.Type).Select(item => item.Type).OrderBy(type => new[] { NotificationTypes.OrderPlaced, NotificationTypes.OrderAccepted, NotificationTypes.OrderPacking, NotificationTypes.OutForDelivery, NotificationTypes.OrderDelivered }.ToList().IndexOf(type)));
        Assert.All(english, item => Assert.Contains(order.OrderNumber, item.Message));
        Assert.All(marathi!, item => Assert.Contains(order.OrderNumber, item.Message));
        Assert.Contains(english, item => item.Title == "Out for delivery");
        Assert.DoesNotContain(marathi, item => item.Title == "Out for delivery");
        Assert.All(english, item => Assert.Equal(NotificationCategories.Order, item.Category));
    }

    [Fact]
    public async Task Payment_events_are_payment_notifications_with_the_order_number()
    {
        var rig = Create();
        var order = await rig.Payments.PlaceOnlineAsync();
        var paid = await rig.Payments.PayAsync(order.Id);

        await rig.Payments.Payments.ConfirmAsync(order.Id, paid.Confirm);

        var note = Assert.Single(rig.Data.Notifications);
        Assert.Equal((NotificationTypes.PaymentReceived, NotificationCategories.Payment), (note.Type, note.Category));
        Assert.Contains(order.OrderNumber, note.Message);
        Assert.Contains(order.OrderNumber, (await rig.Service.GetAsync(false, "mr"))!.Single().Message);
    }

    // ---------------- offers ----------------

    private static async Task<CampaignResponse> CreateOffer(InMemoryCommerceStore data, string title = "Festival sale", DateTime? startsAt = null, string? titleMr = "सणाची सवलत", string? bodyMr = "आज सर्व फळांवर १०% सूट.") =>
        (await Admin(data).CreateAsync(new CampaignRequest(title, "10% off all fruit today.", titleMr, bodyMr, startsAt))).Value!;

    [Fact]
    public async Task An_offer_reaches_only_customers_with_offers_on_and_only_once()
    {
        var rig = Create();
        var customer = rig.Customer;
        var other = new Customer { UserId = Guid.NewGuid(), OffersEnabled = false };
        rig.Data.Customers.Add(other);
        var offer = await CreateOffer(rig.Data);

        Assert.Equal(1, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1)));
        Assert.Equal(0, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(2)));

        var mine = Assert.Single(rig.Data.Notifications, item => item.CustomerId == customer.Id);
        Assert.Equal((NotificationTypes.Offer, NotificationCategories.Offer), (mine.Type, mine.Category));
        Assert.DoesNotContain(rig.Data.Notifications, item => item.CustomerId == other.Id);
        var sent = (await Admin(rig.Data).ListAsync()).Value!.Single();
        Assert.Equal((offer.Id, CampaignStatuses.Sent, 1), (sent.Id, sent.Status, sent.RecipientCount));
    }

    [Fact]
    public async Task An_offer_is_shown_in_marathi_when_it_has_marathi_words_and_in_english_when_it_does_not()
    {
        var rig = Create();
        await CreateOffer(rig.Data);
        await CreateOffer(rig.Data, title: "No Marathi", titleMr: null, bodyMr: null);
        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));

        var marathi = (await rig.Service.GetAsync(false, "mr"))!;
        var english = (await rig.Service.GetAsync(false, "en"))!;

        Assert.Contains(marathi, item => item.Title == "सणाची सवलत" && item.Category == NotificationCategories.Offer);
        Assert.Contains(marathi, item => item.Title == "No Marathi");
        Assert.Contains(english, item => item.Title == "Festival sale");
    }

    [Fact]
    public async Task An_offer_waits_for_its_start_time()
    {
        var rig = Create();
        await CreateOffer(rig.Data, startsAt: DateTime.UtcNow.AddHours(2));

        Assert.Equal(0, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddHours(1)));
        Assert.Empty(rig.Data.Notifications);
        Assert.Equal(1, await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddHours(3)));
        Assert.Single(rig.Data.Notifications);
    }

    [Fact]
    public async Task A_cancelled_offer_is_never_sent_and_one_already_sent_cannot_be_taken_back()
    {
        var rig = Create();
        var service = Admin(rig.Data);
        var cancelled = await CreateOffer(rig.Data, startsAt: DateTime.UtcNow.AddHours(2));
        var sentOffer = await CreateOffer(rig.Data, title: "Sent one");
        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));

        var first = await service.CancelAsync(cancelled.Id);
        var again = await service.CancelAsync(cancelled.Id);
        var late = await service.CancelAsync(sentOffer.Id);
        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddHours(3));

        Assert.Equal((CampaignOperationStatus.Succeeded, CampaignStatuses.Cancelled), (first.Status, first.Value!.Status));
        Assert.Equal(CampaignOperationStatus.Succeeded, again.Status);
        Assert.Equal((CampaignOperationStatus.Conflict, CampaignReasons.AlreadySent), (late.Status, late.Reason));
        Assert.Equal(CampaignOperationStatus.NotFound, (await service.CancelAsync(Guid.NewGuid())).Status);
        Assert.Single(rig.Data.Notifications); // only "Sent one"
    }

    [Fact]
    public async Task Only_an_administrator_can_manage_offers_and_bad_ones_are_refused()
    {
        var rig = Create();
        var staff = Admin(rig.Data, admin: false);
        var admin = Admin(rig.Data);

        Assert.Equal(CampaignOperationStatus.Unauthorized, (await staff.CreateAsync(new CampaignRequest("t", "b"))).Status);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await staff.ListAsync()).Status);
        Assert.Equal(CampaignOperationStatus.Unauthorized, (await staff.CancelAsync(Guid.NewGuid())).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await admin.CreateAsync(new CampaignRequest(" ", "body"))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await admin.CreateAsync(new CampaignRequest("title", ""))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await admin.CreateAsync(new CampaignRequest(new string('t', 161), "body"))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await admin.CreateAsync(new CampaignRequest("title", new string('b', 501)))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await admin.CreateAsync(new CampaignRequest("title", "body", "only title", null))).Status);
        Assert.Equal(CampaignOperationStatus.InvalidRequest, (await admin.CreateAsync(new CampaignRequest("title", "body", StartsAt: DateTime.UtcNow.AddYears(2)))).Status);
        Assert.Empty(rig.Data.Campaigns);
    }

    [Fact]
    public async Task A_start_time_in_the_past_means_now_and_the_list_shows_newest_first_with_status()
    {
        var rig = Create();
        var older = await CreateOffer(rig.Data, startsAt: DateTime.UtcNow.AddDays(-3), title: "Past");
        var later = await CreateOffer(rig.Data, startsAt: DateTime.UtcNow.AddDays(2), title: "Later");

        var list = (await Admin(rig.Data).ListAsync()).Value!;

        Assert.True(older.StartsAt >= DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal([later.Id, older.Id], list.Select(item => item.Id));
        Assert.All(list, item => Assert.Equal(CampaignStatuses.Scheduled, item.Status));
    }

    // ---------------- preferences ----------------

    [Fact]
    public async Task A_customer_can_switch_offers_off_and_on_but_order_notifications_keep_coming()
    {
        var rig = Create();
        Assert.True((await rig.Service.GetPreferencesAsync())!.Offers);

        Assert.False((await rig.Service.SetOffersAsync(false))!.Offers);
        await CreateOffer(rig.Data);
        await rig.Data.PublishDueCampaignsAsync(DateTime.UtcNow.AddMinutes(1));
        await rig.Payments.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery);

        Assert.DoesNotContain(rig.Data.Notifications, item => item.Category == NotificationCategories.Offer);
        Assert.Contains(rig.Data.Notifications, item => item.Type == NotificationTypes.OrderPlaced);
        Assert.True((await rig.Service.SetOffersAsync(true))!.Offers);
        Assert.True((await rig.Service.GetPreferencesAsync())!.Offers);
    }

    [Fact]
    public async Task The_routes_pass_the_language_and_the_preference_through()
    {
        var rig = Create();
        var order = await rig.Payments.PlaceOnlineAsync(method: PaymentMethods.CashOnDelivery);
        var controller = new NotificationsController(rig.Service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var list = Assert.IsType<OkObjectResult>(await controller.Get(false, "mr", CancellationToken.None));
        var set = Assert.IsType<OkObjectResult>(await controller.SetPreferences(new NotificationPreferences(false), CancellationToken.None));
        var read = Assert.IsType<OkObjectResult>(await controller.Preferences(CancellationToken.None));

        var data = (IReadOnlyList<NotificationResponse>)list.Value!.GetType().GetProperty("data")!.GetValue(list.Value)!;
        Assert.Contains(order.OrderNumber, data.Single().Message);
        Assert.NotEqual("Order placed", data.Single().Title);
        Assert.Equal(false, ((NotificationPreferences)set.Value!.GetType().GetProperty("data")!.GetValue(set.Value)!).Offers);
        Assert.Equal(false, ((NotificationPreferences)read.Value!.GetType().GetProperty("data")!.GetValue(read.Value)!).Offers);
    }
}

/// <summary>The same on SQL Server: one offer per customer even when several servers send at once, opt-out, and the cancel race.</summary>
public sealed class NotificationEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<Guid> AddCampaignAsync(CheckoutEfIntegrationTests.World world, string title, DateTime startsAt, string? titleMr = null)
    {
        await using var db = world.Db();
        var campaign = new Campaign { TitleEn = title, BodyEn = "body", TitleMr = titleMr, BodyMr = titleMr is null ? null : "मजकूर", StartsAt = startsAt, CreatedBy = "test" };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, params Guid[] campaignIds)
    {
        await using var db = world.Db();
        await db.Notifications.Where(item => item.CampaignId != null && campaignIds.Contains(item.CampaignId.Value)).ExecuteDeleteAsync();
        await db.Campaigns.Where(item => campaignIds.Contains(item.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Sending_from_several_servers_at_once_gives_each_customer_the_offer_once()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 3, stock: 5);
        var id = await AddCampaignAsync(world, "Parallel offer " + Guid.NewGuid().ToString("N")[..6], DateTime.UtcNow.AddMinutes(-1), titleMr: "मराठी");
        try
        {
            var sends = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var db = world.Db();
                return await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }));

            await using var verify = world.Db();
            var mine = await verify.Notifications.AsNoTracking().Where(item => item.CampaignId == id).ToListAsync();
            var campaign = await verify.Campaigns.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.True(sends.Sum() >= 1);
            Assert.Equal(mine.Count, mine.Select(item => item.CustomerId).Distinct().Count());
            Assert.All(world.CustomerIds, customer => Assert.Contains(mine, item => item.CustomerId == customer));
            Assert.Equal((mine.Count, true), (campaign.RecipientCount, campaign.PublishedAt != null));
            Assert.All(mine, item => Assert.Equal((NotificationTypes.Offer, NotificationCategories.Offer), (item.Type, item.Category)));
        }
        finally
        {
            await CleanAsync(world, id);
        }
    }

    [Fact]
    public async Task A_customer_with_offers_off_is_skipped_and_a_future_or_cancelled_offer_is_not_sent()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 5);
        var optedOut = world.CustomerIds[1];
        await using (var db = world.Db())
        {
            await new EfCommerceStore(db).SetOffersEnabledAsync(optedOut, false);
        }

        var now = await AddCampaignAsync(world, "Now " + Guid.NewGuid().ToString("N")[..6], DateTime.UtcNow.AddMinutes(-1));
        var future = await AddCampaignAsync(world, "Future " + Guid.NewGuid().ToString("N")[..6], DateTime.UtcNow.AddHours(3));
        var cancelled = await AddCampaignAsync(world, "Cancelled " + Guid.NewGuid().ToString("N")[..6], DateTime.UtcNow.AddMinutes(-1));
        try
        {
            await using (var db = world.Db())
            {
                await new EfCampaignStore(db).TryCancelCampaignAsync(cancelled);
            }

            await using (var db = world.Db())
            {
                await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }

            await using var verify = world.Db();
            var sentTo = await verify.Notifications.AsNoTracking().Where(item => item.CampaignId == now).Select(item => item.CustomerId).ToListAsync();
            Assert.Contains(world.CustomerIds[0], sentTo);
            Assert.DoesNotContain(optedOut, sentTo);
            Assert.Equal(0, await verify.Notifications.CountAsync(item => item.CampaignId == future || item.CampaignId == cancelled));
            Assert.Null((await verify.Campaigns.AsNoTracking().SingleAsync(item => item.Id == future)).PublishedAt);
        }
        finally
        {
            await CleanAsync(world, now, future, cancelled);
        }
    }

    [Fact]
    public async Task Cancelling_after_it_was_sent_changes_nothing_and_the_database_refuses_a_second_copy_for_a_customer()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var id = await AddCampaignAsync(world, "Late cancel " + Guid.NewGuid().ToString("N")[..6], DateTime.UtcNow.AddMinutes(-1));
        try
        {
            await using (var db = world.Db())
            {
                await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }

            await using (var db = world.Db())
            {
                var after = await new EfCampaignStore(db).TryCancelCampaignAsync(id);
                Assert.True(after!.IsActive);
                Assert.NotNull(after.PublishedAt);
            }

            await using var duplicate = world.Db();
            duplicate.Notifications.Add(new Notification { CustomerId = world.CustomerIds.Single(), Type = NotificationTypes.Offer, Category = NotificationCategories.Offer, Title = "x", Message = "y", CampaignId = id });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        finally
        {
            await CleanAsync(world, id);
        }
    }

    [Fact]
    public async Task The_customers_list_reads_an_offer_in_marathi_through_its_campaign()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var title = "Marathi offer " + Guid.NewGuid().ToString("N")[..6];
        var id = await AddCampaignAsync(world, title, DateTime.UtcNow.AddMinutes(-1), titleMr: "मराठी सवलत");
        try
        {
            await using (var db = world.Db())
            {
                await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }

            await using var read = world.Db();
            var notifications = await new EfCommerceStore(read).GetNotificationsAsync(world.CustomerIds.Single(), false);
            var offer = Assert.Single(notifications, item => item.CampaignId == id);
            Assert.Equal(("मराठी सवलत", "मजकूर"), NotificationCatalog.Render(offer, "mr"));
            Assert.Equal((title, "body"), NotificationCatalog.Render(offer, "en"));
        }
        finally
        {
            await CleanAsync(world, id);
        }
    }

    [Fact]
    public async Task New_customers_have_offers_on_and_order_notifications_carry_a_category_and_data()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5, cartQuantity: 1);
        var placed = await world.Checkout(world.CustomerIds.Single(), CheckoutEfIntegrationTests.Commit(null));

        await using var verify = world.Db();
        Assert.True((await verify.Customers.AsNoTracking().SingleAsync(item => item.Id == world.CustomerIds.Single())).OffersEnabled);
        var note = await verify.Notifications.AsNoTracking().SingleAsync(item => item.OrderId == placed.Order!.Id);
        Assert.Equal((NotificationTypes.OrderPlaced, NotificationCategories.Order), (note.Type, note.Category));
        Assert.Contains(placed.Order!.OrderNumber, note.DataJson);
        Assert.Contains(placed.Order.OrderNumber, NotificationCatalog.Render(note, "mr").Message);
    }
}
