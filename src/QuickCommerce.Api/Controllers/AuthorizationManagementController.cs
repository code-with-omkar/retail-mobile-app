using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Policy = SecurityPolicies.AdminManagement)]
public sealed class AuthorizationManagementController(IAuthorizationManagementService service) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetUsersAsync(cancellationToken) });

    /// <summary>The portal's user list, a page at a time.</summary>
    [HttpGet("users/paged")]
    public async Task<IActionResult> UsersPage([FromQuery] string? search, [FromQuery] string[]? role, [FromQuery] bool? active, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(new { success = true, data = await service.GetUsersPageAsync(new ManagedUserQuery(search, role, active, sort, page, pageSize), cancellationToken) });

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken) => await service.GetUserAsync(id, cancellationToken) is { } user
        ? Ok(new { success = true, data = user })
        : NotFound(Failure("User not found"));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(CreateManagedUserRequest request, CancellationToken cancellationToken) => await service.CreateUserAsync(request, cancellationToken) is { } user
        ? Created($"/api/users/{user.Id}", new { success = true, data = user })
        : Conflict(Failure("User could not be created for the requested organization or email already exists"));

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, UpdateManagedUserRequest request, CancellationToken cancellationToken) => await service.UpdateUserAsync(id, request, cancellationToken) is { } user
        ? Ok(new { success = true, data = user })
        : NotFound(Failure("User not found"));

    [HttpPatch("users/{id:guid}/status")]
    public async Task<IActionResult> SetUserStatus(Guid id, [FromBody] SetUserStatusRequest request, CancellationToken cancellationToken) => await service.SetUserStatusAsync(id, request.IsActive, cancellationToken) is { } user
        ? Ok(new { success = true, data = user })
        : NotFound(Failure("User not found"));

    /// <summary>A new temporary password for a member of staff, shown once. They must change it when they next sign in.</summary>
    [HttpPost("users/{id:guid}/temporary-password")]
    public async Task<IActionResult> TemporaryPassword(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await service.SetTemporaryPasswordAsync(id, cancellationToken) is { } made
            ? Ok(new { success = true, data = made })
            : Conflict(Failure("A temporary password can be set only for another active member of staff with an email address."));
    }

    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetRolesAsync(cancellationToken) });

    [HttpGet("permissions")]
    public async Task<IActionResult> Permissions(CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetPermissionsAsync(cancellationToken) });

    [HttpPut("roles/{id:guid}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken) => await service.UpdateRolePermissionsAsync(id, request, cancellationToken) is { } role
        ? Ok(new { success = true, data = role })
        : NotFound(Failure("Role or permission not found"));

    [HttpGet("organizations")]
    public async Task<IActionResult> Organizations(CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetOrganizationsAsync(cancellationToken) });

    [HttpGet("admin/stores")]
    public async Task<IActionResult> Stores([FromQuery] Guid? organizationId, CancellationToken cancellationToken) => Ok(new { success = true, data = await service.GetStoresAsync(organizationId, cancellationToken) });

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}

public sealed record SetUserStatusRequest(bool IsActive);