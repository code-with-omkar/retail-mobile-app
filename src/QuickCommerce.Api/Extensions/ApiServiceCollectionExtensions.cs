using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using QuickCommerce.Api.Hosting;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddControllers();
        ValidateProductionConfiguration(configuration, environment);
        services.AddHealthChecks();
        services.AddHostedService<ShutdownLoggingHostedService>();
        services.AddOpenApi();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureJwt(options, configuration, environment));
        services.AddAuthorization(options =>
        {
            options.AddPolicy(SecurityPolicies.Orders, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("Customer") ||
                    context.User.IsInRole("StoreStaff") ||
                    context.User.IsInRole("Admin") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "orders:read")));
            options.AddPolicy(SecurityPolicies.StoreOrderOperations, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("StoreStaff") ||
                    context.User.IsInRole("Admin") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "orders:operate")));
        });
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter a JWT bearer token."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
        services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        return services;
    }

    private static void ConfigureJwt(JwtBearerOptions options, IConfiguration configuration, IHostEnvironment environment)
    {
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        var signingKey = configuration["Jwt:SigningKey"];
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured outside Development.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateIssuerSigningKey = !string.IsNullOrWhiteSpace(signingKey),
            IssuerSigningKey = string.IsNullOrWhiteSpace(signingKey) ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private static void ValidateProductionConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var requiredSettings = new[]
        {
            "Jwt:Issuer",
            "Jwt:Audience",
            "Jwt:SigningKey",
            "ConnectionStrings:QuickCommerceDb"
        };
        var missingSettings = requiredSettings.Where(setting => string.IsNullOrWhiteSpace(configuration[setting])).ToArray();
        if (missingSettings.Length > 0)
        {
            throw new InvalidOperationException($"Required production settings are missing: {string.Join(", ", missingSettings)}");
        }
    }
}
