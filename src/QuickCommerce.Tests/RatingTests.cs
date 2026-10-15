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

/// <summary>Customers rate delivered orders (the shop, and the delivery when a partner brought it); staff read the ratings.</summary>
public sealed class RatingTests
{
    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; } = context;
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class MemoryRatings : IRatingStore
    {
        public Dictionary<Guid, OrderRating> Rows { get; } = [];
        public (IReadOnlyCollection<Guid>? Stores, DateTime From, DateTime To) LastQuery { get; private set; }
        public HashSet<Guid> Stores { get; } = [];

        public Task<OrderRating?> GetAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(Rows.GetValueOrDefault(orderId));

        public Task SaveAsync(OrderRating rating, CancellationToken cancellationToken = default)
        {
            Rows[rating.OrderId] = new OrderRating
            {
                OrderId = rating.OrderId, StoreId = rating.StoreId, CustomerId = rating.CustomerId, DeliveryPartnerId = rating.DeliveryPartnerId, StoreStars = rating.StoreStars,
                DeliveryStars = rating.DeliveryStars, Comment = rating.Comment, CreatedAt = rating.CreatedAt, UpdatedAt = rating.UpdatedAt
            };
            return Task.CompletedTask;
        }

        public Task<RatingSummary> SummaryAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            LastQuery = (storeIds, fromUtc, toUtc);
            return Task.FromResult(new RatingSummary(0, 0, null, [0, 0, 0, 0, 0], []));
        }

        public Task<IReadOnlyList<RatingRow>> RecentAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RatingRow>>([]);

        public Task<bool> StoreExistsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(Stores.Contains(storeId));
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required MemoryRatings Store { get; init; }
        public required FixedUser Who { get; init; }
        public required TestClock Clock { get; init; }
        public required RatingService Service { get; init; }
        public required Customer Me { get; init; }
        public required Guid Organization { get; init; }
        public required Store Shop { get; init; }
    }

    private static Rig Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var me = data.Customers.Single();
        var org = data.Organizations.Single().Id;
        var store = new MemoryRatings();
        store.Stores.Add(data.Stores[0].Id);
        var who = new FixedUser(new UserContext(me.UserId, org, null, Role.Customer));
        var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        return new Rig { Data = data, Store = store, Who = who, Clock = clock, Me = me, Organization = org, Shop = data.Stores[0], Service = new RatingService(store, data, who, clock) };
    }

    private static Order AddOrder(Rig rig, OrderStatus status = OrderStatus.Delivered, Guid? partner = null, TimeSpan? deliveredAgo = null, Guid? user = null)
    {
        var at = rig.Clock.GetUtcNow().UtcDateTime - (deliveredAgo ?? TimeSpan.FromHours(1));
        var order = new Order { OrderNumber = "RT-" + Guid.NewGuid().ToString("N")[..6], UserId = user ?? rig.Me.UserId, StoreId = rig.Shop.Id, Status = status, DeliveryAddress = "x", TotalAmount = 100, DeliveryPartnerId = partner };
        if (status is OrderStatus.Delivered && partner is not null)
        {
            order.DeliveredAt = at;
        }

        order.StatusHistory.Add(new OrderStatusHistory { Status = status, ChangedAt = at });
        rig.Data.Orders.Add(order);
        return order;
    }

    // ---------------- giving a rating ----------------

    [Fact]
    public async Task A_delivered_order_is_rated_with_stars_and_a_comment()
    {
        var rig = Create();
        var order = AddOrder(rig);

        var result = await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, null, "  Fresh and on time  "));

        Assert.Equal(RatingStatus.Succeeded, result.Status);
        Assert.Equal((5, null, "Fresh and on time", true, false), (result.Value!.StoreStars, result.Value.DeliveryStars, result.Value.Comment, result.Value.CanEdit, result.Value.CanRateDelivery));
        var saved = rig.Store.Rows[order.Id];
        Assert.Equal((rig.Shop.Id, rig.Me.Id), (saved.StoreId, saved.CustomerId));
    }

    [Fact]
    public async Task An_order_completed_by_the_store_can_be_rated_too()
    {
        var rig = Create();
        var order = AddOrder(rig, OrderStatus.Completed);

        Assert.Equal(RatingStatus.Succeeded, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(4))).Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.AwaitingPayment)]
    public async Task An_order_that_has_not_been_delivered_cannot_be_rated(OrderStatus status)
    {
        var rig = Create();
        var order = AddOrder(rig, status);

        Assert.Equal(RatingStatus.NotYet, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5))).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Fact]
    public async Task Fourteen_days_after_delivery_is_the_last_moment_to_rate_or_change()
    {
        var rig = Create();
        var order = AddOrder(rig, deliveredAgo: TimeSpan.FromDays(14));
        var late = AddOrder(rig, deliveredAgo: TimeSpan.FromDays(14) + TimeSpan.FromSeconds(1));

        Assert.Equal(RatingStatus.Succeeded, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(3))).Status);
        Assert.Equal(RatingStatus.TooLate, (await rig.Service.SaveMineAsync(late.Id, new RatingRequest(3))).Status);

        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(RatingStatus.TooLate, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(1))).Status);
        Assert.Equal(3, rig.Store.Rows[order.Id].StoreStars);
        var after = (await rig.Service.GetMineAsync(order.Id)).Value!;
        Assert.False(after.CanRate);
        Assert.False(after.Rating!.CanEdit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task Stars_are_one_to_five(int stars)
    {
        var rig = Create();
        var order = AddOrder(rig, partner: Guid.NewGuid());

        Assert.Equal(RatingStatus.Invalid, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(stars))).Status);
        Assert.Equal(RatingStatus.Invalid, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, stars))).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Fact]
    public async Task The_delivery_can_be_rated_only_when_a_partner_brought_the_order()
    {
        var rig = Create();
        var byPartner = AddOrder(rig, partner: Guid.NewGuid());
        var collected = AddOrder(rig);

        var ok = await rig.Service.SaveMineAsync(byPartner.Id, new RatingRequest(5, 4));
        Assert.Equal((4, true), (ok.Value!.DeliveryStars, ok.Value.CanRateDelivery));
        Assert.Equal(byPartner.DeliveryPartnerId, rig.Store.Rows[byPartner.Id].DeliveryPartnerId);
        Assert.Equal(RatingStatus.Invalid, (await rig.Service.SaveMineAsync(collected.Id, new RatingRequest(5, 4))).Status);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("nice <b>bold</b>")]
    [InlineData("a > b")]
    [InlineData("bell\u0007")]
    [InlineData("null\u0000byte")]
    public async Task A_comment_cannot_carry_markup_or_control_characters(string comment)
    {
        var rig = Create();
        var order = AddOrder(rig);

        Assert.Equal(RatingStatus.Invalid, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, null, comment))).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Theory]
    [InlineData("खूप छान, वेळेवर आले. 👍")]
    [InlineData("Line one\nline two")]
    [InlineData("Quote \"fine\" & ampersand ok")]
    public async Task Ordinary_text_in_any_language_is_kept_as_written(string comment)
    {
        var rig = Create();
        var order = AddOrder(rig);

        Assert.Equal(comment, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, null, comment))).Value!.Comment);
    }

    [Fact]
    public async Task A_comment_may_be_five_hundred_characters_but_not_more_and_an_empty_one_is_no_comment()
    {
        var rig = Create();
        var order = AddOrder(rig);

        Assert.Equal(RatingStatus.Succeeded, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, null, new string('a', 500)))).Status);
        Assert.Equal(RatingStatus.Invalid, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, null, new string('a', 501)))).Status);
        Assert.Null((await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, null, "   "))).Value!.Comment);
    }

    [Fact]
    public async Task Rating_again_changes_the_rating_instead_of_adding_a_second_one()
    {
        var rig = Create();
        var order = AddOrder(rig, partner: Guid.NewGuid());
        await rig.Service.SaveMineAsync(order.Id, new RatingRequest(2, 1, "late"));
        var created = rig.Store.Rows[order.Id].CreatedAt;
        rig.Clock.Advance(TimeSpan.FromHours(3));

        var again = await rig.Service.SaveMineAsync(order.Id, new RatingRequest(4));

        Assert.Single(rig.Store.Rows);
        var row = rig.Store.Rows[order.Id];
        Assert.Equal((4, null, null, created), (row.StoreStars, row.DeliveryStars, row.Comment, row.CreatedAt));
        Assert.True(row.UpdatedAt > created);
        Assert.Equal(4, again.Value!.StoreStars);
    }

    [Fact]
    public async Task What_the_customer_sent_cannot_change_whose_order_or_which_store_or_partner_it_is()
    {
        var rig = Create();
        var partner = Guid.NewGuid();
        var order = AddOrder(rig, partner: partner);

        await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5, 5));

        var row = rig.Store.Rows[order.Id];
        Assert.Equal((order.StoreId, partner, rig.Me.Id), (row.StoreId, row.DeliveryPartnerId, row.CustomerId));
    }

    // ---------------- whose order ----------------

    [Fact]
    public async Task Someone_elses_order_is_not_found_for_reading_or_rating()
    {
        var rig = Create();
        var stranger = new Customer { UserId = Guid.NewGuid() };
        rig.Data.Customers.Add(stranger);
        var theirs = AddOrder(rig, user: stranger.UserId);

        Assert.Equal(RatingStatus.NotFound, (await rig.Service.SaveMineAsync(theirs.Id, new RatingRequest(1))).Status);
        Assert.Equal(RatingStatus.NotFound, (await rig.Service.GetMineAsync(theirs.Id)).Status);
        Assert.Equal(RatingStatus.NotFound, (await rig.Service.SaveMineAsync(Guid.NewGuid(), new RatingRequest(1))).Status);
        Assert.Empty(rig.Store.Rows);
    }

    [Theory]
    [InlineData(Role.StoreStaff)]
    [InlineData(Role.Admin)]
    [InlineData(Role.DeliveryPartner)]
    public async Task Only_a_customer_rates_an_order(Role role)
    {
        var rig = Create();
        var order = AddOrder(rig);
        rig.Who.Context = new UserContext(rig.Me.UserId, rig.Organization, rig.Shop.Id, role);

        Assert.Equal(RatingStatus.NotFound, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5))).Status);
        Assert.Empty(rig.Store.Rows);
        rig.Who.Context = null;
        Assert.Equal(RatingStatus.NotFound, (await rig.Service.SaveMineAsync(order.Id, new RatingRequest(5))).Status);
    }

    [Fact]
    public async Task Reading_your_rating_says_none_yet_before_and_shows_it_after()
    {
        var rig = Create();
        var order = AddOrder(rig);

        var before = (await rig.Service.GetMineAsync(order.Id)).Value!;
        Assert.Equal((true, false, null), (before.CanRate, before.CanRateDelivery, before.Rating));
        await rig.Service.SaveMineAsync(order.Id, new RatingRequest(3, null, "ok"));
        var mine = (await rig.Service.GetMineAsync(order.Id)).Value!;

        Assert.Equal((true, 3, "ok", true), (mine.CanRate, mine.Rating!.StoreStars, mine.Rating.Comment, mine.Rating.CanEdit));
    }

    [Fact]
    public async Task The_state_of_an_order_says_what_can_be_rated_before_anything_is_given()
    {
        var rig = Create();
        var partnerOrder = AddOrder(rig, partner: Guid.NewGuid());
        var open = AddOrder(rig, OrderStatus.OutForDelivery);
        var old = AddOrder(rig, deliveredAgo: TimeSpan.FromDays(30));

        var delivered = (await rig.Service.GetMineAsync(partnerOrder.Id)).Value!;
        Assert.Equal((true, true), (delivered.CanRate, delivered.CanRateDelivery));
        var notYet = (await rig.Service.GetMineAsync(open.Id)).Value!;
        Assert.Equal((false, false, null), (notYet.CanRate, notYet.CanRateDelivery, notYet.Rating));
        var tooOld = (await rig.Service.GetMineAsync(old.Id)).Value!;
        Assert.Equal((false, false), (tooOld.CanRate, tooOld.CanRateDelivery));
    }

    // ---------------- the staff view ----------------

    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public async Task Store_staff_see_their_own_store_even_when_they_ask_for_another()
    {
        var rig = Create();
        var other = Guid.NewGuid();
        rig.Store.Stores.Add(other);
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, rig.Shop.Id, Role.StoreStaff);

        var report = (await rig.Service.ReportAsync(Day, Day, other)).Value!;

        Assert.Equal([rig.Shop.Id], rig.Store.LastQuery.Stores);
        Assert.Equal(rig.Shop.Id, report.StoreId);
        Assert.Equal((ReportMath.StartUtc(Day), ReportMath.EndUtc(Day)), (rig.Store.LastQuery.From, rig.Store.LastQuery.To));
    }

    [Fact]
    public async Task An_administrator_sees_every_store_or_the_one_named_and_an_unknown_store_is_not_found()
    {
        var rig = Create();
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, null, Role.Admin);

        Assert.Equal(RatingStatus.Succeeded, (await rig.Service.ReportAsync(Day, Day, null)).Status);
        Assert.Null(rig.Store.LastQuery.Stores);
        Assert.Equal(RatingStatus.Succeeded, (await rig.Service.ReportAsync(Day, Day, rig.Shop.Id)).Status);
        Assert.Equal([rig.Shop.Id], rig.Store.LastQuery.Stores);
        Assert.Equal(RatingStatus.NotFound, (await rig.Service.ReportAsync(Day, Day, Guid.NewGuid())).Status);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.DeliveryPartner)]
    public async Task Customers_and_delivery_partners_cannot_read_the_ratings(Role role)
    {
        var rig = Create();
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, rig.Shop.Id, role);

        Assert.Equal(RatingStatus.Forbidden, (await rig.Service.ReportAsync(Day, Day, null)).Status);
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, null, Role.StoreStaff);
        Assert.Equal(RatingStatus.Forbidden, (await rig.Service.ReportAsync(Day, Day, null)).Status);
        rig.Who.Context = null;
        Assert.Equal(RatingStatus.Forbidden, (await rig.Service.ReportAsync(Day, Day, null)).Status);
    }

    [Fact]
    public async Task A_bad_range_is_refused()
    {
        var rig = Create();
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, null, Role.Admin);

        Assert.Equal(RatingStatus.Invalid, (await rig.Service.ReportAsync(Day, Day.AddDays(-1), null)).Status);
        Assert.Equal(RatingStatus.Invalid, (await rig.Service.ReportAsync(Day, Day.AddDays(100), null)).Status);
    }
}

