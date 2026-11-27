using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Controllers;

/// <summary>The category tree: administrators add categories and put one under another (sub-categories).</summary>
[ApiController]
public sealed class CategoryAdminController(ICategoryAdminService categories) : ControllerBase
{
    [HttpGet("api/admin/catalog/categories")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Respond(await categories.ListAsync(cancellationToken));

    [HttpPost("api/admin/catalog/categories")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken cancellationToken) => Respond(await categories.CreateAsync(request, cancellationToken), created: true);

    [HttpPut("api/admin/catalog/categories/{id:guid}")]
    [Authorize(Policy = SecurityPolicies.CatalogManagement)]
    public async Task<IActionResult> Update(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken) => Respond(await categories.UpdateAsync(id, request, cancellationToken));

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
