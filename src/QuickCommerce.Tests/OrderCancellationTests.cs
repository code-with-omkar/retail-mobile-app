using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class OrderCancellationTests
{
    [Fact]
    public void Only_an_accepted_order_that_is_not_delivered_can_be_cancelled_by_the_store()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            Assert.Equal(status is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Confirmed or OrderStatus.OutForDelivery, OrderCancellations.CanCancel(status));
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("DeliveryFailed", null, true)]
    [InlineData("Other", "", false)]
    [InlineData("Other", "Shop flooded", true)]
    [InlineData("Because", null, false)]
    public void Cancelling_needs_a_known_reason(string? reason, string? note, bool allowed) => Assert.Equal(allowed, OrderCancellations.Problem(reason, note) is null);

    [Fact]
    public void A_failed_delivery_hands_the_order_back_ready_with_the_reason()
    {
        var partner = Guid.NewGuid();
        var order = new Order { OrderNumber = "O-1", DeliveryAddress = "x", Status = OrderStatus.OutForDelivery, DeliveryPartnerId = partner, PickedUpAt = DateTime.UtcNow };

        Assert.Equal(Application.Interfaces.DeliveryChangeStatus.WrongState, DeliveryRules.Fail(order, Guid.NewGuid(), "WrongAddress", null, DateTime.UtcNow));
        Assert.Equal(Application.Interfaces.DeliveryChangeStatus.Succeeded, DeliveryRules.Fail(order, partner, "WrongAddress", "No such flat", DateTime.UtcNow));

        Assert.Equal((OrderStatus.Ready, null, "WrongAddress", "No such flat"), (order.Status, order.DeliveryPartnerId, order.DeliveryFailureReason, order.DeliveryFailureNote));
        Assert.NotNull(order.DeliveryFailedAt);
        Assert.Null(order.PickedUpAt);
    }

    [Fact]
    public void The_customer_is_told_why_in_their_language_and_not_the_remark()
    {
        var order = new Order { OrderNumber = "O-2", DeliveryAddress = "x", CancelReason = "DeliveryFailed", CancelNote = "gate locked" };
        var note = NotificationCatalog.ForStatus(Guid.NewGuid(), order, OrderStatus.Cancelled);

        Assert.Contains("We could not deliver it.", NotificationCatalog.Render(note, "en").Message);
        Assert.Contains("आम्ही ती पोहोचवू शकलो नाही.", NotificationCatalog.Render(note, "mr").Message);
        Assert.DoesNotContain("gate", note.Message + note.DataJson);
    }
}
