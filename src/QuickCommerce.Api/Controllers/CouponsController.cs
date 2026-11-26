using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
public sealed class CouponsController(ICouponService coupons) : ControllerBase
{
    /// <summary>Administrators: every coupon with how often it was used and what it cost.</summary>
    [HttpGet("api/admin/coupons")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Respond(await coupons.ListAsync(cancellationToken));

    [HttpPost("api/admin/coupons")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Create(SaveCouponRequest request, CancellationToken cancellationToken) => Respond(await coupons.CreateAsync(request, cancellationToken));

    [HttpPut("api/admin/coupons/{id:guid}")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Update(Guid id, SaveCouponRequest request, CancellationToken cancellationToken) => Respond(await coupons.UpdateAsync(id, request, cancellationToken));

    /// <summary>The coupons the app lists at a store (those marked to be shown). Open to anyone.</summary>
    [HttpGet("api/offers/coupons")]
    [AllowAnonymous]
    public async Task<IActionResult> Featured([FromQuery] Guid storeId, CancellationToken cancellationToken) => Respond(await coupons.FeaturedAsync(storeId, cancellationToken));

    /// <summary>The customer's cart with a typed code: the discount and new total, or why the code cannot be used (still 200, with <c>valid: false</c>).</summary>
    [HttpPost("api/carts/{storeId:guid}/coupon")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> Preview(Guid storeId, CouponPreviewRequest request, CancellationToken cancellationToken) => Respond(await coupons.PreviewAsync(storeId, request.Code, cancellationToken));

    private IActionResult Respond<T>(CouponResult<T> result)
    {
        var body = new { success = result.Status == CouponServiceStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            CouponServiceStatus.Succeeded => Ok(body),
            CouponServiceStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            CouponServiceStatus.NotFound => NotFound(body),
            CouponServiceStatus.Conflict => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
