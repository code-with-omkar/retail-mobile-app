using Microsoft.AspNetCore.Cors.Infrastructure;

namespace QuickCommerce.Api.Security;

/// <summary>
/// Which web pages may call the API from a browser. The apps do not use CORS. In Development any page may (the portal runs on changing local
/// ports). Anywhere else only the origins listed in <c>Cors:AllowedOrigins</c> may, and with none listed no browser page may.
/// </summary>
public static class ApiCors
{
    public static CorsPolicy Build(bool development, IReadOnlyCollection<string> origins)
    {
        var builder = new CorsPolicyBuilder().AllowAnyHeader().AllowAnyMethod();
        if (development)
        {
            builder.AllowAnyOrigin();
        }
        else if (origins.Count > 0)
        {
            builder.WithOrigins(origins.ToArray());
        }

        return builder.Build();
    }

    /// <summary>Each entry must be a bare origin such as https://portal.example.com (no path); a trailing slash is dropped. Anything else stops the API from starting.</summary>
    public static IReadOnlyList<string> ParseOrigins(IEnumerable<string>? configured) =>
        (configured ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item =>
        {
            var text = item.Trim().TrimEnd('/');
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || text.Contains('*'))
            {
                throw new InvalidOperationException($"Cors:AllowedOrigins entry '{item}' must be an origin such as https://portal.example.com (no path, no wildcard).");
            }

            return text;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
