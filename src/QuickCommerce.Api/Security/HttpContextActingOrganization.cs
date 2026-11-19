using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Security;

public sealed class HttpContextActingOrganization(IHttpContextAccessor accessor) : IActingOrganization
{
    public const string HeaderName = "X-Organization-Id";

    public Guid? Requested => Guid.TryParse(accessor.HttpContext?.Request.Headers[HeaderName].ToString(), out var id) && id != Guid.Empty ? id : null;
}
