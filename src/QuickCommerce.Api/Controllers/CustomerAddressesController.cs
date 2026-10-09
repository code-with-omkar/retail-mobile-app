using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>
/// The signed-in customer's saved delivery addresses. The customer comes from the token, never from the request, and an address that
/// belongs to someone else is reported as not found, so its existence is not revealed.
/// </summary>
[ApiController]
[Route("api/customer/addresses")]
[Authorize(Policy = SecurityPolicies.Orders)]
public sealed class CustomerAddressesController(IAddressService addresses) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Respond(await addresses.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CustomerAddressRequest request, CancellationToken cancellationToken)
    {
        var result = await addresses.CreateAsync(request, cancellationToken);
        return result.Succeeded ? Created($"/api/customer/addresses/{result.Value!.Id}", new { success = true, data = result.Value }) : Respond(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CustomerAddressRequest request, CancellationToken cancellationToken) => Respond(await addresses.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/default")]
    public async Task<IActionResult> MakeDefault(Guid id, CancellationToken cancellationToken) => Respond(await addresses.SetDefaultAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) => Respond(await addresses.DeleteAsync(id, cancellationToken), _ => null);

    private IActionResult Respond<T>(AccountResult<T> result, Func<T, object?>? data = null) => result.Status switch
    {
        AccountStatus.Succeeded => Ok(new { success = true, data = data is null ? result.Value : data(result.Value!) }),
        AccountStatus.InvalidRequest => BadRequest(new { success = false, message = result.Message, errors = result.Errors ?? [] }),
        AccountStatus.Conflict => Conflict(Failure(result.Message!)),
        AccountStatus.NotFound => NotFound(Failure(result.Message!)),
        _ => Unauthorized(Failure(result.Message!))
    };

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}
