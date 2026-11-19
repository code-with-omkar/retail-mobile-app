using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <param name="StoreStars">1 to 5: the shop and the order.</param>
/// <param name="DeliveryStars">1 to 5, only for an order a delivery partner brought; leave out otherwise.</param>
/// <param name="Comment">Up to 500 characters of plain text.</param>
public sealed record RatingRequest(int StoreStars, int? DeliveryStars = null, string? Comment = null);

public sealed record RatingResponse(Guid OrderId, int StoreStars, int? DeliveryStars, string? Comment, DateTime CreatedAt, DateTime UpdatedAt, bool CanEdit, bool CanRateDelivery);

/// <summary>Where an order stands for rating: whether it can be rated now, whether the delivery can be rated, and the rating if there is one.</summary>
public sealed record RatingStateResponse(bool CanRate, bool CanRateDelivery, RatingResponse? Rating);

public sealed record PartnerRating(Guid PartnerId, string Partner, int Count, decimal Average);

/// <param name="Stars">How many ratings gave 1, 2, 3, 4 and 5 stars to the shop, in that order.</param>
public sealed record RatingSummary(int Count, decimal AverageStore, decimal? AverageDelivery, IReadOnlyList<int> Stars, IReadOnlyList<PartnerRating> Partners);

/// <summary>A rating as the staff see it. The customer is never named.</summary>
public sealed record RatingRow(Guid OrderId, string OrderNumber, string Store, int StoreStars, int? DeliveryStars, string? Comment, DateTime CreatedAt);

public sealed record RatingReport(DateOnly From, DateOnly To, Guid? StoreId, RatingSummary Summary, IReadOnlyList<RatingRow> Recent);

public enum RatingStatus { Succeeded, Forbidden, NotFound, Invalid, NotYet, TooLate }

public sealed record RatingResult<T>(RatingStatus Status, T? Value = default, string? Message = null)
{
    public static RatingResult<T> Ok(T value) => new(RatingStatus.Succeeded, value);
    public static RatingResult<T> Fail(RatingStatus status, string message) => new(status, default, message);
}

public interface IRatingService
{
    /// <summary>Where the customer's own order stands for rating, and their rating if they gave one. NotFound only for an order that is not theirs.</summary>
    Task<RatingResult<RatingStateResponse>> GetMineAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Rates a delivered order, or changes the rating, within 14 days of delivery.</summary>
    Task<RatingResult<RatingResponse>> SaveMineAsync(Guid orderId, RatingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ratings for store staff (their own store) and administrators (any store, or all).</summary>
    Task<RatingResult<RatingReport>> ReportAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default);
}

public interface IRatingStore
{
    Task<OrderRating?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SaveAsync(OrderRating rating, CancellationToken cancellationToken = default);

    Task<RatingSummary> SummaryAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RatingRow>> RecentAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default);
    Task<bool> StoreExistsAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default);
}
