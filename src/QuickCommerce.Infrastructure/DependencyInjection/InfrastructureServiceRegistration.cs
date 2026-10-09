using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Infrastructure.Email;
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
        services.AddScoped<IPasswordHashing, IdentityPasswordHashing>();
        services.AddScoped<IAccountStore, EfAccountStore>();
        services.AddScoped<IAddressStore, EfAddressStore>();
        services.AddScoped<IPaymentStore, EfPaymentStore>();
        services.AddScoped<ICampaignStore, EfCampaignStore>();
        // Settings are normally registered (and validated) by the API; these defaults only apply when it did not.
        services.TryAddSingleton(new EmailSettings());
        services.TryAddSingleton(new RegistrationSettings());
        services.AddScoped<IEmailSender>(provider => provider.GetRequiredService<EmailSettings>().Mode == EmailMode.Smtp
            ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider)
            : ActivatorUtilities.CreateInstance<LoggingEmailSender>(provider));
        services.AddHostedService<DevelopmentAuthenticationSeedService>();
        services.AddScoped<ICurrentUserContextResolver, CurrentUserContextResolver>();
        return services;
    }
}
