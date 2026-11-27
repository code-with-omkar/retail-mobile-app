using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public static class OrderIssueRules
{
    /// <summary>How long after delivery a customer can report a problem.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(48);

    public static readonly string[] Kinds = ["Missing", "Damaged", "WrongItem", "Quality"];

    public const int NoteLength = 500;

    public static DateTime? ReportUntil(OrderStatus status, DateTime? deliveredAt) =>
        status is OrderStatus.Delivered or OrderStatus.Completed && deliveredAt is { } at ? at + Window : null;

    public static bool NoteOk(string? note) => note is null || (note.Trim().Length <= NoteLength && !note.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n')));

    /// <summary>
    /// The lines claimed with what was paid for them (a coupon's share is taken off). Null with a reason when the request does not fit the order.
    /// </summary>
    public static (List<OrderIssueItem>? Items, string? Problem) Claim(Order order, IReadOnlyList<IssueLineRequest>? wanted)
    {
        if (wanted is not { Count: > 0 })
        {
            return (null, "Choose at least one item.");
        }

        if (wanted.Select(line => (line.ProductId, line.VariantId)).Distinct().Count() != wanted.Count)
        {
            return (null, "An item is listed twice.");
        }

        // A coupon lowered what the customer paid for each item by the same share.
        var paidShare = order.SubtotalAmount > 0 ? Math.Max(0m, order.SubtotalAmount - order.CouponDiscount) / order.SubtotalAmount : 1m;
        var items = new List<OrderIssueItem>();
        foreach (var line in wanted)
        {
            var ordered = order.Items.Where(item => item.ProductId == line.ProductId && item.VariantId == line.VariantId).ToArray();
            var quantity = ordered.Sum(item => item.Quantity);
            if (line.Quantity < 1 || line.Quantity > quantity)
            {
                return (null, "Choose a quantity you ordered.");
            }

            items.Add(new OrderIssueItem
            {
                ProductId = line.ProductId,
                VariantId = line.VariantId,
                Name = ordered[0].ProductNameSnapshot,
                Label = ordered[0].VariantLabelSnapshot,
                Quantity = line.Quantity,
                Amount = decimal.Round(ordered[0].UnitPrice * line.Quantity * paidShare, 2, MidpointRounding.ToZero)
            });
        }

        return (items, null);
    }
}

/// <summary>Customers report problems after delivery; a store manager (or an administrator) decides; cash refunds are marked by the store.</summary>
public sealed class OrderIssueService(IOrderIssueStore store, ICurrentUserContextResolver users, TimeProvider? timeProvider = null) : IOrderIssueService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IssueResult<CustomerIssueResponse>> MineAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await users.ResolveAsync(cancellationToken);
        if (context is not { Role: Role.Customer })
        {
            return new(IssueStatus.Forbidden, Message: "Only a customer can see this.");
        }

        if (await store.GetCustomerOrderAsync(orderId, context.UserId, cancellationToken) is not { } mine)
        {
            return new(IssueStatus.NotFound, Message: "Order not found.");
        }

        var issue = await store.GetByOrderAsync(orderId, cancellationToken);
        var until = OrderIssueRules.ReportUntil(mine.Order.Status, mine.DeliveredAt);
        return new(IssueStatus.Succeeded, new CustomerIssueResponse(issue is null ? null : Map(issue, false), issue is null && until > Now ? Utc(until!.Value) : null));
    }

    public async Task<IssueResult<IssueResponse>> ReportAsync(Guid orderId, ReportIssueRequest request, CancellationToken cancellationToken = default)
    {
        var context = await users.ResolveAsync(cancellationToken);
        if (context is not { Role: Role.Customer })
        {
            return new(IssueStatus.Forbidden, Message: "Only a customer can report a problem.");
        }

        if (request.Kind is null || !OrderIssueRules.Kinds.Contains(request.Kind))
        {
            return new(IssueStatus.Invalid, Message: "Choose what went wrong.");
        }

        if (!OrderIssueRules.NoteOk(request.Note))
        {
            return new(IssueStatus.Invalid, Message: "The note can be up to 500 characters and cannot contain < or >.");
        }

        if (await store.GetCustomerOrderAsync(orderId, context.UserId, cancellationToken) is not { } mine)
        {
            return new(IssueStatus.NotFound, Message: "Order not found.");
        }

        if (!(OrderIssueRules.ReportUntil(mine.Order.Status, mine.DeliveredAt) > Now))
        {
            return new(IssueStatus.Conflict, Message: "A problem can be reported up to 48 hours after delivery.");
        }

        var (items, problem) = OrderIssueRules.Claim(mine.Order, request.Items);
        if (items is null)
        {
            return new(IssueStatus.Invalid, Message: problem);
        }

        var issue = new OrderIssue
        {
            OrganizationId = mine.OrganizationId,
            StoreId = mine.Order.StoreId,
            OrderId = orderId,
            CustomerUserId = context.UserId,
            Kind = request.Kind,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ClaimedAmount = items.Sum(item => item.Amount),
            CreatedAt = Now,
            Items = items
        };
        if (!await store.AddAsync(issue, cancellationToken))
        {
            return new(IssueStatus.Conflict, Message: "You have already reported a problem with this order.");
        }

        return new(IssueStatus.Succeeded, Map((await store.GetByOrderAsync(orderId, cancellationToken))!, false));
    }

    public async Task<IssueResult<IReadOnlyList<IssueResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var context = await users.ResolveAsync(cancellationToken);
        if (Stores(context) is not { Allowed: true } scope)
        {
            return new(IssueStatus.Forbidden, Message: "Only store staff or an administrator can see reports.");
        }

        var rows = await store.ListAsync(context!.OrganizationId, scope.StoreIds, cancellationToken);
        return new(IssueStatus.Succeeded, rows.Select(row => Map(row, scope.Decides)).ToArray());
    }

    public async Task<IssueResult<IssueResponse>> DecideAsync(Guid id, DecideIssueRequest request, CancellationToken cancellationToken = default)
    {
        var found = await ForStaffAsync(id, cancellationToken);
        if (found.Row is null)
        {
            return new(found.Status, Message: found.Message);
        }

        if (!found.Decides)
        {
            return new(IssueStatus.Forbidden, Message: "Only a store manager or an administrator can decide.");
        }

        var amount = request.Approve ? request.Amount ?? found.Row.Issue.ClaimedAmount : 0m;
        if (request.Approve && (amount <= 0 || amount > found.Row.Issue.ClaimedAmount || decimal.Round(amount, 2) != amount))
        {
            return new(IssueStatus.Invalid, Message: $"Give back more than 0 and at most {found.Row.Issue.ClaimedAmount:0.00}.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (!OrderIssueRules.NoteOk(note) || (!request.Approve && note is null))
        {
            return new(IssueStatus.Invalid, Message: request.Approve ? "The note can be up to 500 characters and cannot contain < or >." : "Tell the customer why it is refused.");
        }

        return await store.DecideAsync(id, request.Approve, amount, note, found.UserId, Now, cancellationToken)
            ? new(IssueStatus.Succeeded, Map((await store.GetByOrderAsync(found.Row.Issue.OrderId, cancellationToken))!, true))
            : new(IssueStatus.Conflict, Message: "This report was already decided.");
    }

    public async Task<IssueResult<IssueResponse>> MarkCashPaidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await ForStaffAsync(id, cancellationToken);
        if (found.Row is null)
        {
            return new(found.Status, Message: found.Message);
        }

        return await store.MarkCashPaidAsync(id, found.UserId, Now, cancellationToken)
            ? new(IssueStatus.Succeeded, Map((await store.GetByOrderAsync(found.Row.Issue.OrderId, cancellationToken))!, found.Decides))
            : new(IssueStatus.Conflict, Message: "There is no cash refund waiting to be paid.");
    }

    /// <summary>The stores a member of staff handles (null: all of them), and whether they may decide.</summary>
    private static (bool Allowed, IReadOnlyCollection<Guid>? StoreIds, bool Decides) Stores(UserContext? context) => context?.Role switch
    {
        Role.Admin or Role.ApplicationAdmin => (true, null, true),
        Role.StoreStaff => (true, context.OrderStoreIds.ToArray(), context.StaffCategory == StaffCategory.StoreManager),
        _ => (false, null, false)
    };

    private async Task<(IssueRow? Row, bool Decides, Guid UserId, IssueStatus Status, string? Message)> ForStaffAsync(Guid id, CancellationToken cancellationToken)
    {
        var context = await users.ResolveAsync(cancellationToken);
        var scope = Stores(context);
        if (!scope.Allowed)
        {
            return (null, false, Guid.Empty, IssueStatus.Forbidden, "Only store staff or an administrator can do this.");
        }

        var row = await store.GetAsync(context!.OrganizationId, id, cancellationToken);
        return row is null || (scope.StoreIds is not null && !scope.StoreIds.Contains(row.Issue.StoreId))
            ? (null, false, Guid.Empty, IssueStatus.NotFound, "Report not found.")
            : (row, scope.Decides, context.UserId, IssueStatus.Succeeded, null);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static IssueResponse Map(IssueRow row, bool decides) => new(
        row.Issue.Id, row.Issue.OrderId, row.OrderNumber, row.Issue.StoreId, row.Store, row.Customer, row.Issue.Kind, row.Issue.Note, row.Issue.Status,
        row.Issue.ClaimedAmount, row.Issue.ApprovedAmount, row.Issue.DecisionNote, row.Issue.RefundMethod,
        row.Issue.CashPaidAt is { } paid ? Utc(paid) : null, Utc(row.Issue.CreatedAt), row.Issue.DecidedAt is { } decided ? Utc(decided) : null,
        row.Issue.Items.OrderBy(item => item.Name).Select(item => new IssueLine(item.ProductId, item.VariantId, item.Name, item.Label, item.Quantity, item.Amount)).ToArray(),
        decides && row.Issue.Status == "Open");
}
