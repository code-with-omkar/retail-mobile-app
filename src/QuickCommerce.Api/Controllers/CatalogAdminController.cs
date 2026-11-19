using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>Editing the shared catalogue (administrators) and each store's own stock (store staff for their store, administrators for any).</summary>
[ApiController]
public sealed class CatalogAdminController(ICatalogAdminService catalogue, IStockService stock) : ControllerBase
{
    [HttpGet("api/admin/catalog/products")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Respond(await catalogue.ListAsync(search, page, pageSize, cancellationToken));

    [HttpGet("api/admin/catalog/products/{id:guid}")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => Respond(await catalogue.GetAsync(id, cancellationToken));

    [HttpPost("api/admin/catalog/products")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken cancellationToken) => Respond(await catalogue.CreateAsync(request, cancellationToken), created: true);

    [HttpPut("api/admin/catalog/products/{id:guid}")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, CancellationToken cancellationToken) => Respond(await catalogue.UpdateAsync(id, request, cancellationToken));

    [HttpPut("api/admin/catalog/products/{id:guid}/translations/{locale}")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> SaveTranslation(Guid id, string locale, SaveTranslationRequest request, CancellationToken cancellationToken) => Respond(await catalogue.SaveTranslationAsync(id, locale, request, cancellationToken));

    [HttpDelete("api/admin/catalog/products/{id:guid}/translations/{locale}")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> DeleteTranslation(Guid id, string locale, CancellationToken cancellationToken) => Respond(await catalogue.DeleteTranslationAsync(id, locale, cancellationToken));

    [HttpPost("api/admin/catalog/products/{id:guid}/variants")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> AddVariant(Guid id, SaveVariantRequest request, CancellationToken cancellationToken) => Respond(await catalogue.AddVariantAsync(id, request, cancellationToken));

    [HttpPut("api/admin/catalog/products/{id:guid}/variants/{variantId:guid}")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> UpdateVariant(Guid id, Guid variantId, SaveVariantRequest request, CancellationToken cancellationToken) => Respond(await catalogue.UpdateVariantAsync(id, variantId, request, cancellationToken));

    [HttpPost("api/admin/catalog/products/{id:guid}/variants/{variantId:guid}/default")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> MakeDefault(Guid id, Guid variantId, CancellationToken cancellationToken) => Respond(await catalogue.MakeDefaultAsync(id, variantId, cancellationToken));

    /// <summary>One picture as the form field "file". What counts is the content (JPEG, PNG or WebP, up to 2 MB), never the file name or the type it claims.</summary>
    [HttpPost("api/admin/catalog/products/{id:guid}/image")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> SetImage(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(Failure("Choose a picture."));
        }

        await using var stream = file.OpenReadStream();
        return Respond(await catalogue.SetImageAsync(id, stream, file.Length, cancellationToken));
    }

    [HttpDelete("api/admin/catalog/products/{id:guid}/image")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> RemoveImage(Guid id, CancellationToken cancellationToken) => Respond(await catalogue.RemoveImageAsync(id, cancellationToken));

    [HttpGet("api/stock")]
    [Authorize(Policy = SecurityPolicies.StockManagement)]
    public async Task<IActionResult> Stock([FromQuery] Guid? storeId, CancellationToken cancellationToken) => Respond(await stock.ListAsync(storeId, cancellationToken));

    [HttpPut("api/stock/{variantId:guid}")]
    [Authorize(Policy = SecurityPolicies.StockManagement)]
    public async Task<IActionResult> SetStock(Guid variantId, [FromQuery] Guid? storeId, SetStockRequest request, CancellationToken cancellationToken) => Respond(await stock.SetAsync(storeId, variantId, request, cancellationToken));

    private IActionResult Respond<T>(CatalogAdminResult<T> result, bool created = false) => result.Status switch
    {
        CatalogAdminStatus.Succeeded => created ? StatusCode(StatusCodes.Status201Created, new { success = true, data = result.Value }) : Ok(new { success = true, data = result.Value }),
        CatalogAdminStatus.Forbidden => Forbid(),
        CatalogAdminStatus.NotFound => NotFound(Failure(result.Message!)),
        CatalogAdminStatus.Invalid => BadRequest(Failure(result.Message!)),
        _ => Conflict(Failure(result.Message!))
    };

    private static object Failure(string message) => new { success = false, message, errors = Array.Empty<string>() };
}
