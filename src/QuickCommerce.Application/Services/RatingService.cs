using System.Text.RegularExpressions;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

public sealed partial class RatingService(
    IRatingStore store,
    ICommerceStore data,
    ICurrentUserContextResolver contextResolver,
    TimeProvider clock) : IRatingService
{
    public const int MaxCommentLength = 500;
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    // Angle brackets and control characters are refused, so a comment can never carry markup into a page that shows it.
    [GeneratedRegex("[<>\\u0000-\\u0008\\u000B\\u000C\\u000E-\\u001F]")]
    private static partial Regex Unwanted();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>When the customer got the order: the partner's mark, or the moment the store completed it.</summary>
    public static DateTime? DeliveredAt(Order order) =>
        order.DeliveredAt ?? order.StatusHistory.Where(item => item.Status is OrderStatus.Delivered or OrderStatus.Completed).Select(item => (DateTime?)item.ChangedAt).Max();

    private static RatingResponse Map(OrderRating rating, DateTime now, DateTime deliveredAt, bool hadPartner) =>
        new(rating.OrderId, rating.StoreStars, rating.DeliveryStars, rating.Comment, rating.CreatedAt, rating.UpdatedAt, now - deliveredAt <= Window, hadPartner);

    private async Task<(Order? Order, Guid? CustomerId)> MyOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is not { Role: Role.Customer })
        {
            return (null, null);
        }

        var customer = await data.GetCustomerByUserIdAsync(context.UserId, cancellationToken);
        var order = customer is { IsActive: true } ? await data.GetCustomerOrderAsync(orderId, context.UserId, context.OrganizationId, cancellationToken) : null;
        return (order, customer?.Id);
    }

    public async Task<RatingResult<RatingStateResponse>> GetMineAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var (order, _) = await MyOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return RatingResult<RatingStateResponse>.Fail(RatingStatus.NotFound, "Order not found.");
        }

        var now = Now;
        var hadPartner = order.DeliveryPartnerId.HasValue;
        var deliveredAt = order.Status is OrderStatus.Delivered or OrderStatus.Completed ? DeliveredAt(order) : null;
        var rating = await store.GetAsync(orderId, cancellationToken);
        var canRate = deliveredAt is not null && now - deliveredAt.Value <= Window;
        return RatingResult<RatingStateResponse>.Ok(new RatingStateResponse(canRate, canRate && hadPartner, rating is null || deliveredAt is null ? null : Map(rating, now, deliveredAt.Value, hadPartner)));
    }

    public async Task<RatingResult<RatingResponse>> SaveMineAsync(Guid orderId, RatingRequest request, CancellationToken cancellationToken = default)
    {
        var (order, customerId) = await MyOrderAsync(orderId, cancellationToken);
        if (order is null || customerId is null)
        {
            return RatingResult<RatingResponse>.Fail(RatingStatus.NotFound, "Order not found.");
        }

        if (order.Status is not (OrderStatus.Delivered or OrderStatus.Completed) || DeliveredAt(order) is not { } deliveredAt)
        {
            return RatingResult<RatingResponse>.Fail(RatingStatus.NotYet, "You can rate an order once it is delivered.");
        }

        var now = Now;
        if (now - deliveredAt > Window)
        {
            return RatingResult<RatingResponse>.Fail(RatingStatus.TooLate, "Ratings can be given or changed for 14 days after delivery.");
        }

        var hadPartner = order.DeliveryPartnerId.HasValue;
        var comment = request.Comment?.Trim();
        if (request.StoreStars is < 1 or > 5 || request.DeliveryStars is < 1 or > 5)
        {
            return RatingResult<RatingResponse>.Fail(RatingStatus.Invalid, "Choose 1 to 5 stars.");
        }

        if (request.DeliveryStars is not null && !hadPartner)
        {
            return RatingResult<RatingResponse>.Fail(RatingStatus.Invalid, "This order was not brought by a delivery partner.");
        }

        if (comment is { Length: > MaxCommentLength } || (comment is not null && Unwanted().IsMatch(comment)))
        {
            return RatingResult<RatingResponse>.Fail(RatingStatus.Invalid, $"The comment can be up to {MaxCommentLength} characters of plain text.");
        }

        var existing = await store.GetAsync(orderId, cancellationToken);
        var rating = existing ?? new OrderRating { OrderId = orderId, StoreId = order.StoreId, CreatedAt = now };
        rating.CustomerId = customerId;
        rating.DeliveryPartnerId = order.DeliveryPartnerId;
        rating.StoreStars = request.StoreStars;
        rating.DeliveryStars = request.DeliveryStars;
        rating.Comment = string.IsNullOrEmpty(comment) ? null : comment;
        rating.UpdatedAt = now;
        await store.SaveAsync(rating, cancellationToken);
        return RatingResult<RatingResponse>.Ok(Map(rating, now, deliveredAt, hadPartner));
    }

    public async Task<RatingResult<RatingReport>> ReportAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return RatingResult<RatingReport>.Fail(RatingStatus.Forbidden, "Ratings are for store staff and administrators.");
        }

        IReadOnlyCollection<Guid>? stores = null;
        if (context.Role == Role.StoreStaff)
        {
            if (context.StoreId is not { } own)
            {
                return RatingResult<RatingReport>.Fail(RatingStatus.Forbidden, "Store staff must be assigned to a store.");
            }

            stores = [own];
        }
        else if (storeId is { } chosen)
        {
            if (!await store.StoreExistsAsync(context.OrganizationId, chosen, cancellationToken))
            {
                return RatingResult<RatingReport>.Fail(RatingStatus.NotFound, "Store not found.");
            }

            stores = [chosen];
        }

        if (ReportMath.Check(from, to) is { } problem)
        {
            return RatingResult<RatingReport>.Fail(RatingStatus.Invalid, problem);
        }

        var fromUtc = ReportMath.StartUtc(from);
        var toUtc = ReportMath.EndUtc(to);
        var summary = await store.SummaryAsync(context.OrganizationId, stores, fromUtc, toUtc, cancellationToken);
        var recent = await store.RecentAsync(context.OrganizationId, stores, fromUtc, toUtc, 30, cancellationToken);
        return RatingResult<RatingReport>.Ok(new RatingReport(from, to, stores?.Single(), summary, recent));
    }
}
