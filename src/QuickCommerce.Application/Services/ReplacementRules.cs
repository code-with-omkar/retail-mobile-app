using System.Text.Json;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// Replacing an item the store cannot supply. Proposing prices the replacement at the store's price now; accepting swaps the line, takes the
/// replacement's stock, recalculates the totals (fees stay) and refunds a saving on a paid order. Declining or letting it run out removes the item.
/// </summary>
public static class ReplacementRules
{
    public const int MaxQuantity = 99;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static ReplacementCommit Propose(Order order, Payment? payment, ProposeReplacementRequest request, Product? product, ProductVariant? variant, StoreVariantInventory? stock, int offerPercent, Guid proposedBy, DateTime now, TimeSpan window)
    {
        if (order.Status is not (OrderStatus.Accepted or OrderStatus.Preparing))
        {
            return new(ReplacementOutcome.WrongState, "A replacement can only be offered while the order is accepted or being packed.");
        }

        var lines = order.Items.Where(item => item.ProductId == request.ProductId && (request.VariantId is null || item.VariantId == request.VariantId)).ToArray();
        if (lines.Length != 1)
        {
            return new(ReplacementOutcome.Invalid, lines.Length == 0 ? "That item is not in this order." : "Name the pack size of the item.");
        }

        var line = lines[0];
        if (request.Quantity is < 1 or > MaxQuantity)
        {
            return new(ReplacementOutcome.Invalid, $"Offer between 1 and {MaxQuantity} of the replacement.");
        }

        if (product is not { IsActive: true } || variant is not { IsActive: true } || variant.ProductId != product.Id)
        {
            return new(ReplacementOutcome.Invalid, "That replacement is not available.");
        }

        if (variant.Id == line.VariantId)
        {
            return new(ReplacementOutcome.Invalid, "Choose a different product or size as the replacement.");
        }

        if (stock is null || stock.AvailableQuantity < request.Quantity)
        {
            return new(ReplacementOutcome.Conflict, "The store does not have enough of the replacement.", ReplacementReasons.OutOfStock);
        }

        var unitPrice = StorePricing.Final(variant.Price, stock.PriceOverride, offerPercent);
        var change = unitPrice * request.Quantity - line.UnitPrice * line.Quantity;
        if (change > 0 && payment is { Status: PaymentState.Paid })
        {
            return new(ReplacementOutcome.Conflict, "This order is paid online, so a dearer replacement cannot be charged. Choose one that costs the same or less, or remove the item.", ReplacementReasons.OnlinePaidDearer);
        }

        var proposal = new ReplacementProposal
        {
            OrderId = order.Id,
            LineProductId = line.ProductId,
            LineVariantId = line.VariantId,
            LineName = line.ProductNameSnapshot,
            LineLabel = line.VariantLabelSnapshot,
            OldQuantity = line.Quantity,
            OldUnitPrice = line.UnitPrice,
            NewProductId = product.Id,
            NewVariantId = variant.Id,
            NewName = product.Name,
            NewLabel = variant.Label,
            NewQuantity = request.Quantity,
            NewUnitPrice = unitPrice,
            NewUnitMrp = StorePricing.MrpSnapshot(unitPrice, variant.Price, variant.Mrp),
            ProposedBy = proposedBy,
            CreatedAt = now,
            ExpiresAt = now + window
        };
        return new(ReplacementOutcome.Applied, Proposal: proposal);
    }

    /// <summary>The customer accepted: swap the line. Changes the order, the replacement's stock and (for a saving on a paid order) queues a refund.</summary>
    public static ReplacementCommit Accept(Order order, Payment? payment, ReplacementProposal proposal, StoreVariantInventory? stock, Guid decidedBy, DateTime now)
    {
        if (proposal.Status != "Pending")
        {
            return new(ReplacementOutcome.WrongState, "That replacement has already been answered.");
        }

        if (now > proposal.ExpiresAt)
        {
            return new(ReplacementOutcome.Conflict, "That replacement has run out of time.", ReplacementReasons.Expired);
        }

        if (order.Status is not (OrderStatus.Accepted or OrderStatus.Preparing))
        {
            return new(ReplacementOutcome.WrongState, "The order is no longer being packed.");
        }

        var line = order.Items.SingleOrDefault(item => item.ProductId == proposal.LineProductId && item.VariantId == proposal.LineVariantId);
        if (line is null)
        {
            return new(ReplacementOutcome.Conflict, "The item was changed in the meantime.");
        }

        if (stock is null || stock.AvailableQuantity < proposal.NewQuantity)
        {
            return new(ReplacementOutcome.Conflict, "The store no longer has enough of the replacement.", ReplacementReasons.OutOfStock);
        }

        var oldSubtotal = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var removed = new ChangedLine(line.ProductNameSnapshot, line.VariantLabelSnapshot, line.Quantity, 0, line.UnitPrice);
        var added = new ChangedLine(proposal.NewName, proposal.NewLabel, 0, proposal.NewQuantity, proposal.NewUnitPrice);
        line.ProductId = proposal.NewProductId;
        line.VariantId = proposal.NewVariantId;
        line.ProductNameSnapshot = proposal.NewName;
        line.VariantLabelSnapshot = proposal.NewLabel;
        line.UnitPrice = proposal.NewUnitPrice;
        line.UnitMrpSnapshot = proposal.NewUnitMrp;
        line.Quantity = proposal.NewQuantity;
        stock.AvailableQuantity -= proposal.NewQuantity;
        var newSubtotal = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        order.SubtotalAmount = newSubtotal;
        order.TotalAmount = newSubtotal + order.DeliveryFee + order.HandlingFee;
        if (order.CommissionPercent is { } percent)
        {
            order.CommissionAmount = CommissionMath.Amount(newSubtotal, percent);
        }

        proposal.Status = "Accepted";
        proposal.DecidedAt = now;

        var saving = oldSubtotal - newSubtotal;
        var refundPaise = 0L;
        var refundStatus = "None";
        if (saving > 0 && payment is { Status: PaymentState.Paid })
        {
            refundPaise = (long)Math.Round(saving * 100m, 0, MidpointRounding.AwayFromZero);
            payment.PartialRefundedPaise += refundPaise;
            payment.UpdatedAt = now;
            refundStatus = "Pending";
        }

        var adjustment = new OrderAdjustment
        {
            OrderId = order.Id,
            ChangedBy = decidedBy,
            CreatedAt = now,
            Reason = "Replacement accepted",
            ChangesJson = JsonSerializer.Serialize(new[] { removed, added }),
            OldSubtotal = oldSubtotal,
            NewSubtotal = newSubtotal,
            RefundPaise = refundPaise,
            RefundStatus = refundStatus
        };
        return new(ReplacementOutcome.Applied, Proposal: proposal, Adjustment: adjustment);
    }

    public static ReplacementInfo ToInfo(ReplacementProposal proposal) => new(
        proposal.Id,
        proposal.Status,
        proposal.LineName,
        proposal.LineLabel,
        proposal.OldQuantity,
        proposal.OldUnitPrice,
        proposal.NewName,
        proposal.NewLabel,
        proposal.NewQuantity,
        proposal.NewUnitPrice,
        proposal.NewUnitPrice * proposal.NewQuantity - proposal.OldUnitPrice * proposal.OldQuantity,
        DateTime.SpecifyKind(proposal.ExpiresAt, DateTimeKind.Utc));
}
