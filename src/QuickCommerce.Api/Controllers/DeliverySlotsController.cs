using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

[ApiController]
public sealed class DeliverySlotsController(ISlotService slots) : ControllerBase
{
    /// <summary>Administrators: the default settings and every store's own.</summary>
    [HttpGet("api/admin/delivery-slots")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> Rules(CancellationToken cancellationToken) => Respond(await slots.GetRulesAsync(cancellationToken));

    [HttpPut("api/admin/delivery-slots/default")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> SetDefault(SlotSettingsDto settings, CancellationToken cancellationToken) => Respond(await slots.SetDefaultAsync(settings, cancellationToken));

    /// <summary>One store's own settings (use DELETE to go back to the default).</summary>
    [HttpPut("api/admin/delivery-slots/stores/{storeId:guid}")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> SetStore(Guid storeId, SlotSettingsDto settings, CancellationToken cancellationToken) => Respond(await slots.SetStoreAsync(storeId, settings, cancellationToken));

    /// <summary>Takes a store's own settings away, so the default applies again.</summary>
    [HttpDelete("api/admin/delivery-slots/stores/{storeId:guid}")]
    [Authorize(Policy = SecurityPolicies.AdminManagement)]
    public async Task<IActionResult> ClearStore(Guid storeId, CancellationToken cancellationToken) => Respond(await slots.SetStoreAsync(storeId, null, cancellationToken));

    /// <summary>The windows a customer can choose in this store, with what is left in each.</summary>
    [HttpGet("api/stores/{storeId:guid}/delivery-slots")]
    [Authorize(Policy = SecurityPolicies.Orders)]
    public async Task<IActionResult> List(Guid storeId, CancellationToken cancellationToken) => Respond(await slots.ListAsync(storeId, cancellationToken));

    private IActionResult Respond<T>(SlotResult<T> result)
    {
        var body = new { success = result.Status == SlotServiceStatus.Succeeded, data = result.Value, message = result.Message, errors = Array.Empty<string>() };
        return result.Status switch
        {
            SlotServiceStatus.Succeeded => Ok(body),
            SlotServiceStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            SlotServiceStatus.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
