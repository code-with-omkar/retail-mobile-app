using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Middleware;

/// <summary>
/// A platform administrator looking at another organization (header X-Organization-Id) may read but not change anything.
/// Every such request is logged. Signing out stays possible.
/// </summary>
public sealed class ReadOnlyInOtherOrganizationMiddleware(RequestDelegate next, ILogger<ReadOnlyInOtherOrganizationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IActingOrganization acting, IAuthorizationScopeService scopes)
    {
        if (acting.Requested is { } requested && context.Request.Path.StartsWithSegments("/api") && context.User.Identity?.IsAuthenticated == true)
        {
            var scope = await scopes.ResolveAsync(context.RequestAborted);
            if (scope is null)
            {
                await Refuse(context, "You cannot look at another organization.");
                return;
            }

            if (scope.ActingFromOrganizationId is not null)
            {
                logger.LogInformation("Platform admin {UserId} from organization {Home} looks at organization {Other}: {Method} {Path}", scope.UserId, scope.ActingFromOrganizationId, requested, context.Request.Method, context.Request.Path.Value);
                var safe = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
                if (!safe && !context.Request.Path.StartsWithSegments("/api/auth"))
                {
                    await Refuse(context, "You are looking at another organization, so it is read-only. Switch back to your own organization to make changes.");
                    return;
                }
            }
        }

        await next(context);
    }

    private static async Task Refuse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { success = false, message });
    }
}
