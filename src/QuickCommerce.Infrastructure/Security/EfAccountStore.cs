using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class EfAccountStore(QuickCommerceDbContext db) : IAccountStore
{
    private const string SelfRegistration = "self-registration";
    private const string PasswordReset = "password-reset";

    public async Task<OrganizationChoice> ResolveRegistrationOrganizationAsync(Guid? configuredOrganizationId, CancellationToken cancellationToken = default)
    {
        if (configuredOrganizationId is { } configured)
        {
            return await db.Organizations.AnyAsync(organization => organization.Id == configured && organization.IsActive, cancellationToken)
                ? new OrganizationChoice(configured, null)
                : new OrganizationChoice(null, "Registration:OrganizationId does not match an active organization.");
        }

        var active = await db.Organizations.Where(organization => organization.IsActive).Select(organization => organization.Id).Take(2).ToListAsync(cancellationToken);
        return active.Count switch
        {
            1 => new OrganizationChoice(active[0], null),
            0 => new OrganizationChoice(null, "No active organization exists for registration."),
            _ => new OrganizationChoice(null, "Several organizations are active; set Registration:OrganizationId.")
        };
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        db.Users.AnyAsync(user => user.ExternalSubject == normalizedEmail || (user.Email != null && user.Email.ToLower() == normalizedEmail), cancellationToken);

    public async Task<bool> TryCreateCustomerAsync(NewCustomerAccount account, CancellationToken cancellationToken = default)
    {
        var role = await db.AuthorizationRoles.SingleOrDefaultAsync(item => item.Code == "Customer" && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The Customer authorization role is not configured.");
        var user = new User
        {
            ExternalSubject = account.Email,
            DisplayName = account.FullName,
            FirstName = account.FirstName,
            LastName = string.IsNullOrEmpty(account.LastName) ? null : account.LastName,
            Email = account.Email,
            PhoneNumber = account.PhoneNumber,
            OrganizationId = account.OrganizationId,
            Role = Role.Customer,
            CreatedBy = SelfRegistration
        };
        user.Credential = new UserCredential { UserId = user.Id, User = user, PasswordHash = account.PasswordHash, CreatedBy = SelfRegistration };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, IsActive = true, CreatedBy = SelfRegistration });
        user.Customer = new Customer { UserId = user.Id, User = user };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another request registered the same email between our check and the save (unique index on ExternalSubject).
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<AccountUserInfo?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(user => user.IsActive && user.Role == Role.Customer && user.Customer != null && user.Customer.IsActive
                && (user.ExternalSubject == normalizedEmail || (user.Email != null && user.Email.ToLower() == normalizedEmail)))
            .Select(user => new AccountUserInfo(user.Id, user.Email ?? user.ExternalSubject, user.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AccountUserInfo?> FindActiveCustomerByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive && user.Role == Role.Customer && user.Customer != null && user.Customer.IsActive)
            .Select(user => new AccountUserInfo(user.Id, user.Email ?? user.ExternalSubject, user.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountResetCodesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        db.PasswordResetCodes.CountAsync(code => code.UserId == userId && code.CreatedAt >= sinceUtc, cancellationToken);

    public async Task CreateResetCodeAsync(Guid userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await db.PasswordResetCodes.Where(code => code.UserId == userId && code.UsedAt == null).ExecuteUpdateAsync(set => set.SetProperty(code => code.UsedAt, createdUtc), cancellationToken);
        db.PasswordResetCodes.Add(new PasswordResetCode { UserId = userId, CodeHash = codeHash, CreatedAt = createdUtc, ExpiresAt = expiresUtc, RequestedFromIp = ipAddress });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<ResetCodeInfo?> GetOpenResetCodeAsync(Guid userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default) =>
        db.PasswordResetCodes.AsNoTracking()
            .Where(code => code.UserId == userId && code.UsedAt == null && code.ExpiresAt > nowUtc && code.FailedAttempts < maxAttempts)
            .OrderByDescending(code => code.CreatedAt)
            .Select(code => new ResetCodeInfo(code.Id, code.CodeHash, code.FailedAttempts))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task RecordFailedResetAttemptAsync(Guid codeId, CancellationToken cancellationToken = default) =>
        await db.PasswordResetCodes.Where(code => code.Id == codeId).ExecuteUpdateAsync(set => set.SetProperty(code => code.FailedAttempts, code => code.FailedAttempts + 1), cancellationToken);

    public async Task<bool> TryConsumeResetCodeAsync(Guid codeId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.PasswordResetCodes.Where(code => code.Id == codeId && code.UsedAt == null && code.ExpiresAt > nowUtc)
            .ExecuteUpdateAsync(set => set.SetProperty(code => code.UsedAt, nowUtc), cancellationToken) == 1;

    public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.UserCredentials.AsNoTracking().Where(credential => credential.UserId == userId && credential.IsActive).Select(credential => (string?)credential.PasswordHash).FirstOrDefaultAsync(cancellationToken);

    public async Task SetPasswordAsync(Guid userId, string passwordHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var credential = await db.UserCredentials.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (credential is null)
        {
            db.UserCredentials.Add(new UserCredential { UserId = userId, PasswordHash = passwordHash, CreatedBy = PasswordReset });
        }
        else
        {
            credential.PasswordHash = passwordHash;
            credential.PasswordChangedAt = nowUtc;
            credential.FailedLoginCount = 0;
            credential.LockedUntil = null;
            credential.IsActive = true;
            credential.UpdatedAt = nowUtc;
            credential.UpdatedBy = PasswordReset;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeSessionsAsync(Guid userId, string? exceptRefreshTokenHash, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null && (exceptRefreshTokenHash == null || token.TokenHash != exceptRefreshTokenHash))
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.RevokedAt, nowUtc).SetProperty(token => token.RevokedByIp, ipAddress), cancellationToken);

    public Task<CustomerProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive && user.Customer != null)
            .Select(user => new CustomerProfileResponse(user.Id, user.DisplayName, user.Email ?? user.ExternalSubject, user.PhoneNumber))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task UpdateProfileAsync(Guid userId, string fullName, string firstName, string lastName, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleAsync(item => item.Id == userId && item.Customer != null, cancellationToken);
        user.DisplayName = fullName;
        user.FirstName = firstName;
        user.LastName = string.IsNullOrEmpty(lastName) ? null : lastName;
        user.PhoneNumber = phoneNumber;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = "self-service";
        await db.SaveChangesAsync(cancellationToken);
    }
}
