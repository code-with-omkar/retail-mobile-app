using Microsoft.Extensions.Primitives;

namespace QuickCommerce.Api.Middleware;

public sealed class RequestCorrelationMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out StringValues value) &&
            !StringValues.IsNullOrEmpty(value) &&
            value.ToString().Length <= 128 &&
            value.ToString().All(character => !char.IsControl(character))
                ? value.ToString()
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        await next(context);
    }
}