[Collection("SqlServer")]
public sealed class RatingEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<Guid> OrderAsync(CheckoutEfIntegrationTests.World world, Guid user, Guid? store = null)
    {
        await using var db = world.Db();
        var order = new Order { OrderNumber = "RE-" + Guid.NewGuid().ToString("N")[..10], UserId = user, StoreId = store ?? world.Store.Id, Status = OrderStatus.Delivered, DeliveryAddress = "x", TotalAmount = 50 };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static OrderRating Rating(Guid order, Guid store, int stars, int? delivery = null, Guid? partner = null, string? comment = null, DateTime? at = null) =>
        new() { OrderId = order, StoreId = store, StoreStars = stars, DeliveryStars = delivery, DeliveryPartnerId = partner, Comment = comment, CreatedAt = at ?? DateTime.UtcNow, UpdatedAt = at ?? DateTime.UtcNow };

    [Fact]
    public async Task A_rating_is_saved_and_changed_in_place_and_the_database_refuses_nonsense_stars()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        Guid user, orderId;
        await using (var db = world.Db())
        {
            user = await db.Users.Where(item => item.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
        }

        orderId = await OrderAsync(world, user);
        try
        {
            await using (var db = world.Db())
            {
                await new EfRatingStore(db).SaveAsync(Rating(orderId, world.Store.Id, 2, comment: "slow"));
            }

            await using (var db = world.Db())
            {
                await new EfRatingStore(db).SaveAsync(Rating(orderId, world.Store.Id, 5, comment: null));
            }

            await using (var db = world.Db())
            {
                var saved = await new EfRatingStore(db).GetAsync(orderId);
                Assert.Equal((5, null), (saved!.StoreStars, saved.Comment));
                Assert.Equal(1, await db.OrderRatings.CountAsync(item => item.OrderId == orderId));
                db.ChangeTracker.Clear();
                await db.OrderRatings.Where(item => item.OrderId == orderId).ExecuteDeleteAsync();
                db.OrderRatings.Add(Rating(orderId, world.Store.Id, 6));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            await using var db = world.Db();
            await db.OrderRatings.Where(item => item.OrderId == orderId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Two_taps_at_once_leave_one_rating()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        Guid user;
        await using (var db = world.Db())
        {
            user = await db.Users.Where(item => item.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
        }

        var orderId = await OrderAsync(world, user);
        try
        {
            await Task.WhenAll(Enumerable.Range(1, 5).Select(async stars =>
            {
                await using var db = world.Db();
                await new EfRatingStore(db).SaveAsync(Rating(orderId, world.Store.Id, stars));
            }));

            await using var verify = world.Db();
            Assert.Equal(1, await verify.OrderRatings.CountAsync(item => item.OrderId == orderId));
        }
        finally
        {
            await using var db = world.Db();
            await db.OrderRatings.Where(item => item.OrderId == orderId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_summary_adds_up_averages_the_spread_and_each_partners_average_for_the_stores_and_window_asked()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        Guid user, partnerA, partnerB;
        var other = new Store { Name = "Other " + Guid.NewGuid().ToString("N")[..6], Address = "x", OrganizationId = world.Store.OrganizationId };
        var orders = new List<Guid>();
        try
        {
            await using (var db = world.Db())
            {
                user = await db.Users.Where(item => item.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
                var org = world.Store.OrganizationId;
                var a = new User { ExternalSubject = "pa-" + world.Token, DisplayName = "Anil", OrganizationId = org, Role = Role.DeliveryPartner };
                var b = new User { ExternalSubject = "pb-" + world.Token, DisplayName = "Bela", OrganizationId = org, Role = Role.DeliveryPartner };
                db.Users.AddRange(a, b);
                db.Stores.Add(other);
                await db.SaveChangesAsync();
                partnerA = a.Id;
                partnerB = b.Id;
            }

            var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddDays(10);
            var inside = from.AddDays(2);
            var rows = new (int Stars, int? Delivery, Guid? Partner, Guid Store, DateTime At)[]
            {
                (5, 5, partnerA, world.Store.Id, inside), (4, 3, partnerA, world.Store.Id, inside), (1, null, null, world.Store.Id, inside),
                (3, 4, partnerB, world.Store.Id, inside), (2, 2, partnerB, other.Id, inside), (5, 5, partnerA, world.Store.Id, from.AddDays(-1)), (5, 5, partnerA, world.Store.Id, to)
            };
            foreach (var row in rows)
            {
                var order = await OrderAsync(world, user, row.Store);
                orders.Add(order);
                await using var db = world.Db();
                await new EfRatingStore(db).SaveAsync(Rating(order, row.Store, row.Stars, row.Delivery, row.Partner, "comment " + row.Stars, row.At));
            }

            await using var read = world.Db();
            var store = new EfRatingStore(read);
            var mine = await store.SummaryAsync(world.Store.OrganizationId, [world.Store.Id], from, to);

            Assert.Equal(4, mine.Count);
            Assert.Equal(3.25m, mine.AverageStore);
            Assert.Equal(4m, mine.AverageDelivery);
            Assert.Equal([1, 0, 1, 1, 1], mine.Stars);
            Assert.Equal([("Anil", 2, 4m), ("Bela", 1, 4m)], mine.Partners.Select(item => (item.Partner, item.Count, item.Average)).OrderBy(item => item.Partner));

            var all = await store.SummaryAsync(world.Store.OrganizationId, null, from, to);
            Assert.Equal(5, all.Count);
            Assert.Equal(3.0m, all.AverageStore);
            var none = await store.SummaryAsync(Guid.NewGuid(), null, from, to);
            Assert.Equal((0, 0m, (decimal?)null), (none.Count, none.AverageStore, none.AverageDelivery));
            Assert.Equal([0, 0, 0, 0, 0], none.Stars);

            var recent = await store.RecentAsync(world.Store.OrganizationId, [world.Store.Id], from, to, 2);
            Assert.Equal(2, recent.Count);
            Assert.All(recent, item => Assert.Equal(world.Store.Name, item.Store));
            Assert.True(recent[0].CreatedAt >= recent[1].CreatedAt);
            Assert.Equal(2, (await store.RecentAsync(world.Store.OrganizationId, [world.Store.Id], from, to, 30)).Count(item => item.Comment is "comment 5" or "comment 4"));
        }
        finally
        {
            await using var db = world.Db();
            await db.OrderRatings.Where(item => orders.Contains(item.OrderId)).ExecuteDeleteAsync();
            await db.Orders.Where(item => orders.Contains(item.Id)).ExecuteDeleteAsync();
            await db.Users.Where(item => item.ExternalSubject == "pa-" + world.Token || item.ExternalSubject == "pb-" + world.Token).ExecuteDeleteAsync();
            await db.Stores.Where(item => item.Id == other.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Deleting_the_account_erases_the_comment_and_the_link_but_keeps_the_stars()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        Guid user, customer;
        await using (var db = world.Db())
        {
            user = await db.Users.Where(item => item.ExternalSubject == world.Emails[0]).Select(item => item.Id).SingleAsync();
            customer = await db.Customers.Where(item => item.UserId == user).Select(item => item.Id).SingleAsync();
        }

        var orderId = await OrderAsync(world, user);
        try
        {
            await using (var db = world.Db())
            {
                var rating = Rating(orderId, world.Store.Id, 4, comment: "private words");
                rating.CustomerId = customer;
                await new EfRatingStore(db).SaveAsync(rating);
            }

            await using (var db = world.Db())
            {
                Assert.Equal(DeleteOutcome.Deleted, await new EfAccountStore(db).DeleteCustomerAsync(user, DateTime.UtcNow));
            }

            await using var verify = world.Db();
            var left = await verify.OrderRatings.AsNoTracking().SingleAsync(item => item.OrderId == orderId);
            Assert.Equal((4, null, null), (left.StoreStars, left.Comment, left.CustomerId));
        }
        finally
        {
            await using var db = world.Db();
            await db.OrderRatings.Where(item => item.OrderId == orderId).ExecuteDeleteAsync();
        }
    }
}
