using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P25: the store offers a replacement for an item it cannot supply; the customer accepts or declines within 5 minutes.</summary>
public sealed class ReplacementTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

    private static Order Packing(bool online = false)
    {
        var order = new Order { OrderNumber = "R-1", UserId = Guid.NewGuid(), StoreId = Guid.NewGuid(), DeliveryAddress = "x", Status = OrderStatus.Accepted, DeliveryFee = 25, HandlingFee = 5, PaymentMethod = online ? PaymentMethods.Online : PaymentMethods.CashOnDelivery, CommissionPercent = 10 };
        order.Items.Add(new OrderItem { ProductId = Guid.NewGuid(), VariantId = Guid.NewGuid(), ProductNameSnapshot = "Milk", VariantLabelSnapshot = "1 l", UnitPrice = 34, Quantity = 3 });
        order.Items.Add(new OrderItem { ProductId = Guid.NewGuid(), VariantId = Guid.NewGuid(), ProductNameSnapshot = "Rice", VariantLabelSnapshot = "1 kg", UnitPrice = 89, Quantity = 1 });
        order.SubtotalAmount = 191;
        order.TotalAmount = 221;
        order.CommissionAmount = CommissionMath.Amount(191, 10);
        return order;
    }

    private static (Product Product, ProductVariant Variant, StoreVariantInventory Stock) Candidate(decimal price, int stock = 10, bool active = true)
    {
        var product = new Product { Sku = "R", Name = "Oat Milk", Description = "x", Price = price, UnitOfMeasure = "1 l", CategoryId = Guid.NewGuid(), IsActive = active };
        var variant = new ProductVariant { ProductId = product.Id, Sku = "R-1", Label = "1 l", Price = price, IsDefault = true, IsActive = active };
        return (product, variant, new StoreVariantInventory { VariantId = variant.Id, AvailableQuantity = stock });
    }

    private static ProposeReplacementRequest Ask(Order order, ProductVariant variant, int quantity = 3) => new(order.Items[0].ProductId, order.Items[0].VariantId, variant.ProductId, variant.Id, quantity);

    // ---------------- proposing ----------------

    [Fact]
    public void A_proposal_is_priced_at_the_store_price_less_an_offer_and_open_for_five_minutes()
    {
        var order = Packing();
        var (product, variant, stock) = Candidate(50);
        stock.PriceOverride = 40;

        var commit = ReplacementRules.Propose(order, null, Ask(order, variant, 2), product, variant, stock, offerPercent: 25, Staff, Now, ReplacementRules.Window);

        Assert.Equal(ReplacementOutcome.Applied, commit.Outcome);
        var proposal = commit.Proposal!;
        Assert.Equal(30m, proposal.NewUnitPrice); // 40 less 25%
        Assert.Equal((2, 3, 34m), (proposal.NewQuantity, proposal.OldQuantity, proposal.OldUnitPrice));
        Assert.Equal(Now.AddMinutes(5), proposal.ExpiresAt);
        Assert.Equal("Pending", proposal.Status);
        Assert.Equal(30m * 2 - 34m * 3, ReplacementRules.ToInfo(proposal).PriceChange);
    }

    [Fact]
    public void A_dearer_replacement_is_refused_on_an_order_paid_online_but_allowed_for_cash()
    {
        var order = Packing(online: true);
        var cash = Packing();
        var (product, variant, stock) = Candidate(60);
        var paid = new Payment { Status = PaymentState.Paid };

        var online = ReplacementRules.Propose(order, paid, Ask(order, variant), product, variant, stock, 0, Staff, Now, ReplacementRules.Window);
        var onDelivery = ReplacementRules.Propose(cash, null, Ask(cash, variant), product, variant, stock, 0, Staff, Now, ReplacementRules.Window);

        Assert.Equal((ReplacementOutcome.Conflict, ReplacementReasons.OnlinePaidDearer), (online.Outcome, online.Reason));
        Assert.Equal(ReplacementOutcome.Applied, onDelivery.Outcome);
    }

    [Fact]
    public void Bad_proposals_are_refused()
    {
        var order = Packing();
        var (product, variant, stock) = Candidate(30);
        var wrongLine = Ask(order, variant) with { ProductId = Guid.NewGuid() };
        var sameVariantRow = new ProductVariant { Id = order.Items[0].VariantId!.Value, ProductId = product.Id, Sku = "S", Label = "1 l", Price = 30, IsActive = true };
        var sameVariant = Ask(order, sameVariantRow);

        Assert.Equal(ReplacementOutcome.Invalid, ReplacementRules.Propose(order, null, wrongLine, product, variant, stock, 0, Staff, Now, ReplacementRules.Window).Outcome);
        Assert.Equal(ReplacementOutcome.Invalid, ReplacementRules.Propose(order, null, Ask(order, variant, 0), product, variant, stock, 0, Staff, Now, ReplacementRules.Window).Outcome);
        Assert.Equal(ReplacementOutcome.Invalid, ReplacementRules.Propose(order, null, Ask(order, variant, 100), product, variant, stock, 0, Staff, Now, ReplacementRules.Window).Outcome);
        Assert.Equal(ReplacementOutcome.Invalid, ReplacementRules.Propose(order, null, Ask(order, variant), null, variant, stock, 0, Staff, Now, ReplacementRules.Window).Outcome);
        var (off, offVariant, offStock) = Candidate(30, active: false);
        Assert.Equal(ReplacementOutcome.Invalid, ReplacementRules.Propose(order, null, Ask(order, offVariant), off, offVariant, offStock, 0, Staff, Now, ReplacementRules.Window).Outcome);
        Assert.Equal(ReplacementOutcome.Conflict, ReplacementRules.Propose(order, null, Ask(order, variant, 11), product, variant, stock, 0, Staff, Now, ReplacementRules.Window).Reason == ReplacementReasons.OutOfStock ? ReplacementOutcome.Conflict : ReplacementOutcome.Applied);
        Assert.Equal(ReplacementOutcome.Conflict, ReplacementRules.Propose(order, null, Ask(order, variant), product, variant, null, 0, Staff, Now, ReplacementRules.Window).Outcome);
        Assert.Equal(ReplacementOutcome.Invalid, ReplacementRules.Propose(order, null, sameVariant, product, sameVariantRow, stock, 0, Staff, Now, ReplacementRules.Window).Outcome);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    public void Only_an_accepted_or_packing_order_gets_a_replacement(OrderStatus status)
    {
        var order = Packing();
        order.Status = status;
        var (product, variant, stock) = Candidate(30);

        Assert.Equal(ReplacementOutcome.WrongState, ReplacementRules.Propose(order, null, Ask(order, variant), product, variant, stock, 0, Staff, Now, ReplacementRules.Window).Outcome);
    }

    // ---------------- accepting ----------------

    private static ReplacementProposal Offer(Order order, ProductVariant variant, Product product, StoreVariantInventory stock, int quantity = 3, bool online = false) =>
        ReplacementRules.Propose(order, online ? new Payment { Status = PaymentState.Paid } : null, Ask(order, variant, quantity), product, variant, stock, 0, Staff, Now, ReplacementRules.Window).Proposal!;

    [Fact]
    public void Accepting_swaps_the_line_takes_the_replacements_stock_and_recalculates_with_the_fees_kept()
    {
        var order = Packing();
        var (product, variant, stock) = Candidate(40);
        var proposal = Offer(order, variant, product, stock);

        var commit = ReplacementRules.Accept(order, null, proposal, stock, order.UserId, Now.AddMinutes(2));

        Assert.Equal(ReplacementOutcome.Applied, commit.Outcome);
        var line = order.Items[0];
        Assert.Equal(("Oat Milk", 3, 40m, variant.Id), (line.ProductNameSnapshot, line.Quantity, line.UnitPrice, line.VariantId));
        Assert.Equal(7, stock.AvailableQuantity);
        Assert.Equal(40m * 3 + 89m, order.SubtotalAmount);
        Assert.Equal(order.SubtotalAmount + 30m, order.TotalAmount);
        Assert.Equal(CommissionMath.Amount(order.SubtotalAmount, 10), order.CommissionAmount);
        Assert.Equal("Accepted", proposal.Status);
        Assert.Equal((0L, "None"), (commit.Adjustment!.RefundPaise, commit.Adjustment.RefundStatus)); // cash: the partner collects the new total
    }

    [Fact]
    public void A_cheaper_replacement_on_a_paid_order_queues_a_refund_of_the_saving()
    {
        var order = Packing(online: true);
        var payment = new Payment { OrderId = order.Id, Status = PaymentState.Paid, AmountPaise = 22100 };
        var (product, variant, stock) = Candidate(30);
        var proposal = Offer(order, variant, product, stock, online: true);

        var commit = ReplacementRules.Accept(order, payment, proposal, stock, order.UserId, Now.AddMinutes(1));

        Assert.Equal((1200L, "Pending"), (commit.Adjustment!.RefundPaise, commit.Adjustment.RefundStatus)); // 3 x (34 - 30)
        Assert.Equal(1200L, payment.PartialRefundedPaise);
    }

    [Fact]
    public void An_answer_after_the_time_an_answer_twice_and_a_vanished_stock_are_refused()
    {
        var order = Packing();
        var (product, variant, stock) = Candidate(40);
        var proposal = Offer(order, variant, product, stock);

        Assert.Equal(ReplacementReasons.Expired, ReplacementRules.Accept(order, null, proposal, stock, order.UserId, Now.AddMinutes(6)).Reason);
        stock.AvailableQuantity = 1;
        Assert.Equal(ReplacementReasons.OutOfStock, ReplacementRules.Accept(order, null, proposal, stock, order.UserId, Now.AddMinutes(1)).Reason);
        stock.AvailableQuantity = 10;
        Assert.Equal(ReplacementOutcome.Applied, ReplacementRules.Accept(order, null, proposal, stock, order.UserId, Now.AddMinutes(1)).Outcome);
        Assert.Equal(ReplacementOutcome.WrongState, ReplacementRules.Accept(order, null, proposal, stock, order.UserId, Now.AddMinutes(1)).Outcome);
    }

    // ---------------- SQL ----------------

    private sealed record Setup(CheckoutEfIntegrationTests.World World, Guid OrderId, Guid Organization, Guid StoreId, Guid UserId, Product Product, ProductVariant Variant);

    private static async Task<Setup> Prepare(CheckoutEfIntegrationTests.World world, decimal price = 20)
    {
        var placed = await world.Checkout(world.CustomerIds[0], CheckoutEfIntegrationTests.Commit(null) with { CommissionPercent = 10m });
        Assert.Equal(CheckoutCommitStatus.Succeeded, placed.Status);
        var orderId = placed.Order!.Id;
        await using var db = world.Db();
        var organization = await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync();
        var userId = await db.Orders.Where(order => order.Id == orderId).Select(order => order.UserId).SingleAsync();
        await db.Orders.Where(order => order.Id == orderId).ExecuteUpdateAsync(set => set.SetProperty(order => order.Status, OrderStatus.Accepted));
        var product = new Product { Sku = "RPL-" + world.Token, Name = "Replacement " + world.Token, Description = "x", Price = price, UnitOfMeasure = "1 kg", CategoryId = world.Category.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = new ProductVariant { ProductId = product.Id, Sku = product.Sku, Label = "1 kg", Price = price, IsDefault = true };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        db.StoreVariantInventory.Add(new StoreVariantInventory { StoreId = world.Store.Id, VariantId = variant.Id, AvailableQuantity = 10 });
        await db.SaveChangesAsync();
        return new Setup(world, orderId, organization, world.Store.Id, userId, product, variant);
    }

    private static async Task Cleanup(Setup s)
    {
        await using var db = s.World.Db();
        await db.Notifications.Where(note => note.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.ReplacementProposals.Where(item => item.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.OrderAdjustments.Where(item => item.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.CheckoutRequests.Where(item => item.OrderId == s.OrderId).ExecuteDeleteAsync();
        await db.OrderItems.Where(item => EF.Property<Guid>(item, "OrderId") == s.OrderId).ExecuteDeleteAsync();
        await db.OrderStatusHistory.Where(item => EF.Property<Guid>(item, "OrderId") == s.OrderId).ExecuteDeleteAsync();
        await db.Orders.Where(order => order.Id == s.OrderId).ExecuteDeleteAsync();
        await db.StoreVariantInventory.Where(row => row.VariantId == s.Variant.Id).ExecuteDeleteAsync();
        await db.ProductVariants.Where(variant => variant.Id == s.Variant.Id).ExecuteDeleteAsync();
        await db.Products.Where(product => product.Id == s.Product.Id).ExecuteDeleteAsync();
    }

    private static ProposeReplacementRequest Request(Setup s, int quantity = 3) => new(s.World.Product.Id, s.World.Kilo.Id, s.Product.Id, s.Variant.Id, quantity);

    [Fact]
    public async Task On_SQL_a_proposal_can_be_accepted_declined_or_left_to_run_out_and_only_one_is_open()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 20, cartQuantity: 3);
        var s = await Prepare(world);
        try
        {
            // proposing
            ReplacementCommit first;
            await using (var db = world.Db())
            {
                first = await new EfReplacementStore(db).ProposeAsync(s.OrderId, s.Organization, s.StoreId, Staff, Request(s), Now, ReplacementRules.Window);
            }

            Assert.Equal(ReplacementOutcome.Applied, first.Outcome);
            await using (var db = world.Db())
            {
                var second = await new EfReplacementStore(db).ProposeAsync(s.OrderId, s.Organization, s.StoreId, Staff, Request(s, 1), Now, ReplacementRules.Window);
                Assert.Equal(ReplacementReasons.AlreadyOpen, second.Reason);
                Assert.Equal(ReplacementOutcome.NotFound, (await new EfReplacementStore(db).ProposeAsync(s.OrderId, s.Organization, Guid.NewGuid(), Staff, Request(s, 1), Now, ReplacementRules.Window)).Outcome);
                Assert.True(await db.Notifications.AnyAsync(note => note.OrderId == s.OrderId && note.Type == NotificationTypes.ReplacementProposed));
            }

            // another customer cannot answer
            await using (var db = world.Db())
            {
                Assert.Equal(ReplacementOutcome.NotFound, (await new EfReplacementStore(db).AnswerAsync(s.OrderId, first.Proposal!.Id, Guid.NewGuid(), true, Now.AddMinutes(1))).Outcome);
            }

            // accepting swaps the line and takes the replacement's stock
            await using (var db = world.Db())
            {
                var accepted = await new EfReplacementStore(db).AnswerAsync(s.OrderId, first.Proposal!.Id, s.UserId, true, Now.AddMinutes(1));
                Assert.Equal(ReplacementOutcome.Applied, accepted.Outcome);
            }

            await using (var verify = world.Db())
            {
                var order = await verify.Orders.AsNoTracking().Include(item => item.Items).SingleAsync(item => item.Id == s.OrderId);
                var line = Assert.Single(order.Items);
                Assert.Equal((s.Product.Name, 3, 20m), (line.ProductNameSnapshot, line.Quantity, line.UnitPrice));
                Assert.Equal(60m, order.SubtotalAmount);
                Assert.Equal(7, (await verify.StoreVariantInventory.AsNoTracking().SingleAsync(row => row.VariantId == s.Variant.Id && row.StoreId == s.StoreId)).AvailableQuantity);
                Assert.Equal("Accepted", (await verify.ReplacementProposals.AsNoTracking().SingleAsync(item => item.Id == first.Proposal!.Id)).Status);
            }
        }
        finally
        {
            await Cleanup(s);
        }
    }

    [Fact]
    public async Task On_SQL_a_declined_or_overdue_proposal_removes_the_item_or_leaves_a_lone_item_alone()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 20, cartQuantity: 3);
        var s = await Prepare(world);
        try
        {
            // The order has one line only, so declining or expiring cannot remove it (the store cancels instead): the proposal just closes.
            await using (var db = world.Db())
            {
                var proposed = await new EfReplacementStore(db).ProposeAsync(s.OrderId, s.Organization, s.StoreId, Staff, Request(s), Now, ReplacementRules.Window);
                Assert.Equal(ReplacementOutcome.Applied, proposed.Outcome);
                var declined = await new EfReplacementStore(db).AnswerAsync(s.OrderId, proposed.Proposal!.Id, s.UserId, false, Now.AddMinutes(1));
                Assert.Equal(ReplacementOutcome.Applied, declined.Outcome);
            }

            await using (var verify = world.Db())
            {
                Assert.Single(await verify.OrderItems.AsNoTracking().Where(item => EF.Property<Guid>(item, "OrderId") == s.OrderId).ToListAsync());
                Assert.Equal("Declined", (await verify.ReplacementProposals.AsNoTracking().SingleAsync(item => item.OrderId == s.OrderId)).Status);
            }

            // a new proposal left unanswered past its time is closed by the job
            await using (var db = world.Db())
            {
                await new EfReplacementStore(db).ProposeAsync(s.OrderId, s.Organization, s.StoreId, Staff, Request(s, 2), Now, ReplacementRules.Window);
                Assert.Equal(0, await new EfReplacementStore(db).ExpireDueAsync(Now.AddMinutes(4), 10));
                Assert.Equal(1, await new EfReplacementStore(db).ExpireDueAsync(Now.AddMinutes(6), 10));
            }

            await using (var verify = world.Db())
            {
                Assert.Equal(["Declined", "Expired"], (await verify.ReplacementProposals.AsNoTracking().Where(item => item.OrderId == s.OrderId).Select(item => item.Status).ToListAsync()).Order());
            }
        }
        finally
        {
            await Cleanup(s);
        }
    }

    [Fact]
    public async Task On_SQL_a_dearer_replacement_on_a_cash_order_is_accepted_and_the_total_goes_up()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 20, cartQuantity: 3);
        var s = await Prepare(world, price: 45);
        try
        {
            ReplacementCommit proposed;
            await using (var db = world.Db())
            {
                proposed = await new EfReplacementStore(db).ProposeAsync(s.OrderId, s.Organization, s.StoreId, Staff, Request(s), Now, ReplacementRules.Window);
            }

            Assert.Equal(ReplacementOutcome.Applied, proposed.Outcome);
            await using (var db = world.Db())
            {
                Assert.Equal(ReplacementOutcome.Applied, (await new EfReplacementStore(db).AnswerAsync(s.OrderId, proposed.Proposal!.Id, s.UserId, true, Now.AddMinutes(1))).Outcome);
            }

            await using var verify = world.Db();
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == s.OrderId);
            Assert.Equal(135m, order.SubtotalAmount); // 3 x 45 instead of 3 x 30
            var adjustment = await verify.OrderAdjustments.AsNoTracking().SingleAsync(item => item.OrderId == s.OrderId);
            Assert.Equal((0L, "None", 90m, 135m), (adjustment.RefundPaise, adjustment.RefundStatus, adjustment.OldSubtotal, adjustment.NewSubtotal));
        }
        finally
        {
            await Cleanup(s);
        }
    }
}
