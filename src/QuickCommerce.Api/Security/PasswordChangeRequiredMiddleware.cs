using QuickCommerce.Infrastructure.Security;

namespace QuickCommerce.Api.Security;

/// <summary>Someone signed in with a temporary password may only choose their own password, or sign out.</summary>
public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    private static readonly string[] Allowed = ["/api/auth/change-password", "/api/auth/logout", "/api/auth/refresh", "/api/auth/login", "/api/auth/me"];
    public const string Reason = "PasswordChangeRequired";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(PasswordClaims.Type, PasswordClaims.ChangeRequired)
            && !Allowed.Any(path => context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Change your password to continue.", reason = Reason, errors = Array.Empty<string>() });
            return;
        }

        await next(context);
    }
}
