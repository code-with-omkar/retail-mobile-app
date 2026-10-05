using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Caching;
using QuickCommerce.Infrastructure.Health;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;

namespace QuickCommerce.Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<QuickCommerceDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("QuickCommerceDb")));
        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sql-server", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        var redisConnection = configuration["Caching:RedisConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<ICacheService, NoOpCacheService>();
        }
        else
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
            services.AddSingleton<ICacheService, DistributedCacheService>();
        }
        services.AddScoped<ICommerceStore, EfCommerceStore>();
        services.AddScoped<IAuthorizationManagementService, AuthorizationManagementService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IAuthorizationScopeService, AuthorizationScopeService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddHostedService<DevelopmentAuthenticationSeedService>();
        services.AddScoped<ICurrentUserContextResolver, CurrentUserContextResolver>();
        return services;
    }
}
