using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>What the signed-in customer bought before: the stores for Home, and the items inside a store. The customer comes from the token.</summary>
[ApiController]
[Route("api/customer")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class PurchaseHistoryController(IPurchaseHistoryService history) : ControllerBase
{
    [HttpGet("recent-stores")]
    public async Task<IActionResult> RecentStores(CancellationToken cancellationToken) => Respond(await history.RecentStoresAsync(cancellationToken));

    [HttpGet("stores/{storeId:guid}/bought-before")]
    public async Task<IActionResult> BoughtBefore(Guid storeId, CancellationToken cancellationToken) => Respond(await history.BoughtBeforeAsync(storeId, cancellationToken));

    private IActionResult Respond<T>(PurchaseHistoryResult<T> result)
    {
        var body = new { success = result.Status == PurchaseHistoryStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            PurchaseHistoryStatus.Succeeded => Ok(body),
            PurchaseHistoryStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            _ => NotFound(body)
        };
    }
}
