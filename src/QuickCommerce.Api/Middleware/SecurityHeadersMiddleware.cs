namespace QuickCommerce.Api.Middleware;

/// <summary>Headers every API response should carry. Answers about a person's account are also never kept by a browser or a proxy.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly string[] Private = ["/api/auth", "/api/customer", "/api/account", "/api/admin", "/api/delivery", "/api/placement", "/api/stock"];

    public Task InvokeAsync(HttpContext context)
    {
        // Set up front: the values do not depend on the answer, and nothing later in the pipeline needs to change them.
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        if (Private.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            headers["Cache-Control"] = "no-store";
        }

        return next(context);
    }
}
