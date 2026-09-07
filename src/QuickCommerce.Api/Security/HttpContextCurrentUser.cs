using System.Security.Claims;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Security;

public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    public Guid? OrganizationId => ParseGuid("organization_id");

    public Guid? StoreId => ParseGuid("store_id");

    public IReadOnlyCollection<string> Roles => User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();

    public IReadOnlyCollection<string> Permissions => User.FindAll("permission").Select(claim => claim.Value).ToArray();

    private Guid? ParseGuid(string claimType) => Guid.TryParse(User.FindFirstValue(claimType), out var value) ? value : null;
}