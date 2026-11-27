using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Adding and changing stores (administrators).</summary>
[ApiController]
[Authorize(Policy = SecurityPolicies.AdminManagement)]
public sealed class StoreAdminController(IStoreAdminService stores) : ControllerBase
{
    [HttpPost("api/admin/stores")]
    public async Task<IActionResult> Create(SaveStoreRequest request, CancellationToken cancellationToken) => Respond(await stores.CreateAsync(request, cancellationToken));

    [HttpPut("api/admin/stores/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveStoreRequest request, CancellationToken cancellationToken) => Respond(await stores.UpdateAsync(id, request, cancellationToken));

    private IActionResult Respond(StoreAdminResult result)
    {
        var body = new { success = result.Status == StoreAdminStatus.Succeeded, data = result.Store, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            StoreAdminStatus.Succeeded => Ok(body),
            StoreAdminStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            StoreAdminStatus.NotFound => NotFound(body),
            StoreAdminStatus.Conflict => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
