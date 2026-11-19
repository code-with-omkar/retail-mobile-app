using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P20 on a real SQL Server: the discount query, checkout inside its transaction, quiet offers and the Home banners. Opt-in like the other SQL tests (a DISPOSABLE database only).</summary>
[Collection("SqlServer")]
public sealed class OfferDiscountEfTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Campaign Offer(CheckoutEfIntegrationTests.World world, int percent, int days = 3, Guid[]? stores = null, bool notify = true, PaymentState payment = PaymentState.NotRequired, string? image = null) => new()
    {
        TitleEn = "Tomato week",
        BodyEn = notify ? "Fresh tomatoes for less." : string.Empty,
        StartsAt = Now.AddMinutes(-5),
        CreatedAt = Now.AddMinutes(-5),
        Notify = notify,
        DiscountPercent = percent,
        EndsAt = percent > 0 ? Now.AddDays(days) : null,
        ImageUrl = image,
        PaymentStatus = payment,
        Stores = (stores ?? [world.Store.Id]).Select(id => new CampaignStore { StoreId = id }).ToList(),
        Products = percent > 0 ? [new CampaignProduct { ProductId = world.Product.Id }] : []
    };

    private static async Task<Guid> Add(CheckoutEfIntegrationTests.World world, Campaign campaign)
    {
        await using var db = world.Db();
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task Remove(CheckoutEfIntegrationTests.World world, params Guid[] ids)
    {
        await using var db = world.Db();
        await db.Notifications.Where(note => note.CampaignId != null && ids.Contains(note.CampaignId.Value)).ExecuteDeleteAsync();
        await db.Campaigns.Where(campaign => ids.Contains(campaign.Id)).ExecuteDeleteAsync();
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> Percents(CheckoutEfIntegrationTests.World world, Guid? storeId = null)
    {
        await using var db = world.Db();
        return await new EfCampaignStore(db).GetPercentsAsync(storeId ?? world.Store.Id, [world.Product.Id], DateTime.UtcNow);
    }

    [Fact]
    public async Task The_discount_query_follows_the_rules_for_stores_clock_cancel_and_payment()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var ids = new List<Guid>();
        try
        {
            ids.Add(await Add(world, Offer(world, 10, stores: [])));
            Assert.Equal(10, (await Percents(world))[world.Product.Id]);

            ids.Add(await Add(world, Offer(world, 25)));
            ids.Add(await Add(world, Offer(world, 15)));
            Assert.Equal(25, (await Percents(world))[world.Product.Id]);

            // An offer that is cancelled, over, unpaid or for another store changes nothing.
            var cancelled = Offer(world, 60);
            cancelled.IsActive = false;
            var over = Offer(world, 60);
            over.EndsAt = Now.AddMinutes(-1);
            var later = Offer(world, 60);
            later.StartsAt = Now.AddHours(1);
            ids.Add(await Add(world, cancelled));
            ids.Add(await Add(world, over));
            ids.Add(await Add(world, later));
            ids.Add(await Add(world, Offer(world, 60, payment: PaymentState.Created)));
            ids.Add(await Add(world, Offer(world, 60, stores: [await OtherStore(world)])));
            Assert.Equal(25, (await Percents(world))[world.Product.Id]);

            // A paid fee turns an unpaid offer on.
            await using (var db = world.Db())
            {
                await db.Campaigns.Where(campaign => campaign.PaymentStatus == PaymentState.Created && campaign.TitleEn == "Tomato week").ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.PaymentStatus, PaymentState.Paid));
            }

            Assert.Equal(60, (await Percents(world))[world.Product.Id]);
        }
        finally
        {
            await Remove(world, ids.ToArray());
        }
    }

    private static async Task<Guid> OtherStore(CheckoutEfIntegrationTests.World world)
    {
        await using var db = world.Db();
        return await db.Stores.Where(store => store.Id != world.Store.Id).Select(store => store.Id).FirstAsync();
    }

    [Fact]
    public async Task Checkout_charges_the_discount_and_keeps_the_normal_price_as_the_receipt_mrp()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var id = await Add(world, Offer(world, 20));
        try
        {
            var customer = world.CustomerIds[0];

            // The cart still holds the old price, so checkout asks the customer first, naming the new price.
            var stale = await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null) with { Now = DateTime.UtcNow });
            Assert.Equal(CheckoutCommitStatus.PriceChanged, stale.Status);
            Assert.Equal((30m, 24m), (stale.Issues![0].OldPrice, stale.Issues[0].NewPrice));

            await using (var db = world.Db())
            {
                await db.CartItems.Where(item => item.ProductId == world.Product.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.UnitPriceSnapshot, 24m));
            }

            var ok = await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null) with { Now = DateTime.UtcNow });
            Assert.Equal(CheckoutCommitStatus.Succeeded, ok.Status);
            await using var read = world.Db();
            var item = await read.Orders.Where(order => order.StoreId == world.Store.Id).SelectMany(order => order.Items).SingleAsync();
            Assert.Equal((24m, 30m), (item.UnitPrice, item.UnitMrpSnapshot));
        }
        finally
        {
            await Remove(world, id);
        }
    }

    [Fact]
    public async Task A_discount_that_is_over_is_not_charged_even_though_the_cart_still_holds_it()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var over = Offer(world, 20);
        over.EndsAt = Now.AddMinutes(-1);
        var id = await Add(world, over);
        try
        {
            await using (var db = world.Db())
            {
                await db.CartItems.Where(item => item.ProductId == world.Product.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.UnitPriceSnapshot, 24m));
            }

            var result = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null) with { Now = DateTime.UtcNow });

            Assert.Equal(CheckoutCommitStatus.PriceChanged, result.Status);
            Assert.Equal((24m, 30m), (result.Issues![0].OldPrice, result.Issues[0].NewPrice));
        }
        finally
        {
            await Remove(world, id);
        }
    }

    [Fact]
    public async Task A_quiet_discount_is_marked_sent_without_notifying_anyone_and_a_message_still_notifies()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var quiet = await Add(world, Offer(world, 10, notify: false));
        var loud = await Add(world, Offer(world, 10));
        try
        {
            await using (var db = world.Db())
            {
                // The customer has a cart at the store, so they count as its customer.
                await new EfCampaignStore(db).PublishDueCampaignsAsync(DateTime.UtcNow);
            }

            await using var read = world.Db();
            var quietRow = await read.Campaigns.AsNoTracking().SingleAsync(campaign => campaign.Id == quiet);
            var loudRow = await read.Campaigns.AsNoTracking().SingleAsync(campaign => campaign.Id == loud);
            Assert.NotNull(quietRow.PublishedAt);
            Assert.Equal(0, quietRow.RecipientCount);
            Assert.Empty(await read.Notifications.Where(note => note.CampaignId == quiet).ToListAsync());
            Assert.NotNull(loudRow.PublishedAt);
            Assert.True(loudRow.RecipientCount >= 1);
            Assert.NotEmpty(await read.Notifications.Where(note => note.CampaignId == loud).ToListAsync());
        }
        finally
        {
            await Remove(world, quiet, loud);
        }
    }

    [Fact]
    public async Task Home_banners_match_the_rule_and_ending_a_discount_works_once()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var discount = await Add(world, Offer(world, 20, image: "/media/offers/a.png"));
        var message = await Add(world, Offer(world, 0, image: "/media/offers/b.png"));
        var plain = await Add(world, Offer(world, 0));
        var old = Offer(world, 0, image: "/media/offers/c.png");
        old.StartsAt = Now.AddDays(-8);
        var oldId = await Add(world, old);
        try
        {
            await using (var db = world.Db())
            {
                var shown = await new EfCampaignStore(db).GetActiveOffersAsync(world.Store.Id, DateTime.UtcNow);
                Assert.Equal(new[] { discount, message, plain }.Order(), shown.Select(offer => offer.Id).Order());
                Assert.Equal([world.Product.Id], shown.Single(offer => offer.Id == discount).ProductIds);
                Assert.Equal("/media/offers/a.png", shown.Single(offer => offer.Id == discount).ImageUrl);
            }

            await using (var db = world.Db())
            {
                var ended = await new EfCampaignStore(db).TryEndDiscountAsync(discount, DateTime.UtcNow);
                Assert.True(ended!.EndsAt <= DateTime.UtcNow);
            }

            Assert.Empty(await Percents(world));
            await using var again = world.Db();
            Assert.DoesNotContain(await new EfCampaignStore(again).GetActiveOffersAsync(world.Store.Id, DateTime.UtcNow), offer => offer.Id == discount);
        }
        finally
        {
            await Remove(world, discount, message, plain, oldId);
        }
    }

    [Fact]
    public async Task The_database_refuses_a_discount_with_no_end_or_over_90_percent()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var noEnd = Offer(world, 10);
        noEnd.EndsAt = null;
        var tooMuch = Offer(world, 91);

        foreach (var bad in new[] { noEnd, tooMuch })
        {
            await using var db = world.Db();
            db.Campaigns.Add(bad);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
