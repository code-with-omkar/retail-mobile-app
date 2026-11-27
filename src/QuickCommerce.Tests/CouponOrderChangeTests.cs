using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Coupons when a store changes the order: the discount shrinks with it, never grows, and the refund is what the customer actually paid less.</summary>
public sealed class CouponOrderChangeTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Staff = Guid.NewGuid();

    // Two lines: milk 2 x 50 = 100 and bread 1 x 100 = 100. Subtotal 200, delivery 0, handling 5. With 10% coupon (cap 15) the discount is 15 and the total 190.
    private static (Order Order, CouponRedemption Coupon, Payment Payment) Paid(string kind = CouponKinds.Percent, decimal value = 10, decimal? cap = 15, decimal delivery = 0)
    {
        var order = new Order
        {
            OrderNumber = "T-1", DeliveryAddress = "x", Status = OrderStatus.Accepted, SubtotalAmount = 200, DeliveryFee = delivery, HandlingFee = 5, PaymentMethod = PaymentMethods.Online, PaymentStatus = PaymentState.Paid,
            Items = [new OrderItem { ProductId = Guid.NewGuid(), VariantId = Guid.NewGuid(), ProductNameSnapshot = "Milk", UnitPrice = 50, Quantity = 2 }, new OrderItem { ProductId = Guid.NewGuid(), VariantId = Guid.NewGuid(), ProductNameSnapshot = "Bread", UnitPrice = 100, Quantity = 1 }],
        };
        var discount = CouponRules.Discount(kind, value, cap, 200, delivery);
        order.CouponCode = "SAVE";
        order.CouponDiscount = discount;
        order.TotalAmount = 200 + delivery + 5 - discount;
        var coupon = new CouponRedemption { CouponId = Guid.NewGuid(), OrderId = order.Id, UserId = order.UserId, Kind = kind, Value = value, MaxDiscount = cap, Amount = discount };
        var payment = new Payment { OrderId = order.Id, AmountPaise = (long)(order.TotalAmount * 100), Status = PaymentState.Paid };
        return (order, coupon, payment);
    }

    private static ItemChange To(Order order, int line, int quantity) => new(order.Items[line].ProductId, order.Items[line].VariantId, quantity);

    [Fact]
    public void Removing_the_bread_shrinks_a_percent_coupon_and_the_refund_is_the_difference_in_what_was_paid()
    {
        var (order, coupon, payment) = Paid(); // paid 190 (200 + 5 - 15)

        var result = OrderChangeRules.Apply(order, payment, [To(order, 1, 0)], null, Staff, Now, out var adjustment, coupon);

        Assert.Equal(OrderChangeStatus.Succeeded, result.Status);
        Assert.Equal(100m, order.SubtotalAmount);
        Assert.Equal(10m, order.CouponDiscount);       // 10% of 100
        Assert.Equal(10m, coupon.Amount);
        Assert.Equal(95m, order.TotalAmount);          // 100 + 5 - 10
        Assert.Equal(9500, adjustment!.RefundPaise);   // 190 - 95, not the 100 of the removed bread
        Assert.Equal(9500, payment.PartialRefundedPaise);
    }

    [Fact]
    public void Without_a_coupon_the_refund_is_the_removed_products_as_before()
    {
        var (order, _, payment) = Paid();
        order.CouponCode = null;
        order.CouponDiscount = 0;
        order.TotalAmount = 205;

        OrderChangeRules.Apply(order, payment, [To(order, 1, 0)], null, Staff, Now, out var adjustment);

        Assert.Equal(10000, adjustment!.RefundPaise);
        Assert.Equal(105m, order.TotalAmount);
    }

    [Fact]
    public void A_flat_coupon_stays_until_the_products_are_worth_less_than_it()
    {
        var (order, coupon, payment) = Paid(CouponKinds.Flat, 40, null);

        OrderChangeRules.Apply(order, payment, [To(order, 1, 0)], null, Staff, Now, out _, coupon);
        Assert.Equal(40m, order.CouponDiscount); // 100 of products still covers 40

        // Milk down to 1 leaves 50 of products, still more than 40; a second change to 30 would not be, so check the cap directly.
        Assert.Equal(30m, CouponRules.After(coupon, 30, 0));
    }

    [Fact]
    public void A_free_delivery_coupon_keeps_covering_the_delivery_fee()
    {
        var (order, coupon, payment) = Paid(CouponKinds.FreeDelivery, 0, null, delivery: 30); // 200 + 30 + 5 - 30 = 205

        OrderChangeRules.Apply(order, payment, [To(order, 1, 0)], null, Staff, Now, out var adjustment, coupon);

        Assert.Equal(30m, order.CouponDiscount);
        Assert.Equal(100m + 30 + 5 - 30, order.TotalAmount);
        Assert.Equal(10000, adjustment!.RefundPaise); // only the bread: the delivery stays free
    }

    [Fact]
    public void A_released_use_is_left_alone()
    {
        var (order, coupon, payment) = Paid();
        coupon.ReleasedAt = Now;

        OrderChangeRules.Apply(order, payment, [To(order, 1, 0)], null, Staff, Now, out _, coupon);

        Assert.Equal(15m, order.CouponDiscount);
    }

    [Fact]
    public void A_cash_order_with_a_coupon_has_nothing_to_refund_but_the_total_still_follows()
    {
        var (order, coupon, _) = Paid();
        order.PaymentMethod = PaymentMethods.CashOnDelivery;
        order.PaymentStatus = PaymentState.NotRequired;

        OrderChangeRules.Apply(order, null, [To(order, 1, 0)], null, Staff, Now, out var adjustment, coupon);

        Assert.Equal(95m, order.TotalAmount);
        Assert.Equal(0, adjustment!.RefundPaise);
    }

    [Fact]
    public void The_report_adds_up_the_coupon_cost_of_delivered_orders_only_and_the_stores_earnings_ignore_it()
    {
        var store = Guid.NewGuid();
        ReportOrderRow Row(OrderStatus status, decimal coupon) => new(Guid.NewGuid(), "T", store, Guid.NewGuid(), Now, status, PaymentMethods.CashOnDelivery, PaymentState.NotRequired, 100, 0, 5, 105 - coupon, 10, coupon);
        var rows = new[] { Row(OrderStatus.Delivered, 15), Row(OrderStatus.Delivered, 5), Row(OrderStatus.Cancelled, 20) };

        Assert.Equal(20m, ReportMath.Totals(rows, 0).CouponDiscount);
        var earnings = Assert.Single(ReportMath.Earnings(rows, new Dictionary<Guid, string> { [store] = "S" }));
        Assert.Equal(200m, earnings.Sales);        // product sales before the coupon
        Assert.Equal(20m, earnings.Commission);
        Assert.Equal(190m, earnings.Net);          // 200 - 20 + 10 fees: the business, not the store, paid the coupons
    }

    [Fact]
    public void The_commission_stays_on_the_products_not_reduced_by_the_coupon()
    {
        var (order, coupon, payment) = Paid();
        order.CommissionPercent = 10;
        order.CommissionAmount = 20;

        OrderChangeRules.Apply(order, payment, [To(order, 1, 0)], null, Staff, Now, out _, coupon);

        Assert.Equal(10m, order.CommissionAmount); // 10% of the remaining 100 of products
    }
}
