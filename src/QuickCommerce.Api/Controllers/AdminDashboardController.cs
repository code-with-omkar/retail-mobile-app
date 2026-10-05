using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Api.Security;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = SecurityPolicies.AdminDataRead)]
public sealed class AdminDashboardController(
    IAuthorizationScopeService scopeService,
    ICommerceStore data) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope is null || !scope.IsApplicationAdmin)
        {
            return Forbid();
        }

        var dashboard = await data.GetAdminDashboardAsync(scope.OrganizationId, scope.StoreIds, scope.IsApplicationAdmin, cancellationToken);
        return Ok(new
        {
            success = true,
            data = dashboard
        });
    }
}