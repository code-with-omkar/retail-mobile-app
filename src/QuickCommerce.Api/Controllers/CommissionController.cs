using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>The commission the platform takes from each store's product sales. Administrators only.</summary>
[ApiController]
[Route("api/admin/commission")]
[Authorize(Policy = SecurityPolicies.AdminManagement)]
public sealed class CommissionController(ICommissionService commission) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Respond(await commission.GetAsync(cancellationToken));

    /// <summary>The percent for every store without its own (0 to 100, two decimals).</summary>
    [HttpPut("default")]
    public async Task<IActionResult> SetDefault(SetCommissionRequest request, CancellationToken cancellationToken) =>
        request.Percent is null ? BadRequest(new { success = false, message = "Give the percent.", errors = Array.Empty<string>() }) : Respond(await commission.SetDefaultAsync(request.Percent.Value, cancellationToken));

    /// <summary>One store's own percent, or null to use the default.</summary>
    [HttpPut("stores/{storeId:guid}")]
    public async Task<IActionResult> SetStore(Guid storeId, SetCommissionRequest request, CancellationToken cancellationToken) => Respond(await commission.SetStoreAsync(storeId, request.Percent, cancellationToken));

    private IActionResult Respond(CommissionResult result)
    {
        var body = new { success = result.Status == CommissionStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            CommissionStatus.Succeeded => Ok(body),
            CommissionStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            CommissionStatus.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
