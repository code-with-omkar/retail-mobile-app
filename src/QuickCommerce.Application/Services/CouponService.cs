using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Coupons: made and changed by administrators, tried by customers in the cart. Checkout applies the same rules again.</summary>
public sealed class CouponService(
    ICouponStore coupons,
    IAuthorizationScopeService scopeService,
    ICurrentUserContextResolver userContext,
    ICartService carts,
    TimeProvider? timeProvider = null) : ICouponService
{
    private const string NotAllowed = "Only an administrator can manage coupons.";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<CouponResult<IReadOnlyList<CouponResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CouponServiceStatus.Forbidden, Message: NotAllowed);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var list = await coupons.ListAsync(scope.OrganizationId, cancellationToken);
        return new(CouponServiceStatus.Succeeded, list.Select(item => Map(item.Coupon, item.DiscountGiven, now)).OrderByDescending(item => item.StartsAt).ToArray());
    }

    public async Task<CouponResult<CouponResponse>> CreateAsync(SaveCouponRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CouponServiceStatus.Forbidden, Message: NotAllowed);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var code = CouponRules.Normalize(request.Code);
        var startsAt = request.StartsAt ?? now;
        if (Problem(request, code, startsAt) is { } problem)
        {
            return new(CouponServiceStatus.Invalid, Message: problem);
        }

        var coupon = new Coupon { OrganizationId = scope.OrganizationId, Code = code, Kind = request.Kind, CreatedAt = now, CreatedBy = scope.UserId.ToString() };
        Fill(coupon, request, code, startsAt);
        return await coupons.AddAsync(coupon, cancellationToken)
            ? new(CouponServiceStatus.Succeeded, Map(coupon, 0, now))
            : new(CouponServiceStatus.Conflict, Message: "That code already exists.");
    }

    public async Task<CouponResult<CouponResponse>> UpdateAsync(Guid id, SaveCouponRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(CouponServiceStatus.Forbidden, Message: NotAllowed);
        }

        var coupon = await coupons.GetAsync(scope.OrganizationId, id, cancellationToken);
        if (coupon is null)
        {
            return new(CouponServiceStatus.NotFound, Message: "Coupon not found.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var code = CouponRules.Normalize(request.Code);
        var startsAt = request.StartsAt ?? coupon.StartsAt;
        if (Problem(request, code, startsAt) is { } problem)
        {
            return new(CouponServiceStatus.Invalid, Message: problem);
        }

        // Orders that already used it keep what it was then (the order's own copy), so every field can change.
        Fill(coupon, request, code, startsAt);
        return await coupons.SaveAsync(coupon, cancellationToken)
            ? new(CouponServiceStatus.Succeeded, Map(coupon, (await coupons.ListAsync(scope.OrganizationId, cancellationToken)).FirstOrDefault(item => item.Coupon.Id == id)?.DiscountGiven ?? 0, now))
            : new(CouponServiceStatus.Conflict, Message: "That code already exists.");
    }

    public async Task<CouponResult<CouponPreviewResponse>> PreviewAsync(Guid storeId, string? code, CancellationToken cancellationToken = default)
    {
        var context = await userContext.ResolveAsync(cancellationToken);
        if (context is null || context.Role != Role.Customer)
        {
            return new(CouponServiceStatus.Forbidden, Message: "Only a customer can use a coupon.");
        }

        var typed = CouponRules.Normalize(code);
        if (typed.Length == 0)
        {
            return new(CouponServiceStatus.Invalid, Message: "Type a code.");
        }

        var cart = await carts.GetCartAsync(storeId, cancellationToken);
        if (cart is null || cart.Items.Count == 0 || cart.Subtotal <= 0)
        {
            return new(CouponServiceStatus.Succeeded, new CouponPreviewResponse(false, typed, 0, "Your cart is empty.", cart?.Total ?? 0, "EmptyCart"));
        }

        var coupon = CouponRules.IsValidCode(typed) ? await coupons.GetByCodeAsync(context.OrganizationId, typed, cancellationToken) : null;
        var mine = coupon is null ? 0 : await coupons.UsesByUserAsync(coupon.Id, context.UserId, cancellationToken);
        var hasOrders = coupon is { FirstOrderOnly: true } && await coupons.HasOrdersAsync(context.UserId, cancellationToken);
        var outcome = CouponRules.Check(coupon, new CouponContext(cart.Subtotal, cart.DeliveryFee, clock.GetUtcNow().UtcDateTime, coupon?.UsedCount ?? 0, mine, hasOrders));
        return new(CouponServiceStatus.Succeeded, new CouponPreviewResponse(outcome.Check == CouponCheck.Valid, typed, outcome.Discount, outcome.Message, cart.Total - outcome.Discount, outcome.Check == CouponCheck.Valid ? null : outcome.Check.ToString()));
    }

    private static string? Problem(SaveCouponRequest request, string code, DateTime startsAt)
    {
        if (request.Description is { Length: > 200 } || request.Description?.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n')) == true)
        {
            return "The description can be up to 200 characters and cannot contain < or >.";
        }

        return CouponRules.Validate(code, request.Kind, request.Value, request.MaxDiscount, request.MinSubtotal, startsAt, request.EndsAt, request.TotalLimit, request.PerCustomerLimit);
    }

    private static void Fill(Coupon coupon, SaveCouponRequest request, string code, DateTime startsAt)
    {
        coupon.Code = code;
        coupon.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        coupon.Kind = request.Kind;
        coupon.Value = request.Value;
        coupon.MaxDiscount = request.MaxDiscount;
        coupon.MinSubtotal = request.MinSubtotal;
        coupon.StartsAt = startsAt;
        coupon.EndsAt = request.EndsAt;
        coupon.TotalLimit = request.TotalLimit;
        coupon.PerCustomerLimit = request.PerCustomerLimit;
        coupon.FirstOrderOnly = request.FirstOrderOnly;
        coupon.IsActive = request.IsActive;
    }

    public static string StatusOf(Coupon coupon, DateTime now) =>
        !coupon.IsActive ? "Off"
        : coupon.TotalLimit is { } limit && coupon.UsedCount >= limit ? "Used up"
        : coupon.EndsAt is { } ends && ends <= now ? "Expired"
        : coupon.StartsAt > now ? "Scheduled"
        : "Active";

    private static CouponResponse Map(Coupon coupon, decimal discountGiven, DateTime now) => new(
        coupon.Id, coupon.Code, coupon.Description, coupon.Kind, coupon.Value, coupon.MaxDiscount, coupon.MinSubtotal,
        DateTime.SpecifyKind(coupon.StartsAt, DateTimeKind.Utc), coupon.EndsAt is { } ends ? DateTime.SpecifyKind(ends, DateTimeKind.Utc) : null,
        coupon.TotalLimit, coupon.PerCustomerLimit, coupon.FirstOrderOnly, coupon.IsActive, coupon.UsedCount, discountGiven, StatusOf(coupon, now));
}
