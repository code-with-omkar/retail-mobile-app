using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using QuickCommerce.Api.Middleware;
using QuickCommerce.Api.Security;

namespace QuickCommerce.Api.Extensions;

public static class ApiApplicationBuilderExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        if (AuthRateLimiting.KnownProxies(app.Configuration).Count > 0)
        {
            app.UseForwardedHeaders();
        }
        else if (!app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning("Proxy:KnownProxies is not configured. Behind a load balancer every client appears to have the balancer's IP, so per-IP rate limits would apply to all users together.");
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RequestCorrelationMiddleware>();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            if (app.Services.GetRequiredService<CorsOriginsInfo>().Count == 0)
            {
                app.Logger.LogWarning("Cors:AllowedOrigins is empty, so no browser page (such as the admin portal) can call this API. List the portal's address there.");
            }
        }

        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
            app.Logger.LogError(exception, "Unhandled exception for request {RequestPath} with correlation {CorrelationId}", context.Request.Path, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "An unexpected error occurred.",
                errors = Array.Empty<string>()
            });
        }));

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors();
        app.UseRateLimiter();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        // Product pictures. Names are random and never reused, so browsers may keep them; nothing here is ever run or sniffed as another type.
        var media = app.Services.GetRequiredService<QuickCommerce.Infrastructure.Media.LocalImageStore>();
        Directory.CreateDirectory(media.RootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(media.RootPath),
            RequestPath = QuickCommerce.Infrastructure.Media.LocalImageStore.RequestPath,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Context.Response.Headers["Cache-Control"] = "public, max-age=86400";
                context.Context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }
}
