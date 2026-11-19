using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>The organizations a platform administrator can switch between.</summary>
[ApiController]
[Route("api/admin/organizations")]
[Authorize]
public sealed class OrganizationsController(IAuthorizationScopeService scopes, IOrganizationDirectory directory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(cancellationToken);
        if (scope is null || !scope.IsPlatformAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Only a platform administrator can switch organizations." });
        }

        var home = scope.ActingFromOrganizationId ?? scope.OrganizationId;
        return Ok(new { success = true, data = new { homeOrganizationId = home, currentOrganizationId = scope.OrganizationId, organizations = await directory.ListAsync(cancellationToken) } });
    }
}
