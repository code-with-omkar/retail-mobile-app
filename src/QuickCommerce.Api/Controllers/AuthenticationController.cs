using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(IAuthenticationService service) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await service.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Succeeded ? Ok(new { success = true, data = result.Response }) : Unauthorized(Failure(result.Error!));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RefreshAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Succeeded ? Ok(new { success = true, data = result.Response }) : Unauthorized(Failure(result.Error!));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await service.LogoutAsync(request.RefreshToken, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Ok(new { success = true });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) => await service.GetCurrentUserAsync(cancellationToken) is { } user
        ? Ok(new { success = true, data = user })
        : Unauthorized(Failure("Authentication is no longer valid."));

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}