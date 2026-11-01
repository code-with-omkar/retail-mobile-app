using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Signing in with a text-message code or with Google, and verifying a phone number for an account that already exists.</summary>
[ApiController]
public sealed class SignInController(ISignInService signIn) : ControllerBase
{
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("api/auth/otp/request")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.OtpRequest)]
    public async Task<IActionResult> RequestOtp(OtpRequest request, CancellationToken cancellationToken) => Respond(await signIn.RequestOtpAsync(request, Ip, cancellationToken));

    [HttpPost("api/auth/otp/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.OtpVerify)]
    public async Task<IActionResult> VerifyOtp(OtpVerifyRequest request, CancellationToken cancellationToken) => Respond(await signIn.VerifyOtpAsync(request, Ip, cancellationToken));

    [HttpPost("api/auth/google")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Google)]
    public async Task<IActionResult> Google(GoogleSignInRequest request, CancellationToken cancellationToken) => Respond(await signIn.GoogleSignInAsync(request, Ip, cancellationToken));

    [HttpPost("api/account/phone/otp/request")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.OtpRequest)]
    public async Task<IActionResult> RequestPhoneVerification(OtpRequest request, CancellationToken cancellationToken) => Respond(await signIn.RequestPhoneVerificationAsync(request, Ip, cancellationToken));

    [HttpPost("api/account/phone/otp/verify")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.OtpVerify)]
    public async Task<IActionResult> VerifyPhone(OtpVerifyRequest request, CancellationToken cancellationToken) => Respond(await signIn.VerifyPhoneAsync(request, Ip, cancellationToken));

    private IActionResult Respond<T>(SignInResult<T> result)
    {
        if (result.RetryAfterSeconds is { } seconds)
        {
            Response.Headers.RetryAfter = seconds.ToString();
        }

        return result.Status switch
        {
            SignInStatus.Succeeded => Ok(new { success = true, data = result.Value }),
            SignInStatus.Invalid => BadRequest(Failure(result.Message!)),
            SignInStatus.Unauthorized => Unauthorized(Failure(result.Message!)),
            SignInStatus.TooManyRequests => StatusCode(StatusCodes.Status429TooManyRequests, Failure(result.Message!)),
            SignInStatus.Conflict => Conflict(Failure(result.Message!)),
            SignInStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, Failure(result.Message!)),
            SignInStatus.NotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable, Failure(result.Message!)),
            _ => StatusCode(StatusCodes.Status502BadGateway, Failure(result.Message!))
        };
    }

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}
