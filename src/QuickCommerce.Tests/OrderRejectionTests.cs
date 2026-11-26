using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class OrderRejectionTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("Because", null, false)]
    [InlineData("OutOfStock", null, true)]
    [InlineData("Other", "  ", false)]
    [InlineData("Other", "Power cut", true)]
    [InlineData("TooBusy", "<b>", false)]
    public void Rejecting_needs_a_known_reason_and_a_remark_for_other(string? reason, string? note, bool allowed) =>
        Assert.Equal(allowed, new OrderLifecycleRequestValidator().Validate(new ChangeOrderStatusRequest(OrderStatus.Rejected, reason, note)).IsValid);

    [Fact]
    public void Other_steps_need_no_reason() => Assert.True(new OrderLifecycleRequestValidator().Validate(new ChangeOrderStatusRequest(OrderStatus.Accepted)).IsValid);

    [Fact]
    public void The_customer_is_told_the_reason_in_their_language_and_never_the_remark()
    {
        var order = new Order { OrderNumber = "ORD-9", DeliveryAddress = "x", RejectionReason = "StoreClosed", RejectionNote = "keys lost" };
        var note = NotificationCatalog.ForStatus(Guid.NewGuid(), order, OrderStatus.Rejected);

        var english = NotificationCatalog.Render(note, "en").Message;
        Assert.Contains("The store is closed right now.", english);
        Assert.DoesNotContain("keys", english + note.Message + note.DataJson);
        Assert.Contains("दुकान सध्या बंद आहे.", NotificationCatalog.Render(note, "mr").Message);
    }

    [Fact]
    public void The_report_counts_rejections_and_reasons_by_store()
    {
        var store = Guid.NewGuid();
        ReportOrderRow Row(OrderStatus status, string? reason = null) => new(Guid.NewGuid(), "n", store, Guid.NewGuid(), DateTime.UtcNow, status, PaymentMethods.CashOnDelivery, PaymentState.NotRequired, 1, 0, 0, 1, RejectionReason: reason);
        var rows = new[] { Row(OrderStatus.Delivered), Row(OrderStatus.Rejected, "OutOfStock"), Row(OrderStatus.Rejected, "OutOfStock"), Row(OrderStatus.Rejected) };

        var result = Assert.Single(ReportMath.Rejections(rows, new Dictionary<Guid, string> { [store] = "Harbor" }));

        Assert.Equal((4, 3, 75m), (result.Orders, result.Rejected, result.Percent));
        Assert.Equal([new ReasonCount("OutOfStock", 2), new ReasonCount(OrderRejections.NotRecorded, 1)], result.Reasons);
    }
}
