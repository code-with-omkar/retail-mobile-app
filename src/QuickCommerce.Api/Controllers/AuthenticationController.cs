using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(IAuthenticationService service, IAccountService accounts, ILogger<AuthenticationController> logger) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await service.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Succeeded ? Ok(new { success = true, data = result.Response }) : Unauthorized(Failure(result.Error!));
    }

    /// <summary>Creates a customer account and signs it in. Responds like login on success.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<IActionResult> Register(RegisterCustomerRequest request, CancellationToken cancellationToken) =>
        Respond(await accounts.RegisterAsync(request, ClientIp, cancellationToken));

    /// <summary>Emails a reset code when the account exists. Always answers 200 for a well-formed email so it never reveals whether the account exists.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.ForgotPassword)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken) =>
        Respond(await accounts.RequestPasswordResetAsync(request, Request.Headers.AcceptLanguage.ToString(), ClientIp, cancellationToken), _ => null);

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.ResetPassword)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken) =>
        Respond(await accounts.ResetPasswordAsync(request, ClientIp, cancellationToken), _ => null);

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.ChangePassword)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        Respond(await accounts.ChangePasswordAsync(request, ClientIp, cancellationToken), _ => null);

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

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult Respond<T>(AccountResult<T> result, Func<T, object?>? data = null)
    {
        switch (result.Status)
        {
            case AccountStatus.Succeeded:
                return Ok(new { success = true, data = data is null ? result.Value : data(result.Value!) });
            case AccountStatus.InvalidRequest:
                return BadRequest(new { success = false, message = result.Message, errors = result.Errors ?? [] });
            case AccountStatus.Conflict:
                return Conflict(Failure(result.Message!));
            case AccountStatus.NotFound:
                return NotFound(Failure(result.Message!));
            case AccountStatus.Unauthorized:
                return Unauthorized(Failure(result.Message!));
            default:
                // Configuration problems are for operators, not callers.
                logger.LogError("Account operation failed because of a configuration problem: {Message}", result.Message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, Failure("This feature is not available right now. Please try again later."));
        }
    }

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}