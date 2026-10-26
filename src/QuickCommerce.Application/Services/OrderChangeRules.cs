using System.Text.Json;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// What removing or reducing items does to an order, written once on the entities. The SQL store applies it inside its transaction.
/// Delivery and handling fees stay as the customer saw them; the commission is taken again on the new product total; stock is not
/// returned (the item was out of stock, so the store corrects its own count).
/// </summary>
public static class OrderChangeRules
{
    public const int MaxChanges = 50;
    public const int ReasonMax = 200;

    public static OrderChangeCommit Apply(Order order, Payment? payment, IReadOnlyList<ItemChange> changes, string? reason, Guid changedBy, DateTime now, out OrderAdjustment? adjustment)
    {
        adjustment = null;
        if (order.Status is not (OrderStatus.Accepted or OrderStatus.Preparing))
        {
            return new(OrderChangeStatus.WrongState, "Items can only be changed while the order is accepted or being packed.");
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is not null && (cleanReason.Length > ReasonMax || cleanReason.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n'))))
        {
            return new(OrderChangeStatus.Invalid, cleanReason.Length > ReasonMax ? $"A reason can be up to {ReasonMax} characters." : "The reason cannot contain < or > or control characters.");
        }

        if (changes.Count is 0 or > MaxChanges)
        {
            return new(OrderChangeStatus.Invalid, $"Change between 1 and {MaxChanges} lines.");
        }

        var wanted = new List<(OrderItem Line, int To)>();
        foreach (var change in changes)
        {
            var matches = order.Items.Where(item => item.ProductId == change.ProductId && (change.VariantId is null || item.VariantId == change.VariantId)).ToArray();
            if (matches.Length != 1 || wanted.Any(entry => entry.Line == matches[0]))
            {
                return new(OrderChangeStatus.Invalid, matches.Length == 0 ? "One of the items is not in this order." : "Name each item once, with its pack size when there are several.");
            }

            if (change.Quantity < 0 || change.Quantity >= matches[0].Quantity)
            {
                return new(OrderChangeStatus.Invalid, "An item can only be reduced: give a quantity below what was ordered (0 removes it).");
            }

            wanted.Add((matches[0], change.Quantity));
        }

        if (order.Items.All(item => wanted.Any(entry => entry.Line == item && entry.To == 0)))
        {
            return new(OrderChangeStatus.Invalid, "That removes every item. Decline or cancel the order instead.", OrderChangeReasons.UseCancel);
        }

        var oldSubtotal = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var lines = wanted.Select(entry => new ChangedLine(entry.Line.ProductNameSnapshot, entry.Line.VariantLabelSnapshot, entry.Line.Quantity, entry.To, entry.Line.UnitPrice)).ToArray();
        foreach (var (line, to) in wanted)
        {
            line.Quantity = to;
        }

        var newSubtotal = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var removed = oldSubtotal - newSubtotal;
        order.SubtotalAmount = newSubtotal;
        order.TotalAmount = newSubtotal + order.DeliveryFee + order.HandlingFee;
        if (order.CommissionPercent is { } percent)
        {
            order.CommissionAmount = CommissionMath.Amount(newSubtotal, percent);
        }

        // A paid online order gets the removed amount back to the original payment; the difference is also taken off what a later full refund would give back.
        var refundPaise = 0L;
        var refundStatus = "None";
        if (payment is { Status: PaymentState.Paid })
        {
            refundPaise = (long)Math.Round(removed * 100m, 0, MidpointRounding.AwayFromZero);
            payment.PartialRefundedPaise += refundPaise;
            payment.UpdatedAt = now;
            refundStatus = refundPaise > 0 ? "Pending" : "None";
        }

        adjustment = new OrderAdjustment
        {
            OrderId = order.Id,
            ChangedBy = changedBy,
            CreatedAt = now,
            Reason = cleanReason,
            ChangesJson = JsonSerializer.Serialize(lines),
            OldSubtotal = oldSubtotal,
            NewSubtotal = newSubtotal,
            RefundPaise = refundPaise,
            RefundStatus = refundStatus
        };
        return new(OrderChangeStatus.Succeeded, Adjustment: adjustment, OrderNumber: order.OrderNumber, NewTotal: order.TotalAmount);
    }

    public static OrderChangeInfo ToInfo(OrderAdjustment adjustment) => new(
        DateTime.SpecifyKind(adjustment.CreatedAt, DateTimeKind.Utc),
        JsonSerializer.Deserialize<ChangedLine[]>(adjustment.ChangesJson) ?? [],
        adjustment.RefundPaise / 100m,
        adjustment.RefundStatus,
        adjustment.Reason);
}
