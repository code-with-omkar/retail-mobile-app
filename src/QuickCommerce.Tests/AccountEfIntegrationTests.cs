using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Opt-in, against a DISPOSABLE SQL Server database (QUICKCOMMERCE_TEST_CONNECTION_STRING), like the other SQL tests.
/// Proves what the in-memory tests cannot: the unique-index race on registration, atomic single use of a reset code,
/// attempt counting, session revocation and hashing, all through the real EF queries. Rows it creates are removed again.
/// </summary>
public sealed class AccountEfIntegrationTests
{
    private const string Key = "integration-test-reset-code-key-0123456789";

    [Fact]
    public async Task Account_flows_work_against_a_real_sql_server()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUICKCOMMERCE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(connectionString).Options;
        await using (var migrate = new QuickCommerceDbContext(options))
        {
            await migrate.Database.MigrateAsync();
        }

        var token = "it" + Guid.NewGuid().ToString("N")[..10];
        var email = $"{token}@example.test";
        var otherEmail = $"{token}-2@example.test";
        var mail = new CapturingEmail();

        AccountService NewService(QuickCommerceDbContext db) => new(
            new EfAccountStore(db), new IdentityPasswordHashing(new PasswordHasher<User>()), new StubAuthentication(), mail, new SignedOutUser(),
            new RegisterCustomerRequestValidator(), new ForgotPasswordRequestValidator(), new ResetPasswordRequestValidator(),
            new ChangePasswordRequestValidator(), new UpdateCustomerProfileRequestValidator(),
            new AccountSettings { ResetCodeKey = Key }, new RegistrationSettings(), TimeProvider.System);

        try
        {
            // ----- registration creates user, customer, credential and role in the only active organization -----
            await using var db1 = new QuickCommerceDbContext(options);
            var registered = await NewService(db1).RegisterAsync(new RegisterCustomerRequest("Asha Patil", email.ToUpperInvariant(), "Sunrise-42", "98765 43210"), "10.0.0.1");
            Assert.True(registered.Succeeded, registered.Message);

            await using var read = new QuickCommerceDbContext(options);
            var user = await read.Users.Include(u => u.Credential).Include(u => u.Customer).Include(u => u.UserRoles).ThenInclude(r => r.Role).SingleAsync(u => u.ExternalSubject == email);
            Assert.Equal(("Asha Patil", "Asha", "Patil", "9876543210", Role.Customer), (user.DisplayName, user.FirstName, user.LastName, user.PhoneNumber, user.Role));
            Assert.NotNull(user.Customer);
            Assert.Equal("Customer", user.UserRoles.Single().Role.Code);
            Assert.True(new IdentityPasswordHashing(new PasswordHasher<User>()).Verify(user.Credential!.PasswordHash, "Sunrise-42"));
            Assert.DoesNotContain("Sunrise-42", user.Credential.PasswordHash);

            // ----- the same email again: conflict through the check, and through the unique index when the check is bypassed -----
            await using var db2 = new QuickCommerceDbContext(options);
            Assert.Equal(AccountStatus.Conflict, (await NewService(db2).RegisterAsync(new RegisterCustomerRequest("Asha Two", email, "Sunrise-42", null), null)).Status);
            await using var db3 = new QuickCommerceDbContext(options);
            var raced = await new EfAccountStore(db3).TryCreateCustomerAsync(new NewCustomerAccount(user.OrganizationId, "Race", "Race", "", email, null, "x"));
            Assert.False(raced);
            Assert.Equal(1, await read.Users.CountAsync(u => u.ExternalSubject == email));

            // ----- organization choice -----
            await using var orgDb = new QuickCommerceDbContext(options);
            var orgStore = new EfAccountStore(orgDb);
            Assert.Equal(user.OrganizationId, (await orgStore.ResolveRegistrationOrganizationAsync(null)).OrganizationId);
            Assert.NotNull((await orgStore.ResolveRegistrationOrganizationAsync(Guid.NewGuid())).Error);

            // ----- reset: code is emailed, stored only as a keyed hash, and works exactly once even when submitted twice at the same moment -----
            await using var db4 = new QuickCommerceDbContext(options);
            await NewService(db4).RequestPasswordResetAsync(new ForgotPasswordRequest(email), null, "10.0.0.2");
            var code = ResetCodes.Normalize(System.Text.RegularExpressions.Regex.Match(mail.Last!.Body, "[A-Z0-9]{4}-[A-Z0-9]{4}").Value);
            var storedHash = await read.PasswordResetCodes.Where(c => c.UserId == user.Id).Select(c => c.CodeHash).SingleAsync();
            Assert.Equal(ResetCodes.Hash(Key, user.Id, code), storedHash);
            Assert.DoesNotContain(code, storedHash);

            var sessionA = new RefreshToken { UserId = user.Id, SessionId = Guid.NewGuid(), TokenHash = token + "A", ExpiresAt = DateTime.UtcNow.AddDays(1) };
            var sessionB = new RefreshToken { UserId = user.Id, SessionId = Guid.NewGuid(), TokenHash = token + "B", ExpiresAt = DateTime.UtcNow.AddDays(1) };
            await using (var seed = new QuickCommerceDbContext(options))
            {
                seed.RefreshTokens.AddRange(sessionA, sessionB);
                await seed.SaveChangesAsync();
            }

            await using var raceA = new QuickCommerceDbContext(options);
            await using var raceB = new QuickCommerceDbContext(options);
            var results = await Task.WhenAll(
                NewService(raceA).ResetPasswordAsync(new ResetPasswordRequest(email, code, "BrandNew-77"), "10.0.0.3"),
                NewService(raceB).ResetPasswordAsync(new ResetPasswordRequest(email, code, "BrandNew-88"), "10.0.0.4"));
            Assert.Equal(1, results.Count(result => result.Succeeded));

            await using var after = new QuickCommerceDbContext(options);
            Assert.NotNull((await after.PasswordResetCodes.SingleAsync(c => c.UserId == user.Id)).UsedAt);
            Assert.All(await after.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(), t => Assert.NotNull(t.RevokedAt));
            var newHash = (await after.UserCredentials.SingleAsync(c => c.UserId == user.Id)).PasswordHash;
            Assert.True(new IdentityPasswordHashing(new PasswordHasher<User>()).Verify(newHash, "BrandNew-77") ^ new IdentityPasswordHashing(new PasswordHasher<User>()).Verify(newHash, "BrandNew-88"));

            // ----- wrong-code attempts are counted atomically, then the code is dead -----
            await using var db5 = new QuickCommerceDbContext(options);
            await NewService(db5).RequestPasswordResetAsync(new ForgotPasswordRequest(email), null, null);
            var code2 = ResetCodes.Normalize(System.Text.RegularExpressions.Regex.Match(mail.Last!.Body, "[A-Z0-9]{4}-[A-Z0-9]{4}").Value);
            for (var i = 0; i < 5; i++)
            {
                await using var attempt = new QuickCommerceDbContext(options);
                Assert.Equal(AccountStatus.InvalidRequest, (await NewService(attempt).ResetPasswordAsync(new ResetPasswordRequest(email, "ZZZZ-ZZZZ", "Another-99"), null)).Status);
            }

            await using var db6 = new QuickCommerceDbContext(options);
            Assert.Equal(AccountStatus.InvalidRequest, (await NewService(db6).ResetPasswordAsync(new ResetPasswordRequest(email, code2, "Another-99"), null)).Status);
            Assert.Equal(5, (await read.PasswordResetCodes.Where(c => c.UserId == user.Id && c.UsedAt == null).SingleAsync()).FailedAttempts);

            // ----- revoking sessions except one keeps exactly that one -----
            await using (var reopen = new QuickCommerceDbContext(options))
            {
                await reopen.RefreshTokens.Where(t => t.UserId == user.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, (DateTime?)null));
            }

