using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>The signed-in customer's own profile. The user id comes from the token, never from the request.</summary>
[ApiController]
[Route("api/customer/profile")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class CustomerProfileController(IAccountService accounts, IAccountDeletionService deletion) : ControllerBase
{
    /// <summary>Deletes the signed-in customer's account for good (see IAccountDeletionStore). A POST, because a body on DELETE is dropped by some proxies.</summary>
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await deletion.DeleteAsync(request, cancellationToken);
        return result.Status switch
        {
            AccountStatus.Succeeded => Ok(new { success = true }),
            AccountStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, errors = result.Errors ?? [] }),
            AccountStatus.NotFound => NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
            AccountStatus.Conflict => Conflict(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
            _ => Unauthorized(new { success = false, message = result.Message, errors = Array.Empty<string>() })
        };
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Respond(await accounts.GetProfileAsync(cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update(UpdateCustomerProfileRequest request, CancellationToken cancellationToken) => Respond(await accounts.UpdateProfileAsync(request, cancellationToken));

    private IActionResult Respond(AccountResult<CustomerProfileResponse> result) => result.Status switch
    {
        AccountStatus.Succeeded => Ok(new { success = true, data = result.Value }),
        AccountStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, errors = result.Errors ?? [] }),
        AccountStatus.NotFound => NotFound(new { success = false, message = result.Message, errors = Array.Empty<string>() }),
        _ => Unauthorized(new { success = false, message = result.Message, errors = Array.Empty<string>() })
    };
}
