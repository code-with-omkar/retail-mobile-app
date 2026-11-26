using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>A coupon as the portal shows it.</summary>
/// <param name="Status">Active, Scheduled (not started), Expired, Used up or Off.</param>
/// <param name="UsedCount">Orders using it now (uses given back are not counted).</param>
/// <param name="DiscountGiven">The total the business has given with it on those orders.</param>
public sealed record CouponResponse(
    Guid Id,
    string Code,
    string? Description,
    string Kind,
    decimal Value,
    decimal? MaxDiscount,
    decimal MinSubtotal,
    DateTime StartsAt,
    DateTime? EndsAt,
    int? TotalLimit,
    int? PerCustomerLimit,
    bool FirstOrderOnly,
    bool IsActive,
    int UsedCount,
    decimal DiscountGiven,
    string Status,
    string? Design = null,
    string? ImageUrl = null,
    string? Ribbon = null,
    bool ShowInApp = false,
    bool ShowAsPopup = false,
    string? Animation = null);

/// <summary>What an administrator sends to make or change a coupon. Times are UTC.</summary>
public sealed record SaveCouponRequest(
    string Code,
    string Kind,
    decimal Value,
    decimal? MaxDiscount = null,
    decimal MinSubtotal = 0,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    int? TotalLimit = null,
    int? PerCustomerLimit = null,
    bool FirstOrderOnly = false,
    bool IsActive = true,
    string? Description = null,
    string? Design = null,
    string? ImageUrl = null,
    string? Ribbon = null,
    bool ShowInApp = false,
    bool ShowAsPopup = false,
    string? Animation = null);

/// <summary>A coupon the app lists to customers. No limits or usage figures: those are the business's.</summary>
public sealed record FeaturedCoupon(
    Guid Id,
    string Code,
    string? Description,
    string Kind,
    decimal Value,
    decimal? MaxDiscount,
    decimal MinSubtotal,
    DateTime? EndsAt,
    bool FirstOrderOnly,
    string? Design,
    string? ImageUrl,
    string? Ribbon,
    bool ShowAsPopup,
    string? Animation);

public sealed record CouponPreviewRequest(string? Code);

/// <summary>What the cart shows for a typed code. When not valid, <see cref="Discount"/> is 0 and <see cref="Message"/> says why.</summary>
/// <param name="Reason">When not valid, the kind of refusal (NotFound, Inactive, Expired, BelowMinimum, CustomerLimitReached ...); null when valid. For counting, not for showing.</param>
public sealed record CouponPreviewResponse(bool Valid, string Code, decimal Discount, string Message, decimal Total, string? Reason = null, string? Animation = null);

public sealed record CouponSummary(Coupon Coupon, decimal DiscountGiven);

public interface ICouponStore
{
    Task<Coupon?> GetByCodeAsync(Guid organizationId, string code, CancellationToken cancellationToken = default);
    Task<Coupon?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CouponSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a coupon; false when the code is already taken in the organisation.</summary>
    Task<bool> AddAsync(Coupon coupon, CancellationToken cancellationToken = default);

    /// <summary>Saves changes to a coupon already loaded; false when the new code collides with another.</summary>
    Task<bool> SaveAsync(Coupon coupon, CancellationToken cancellationToken = default);

    /// <summary>The organisation an active store belongs to; null when there is no such store.</summary>
    Task<Guid?> GetStoreOrganizationAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>The coupons marked to be shown in the app that are running and not used up.</summary>
    Task<IReadOnlyList<Coupon>> ListShownAsync(Guid organizationId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>How many of the coupon's uses (not given back) are this customer's.</summary>
    Task<int> UsesByUserAsync(Guid couponId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the customer has an order that is still alive or done (cancelled and rejected orders do not count).</summary>
    Task<bool> HasOrdersAsync(Guid userId, CancellationToken cancellationToken = default);
}

public enum CouponServiceStatus { Succeeded, Forbidden, NotFound, Invalid, Conflict }

public sealed record CouponResult<T>(CouponServiceStatus Status, T? Value = default, string? Message = null);

public interface ICouponService
{
    /// <summary>Administrators only.</summary>
    Task<CouponResult<IReadOnlyList<CouponResponse>>> ListAsync(CancellationToken cancellationToken = default);

    Task<CouponResult<CouponResponse>> CreateAsync(SaveCouponRequest request, CancellationToken cancellationToken = default);

    Task<CouponResult<CouponResponse>> UpdateAsync(Guid id, SaveCouponRequest request, CancellationToken cancellationToken = default);

    /// <summary>The coupons to list at a store. Anyone may ask; a signed-in customer is not shown one they can no longer use.</summary>
    Task<CouponResult<IReadOnlyList<FeaturedCoupon>>> FeaturedAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>The customer's cart with a typed code: what it would take off, or why it cannot be used.</summary>
    Task<CouponResult<CouponPreviewResponse>> PreviewAsync(Guid storeId, string? code, CancellationToken cancellationToken = default);
}