            await using var db7 = new QuickCommerceDbContext(options);
            await new EfAccountStore(db7).RevokeSessionsAsync(user.Id, token + "A", "10.0.0.5", DateTime.UtcNow);
            await using var check = new QuickCommerceDbContext(options);
            var tokens = await check.RefreshTokens.Where(t => t.UserId == user.Id).ToDictionaryAsync(t => t.TokenHash);
            Assert.Null(tokens[token + "A"].RevokedAt);
            Assert.NotNull(tokens[token + "B"].RevokedAt);

            // ----- profile -----
            await using var db8 = new QuickCommerceDbContext(options);
            await new EfAccountStore(db8).UpdateProfileAsync(user.Id, "Asha Kulkarni", "Asha", "Kulkarni", "+919876543210");
            await using var db9 = new QuickCommerceDbContext(options);
            var profile = await new EfAccountStore(db9).GetProfileAsync(user.Id);
            Assert.Equal(("Asha Kulkarni", email, "+919876543210"), (profile!.FullName, profile.Email, profile.PhoneNumber));
        }
        finally
        {
            await using var cleanup = new QuickCommerceDbContext(options);
            var ids = await cleanup.Users.Where(u => u.ExternalSubject.StartsWith(token)).Select(u => u.Id).ToListAsync();
            await cleanup.PasswordResetCodes.Where(c => ids.Contains(c.UserId)).ExecuteDeleteAsync();
            await cleanup.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
            await cleanup.UserRoles.Where(r => ids.Contains(r.UserId)).ExecuteDeleteAsync();
            await cleanup.UserCredentials.Where(c => ids.Contains(c.UserId)).ExecuteDeleteAsync();
            await cleanup.Customers.Where(c => ids.Contains(c.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
            _ = otherEmail;
        }
    }

    private sealed class CapturingEmail : IEmailSender
    {
        public EmailMessage? Last { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Last = message;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthentication : IAuthenticationService
    {
        public Task<AuthenticationResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationResult.Success(new AuthenticationResponse("a", "r", 900, "Bearer", new AuthenticationUserResponse(Guid.NewGuid(), "x", request.Username, Guid.NewGuid(), ["Customer"], []))));

        public Task<AuthenticationResult> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthenticationUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SignedOutUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? UserId => null;
        public Guid? OrganizationId => null;
        public Guid? StoreId => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
