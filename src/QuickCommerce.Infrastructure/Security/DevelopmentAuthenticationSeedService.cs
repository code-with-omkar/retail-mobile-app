using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class DevelopmentAuthenticationSeedService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DevelopmentAuthenticationSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var passwords = new Dictionary<string, string?>
        {
            ["admin"] = configuration["Authentication:DevelopmentAdminPassword"],
            ["storemanager"] = configuration["Authentication:DevelopmentStoreManagerPassword"],
            ["storeemployee"] = configuration["Authentication:DevelopmentStoreEmployeePassword"]
        };
        if (passwords.Values.All(string.IsNullOrWhiteSpace))
        {
            logger.LogWarning("Development admin credential was not initialized because Authentication:DevelopmentAdminPassword is not configured.");
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QuickCommerceDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            foreach (var credential in passwords.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
            {
                var user = await db.Users.SingleOrDefaultAsync(item => item.ExternalSubject == credential.Key, cancellationToken);
                if (user is null || await db.UserCredentials.AnyAsync(item => item.UserId == user.Id, cancellationToken)) continue;
                db.UserCredentials.Add(new UserCredential { UserId = user.Id, PasswordHash = hasher.HashPassword(user, credential.Value!), CreatedBy = "development-seed" });
            }
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Initialized configured development credentials for seeded identities.");
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Development admin credential initialization was skipped because the database is not ready.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}