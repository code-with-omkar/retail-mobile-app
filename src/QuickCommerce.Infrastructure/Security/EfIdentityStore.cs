using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

/// <summary>Text-message codes, verified numbers and Google links on SQL Server. The unique indexes (a verified number, a Google id, an email as sign-in name) are the last word when two requests race.</summary>
public sealed class EfIdentityStore(QuickCommerceDbContext db) : IIdentityStore
{
    private const string System = "self-service";

    public Task<int> CountOtpRequestsSinceAsync(string phone, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        db.OtpChallenges.CountAsync(item => item.Phone == phone && item.CreatedAt >= sinceUtc, cancellationToken);

    public async Task<OtpRecord?> GetOpenOtpAsync(string phone, string purpose, Guid? userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default) =>
        await db.OtpChallenges.AsNoTracking()
            .Where(item => item.Phone == phone && item.Purpose == purpose && item.UserId == userId && item.UsedAt == null && item.ExpiresAt > nowUtc && item.FailedAttempts < maxAttempts)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new OtpRecord(item.Id, item.CodeHash, item.FailedAttempts, item.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task CreateOtpAsync(string phone, string purpose, Guid? userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await db.OtpChallenges.Where(item => item.Phone == phone && item.Purpose == purpose && item.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.UsedAt, createdUtc), cancellationToken);
        db.OtpChallenges.Add(new OtpChallenge { Phone = phone, Purpose = purpose, UserId = userId, CodeHash = codeHash, CreatedAt = createdUtc, ExpiresAt = expiresUtc, RequestedFromIp = ipAddress });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public Task RecordFailedOtpAttemptAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.OtpChallenges.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.FailedAttempts, item => item.FailedAttempts + 1), cancellationToken);

    public async Task<bool> TryConsumeOtpAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.OtpChallenges.Where(item => item.Id == id && item.UsedAt == null).ExecuteUpdateAsync(set => set.SetProperty(item => item.UsedAt, nowUtc), cancellationToken) == 1;

    public Task<Guid?> FindActiveCustomerByVerifiedPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(user => user.PhoneNumber == phone && user.PhoneVerifiedAt != null && user.IsActive && user.Role == Role.Customer && user.Customer != null && user.Customer.IsActive)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsDuplicate(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 };

    private async Task<User> NewCustomerAsync(Guid organizationId, string subject, string fullName, CancellationToken cancellationToken)
    {
        var role = await db.AuthorizationRoles.SingleOrDefaultAsync(item => item.Code == "Customer" && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The Customer authorization role is not configured.");
        var parts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var user = new User
        {
            ExternalSubject = subject,
            DisplayName = fullName,
            FirstName = parts.Length > 0 ? parts[0] : fullName,
            LastName = parts.Length > 1 ? parts[1] : null,
            OrganizationId = organizationId,
            Role = Role.Customer,
            CreatedBy = System
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, IsActive = true, CreatedBy = System });
        user.Customer = new Customer { UserId = user.Id, User = user };
        return user;
    }

    public async Task<Guid?> CreatePhoneCustomerAsync(Guid organizationId, string phone, string fullName, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var user = await NewCustomerAsync(organizationId, "phone:" + phone, fullName, cancellationToken);
        user.PhoneNumber = phone;
        user.PhoneVerifiedAt = nowUtc;
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return user.Id;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // The same number signed up a moment ago in another request: both land in that one account.
            db.ChangeTracker.Clear();
            return await FindActiveCustomerByVerifiedPhoneAsync(phone, cancellationToken);
        }
    }

    public async Task<bool> TryMarkPhoneVerifiedAsync(Guid userId, string phone, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var holder = await db.Users.AsNoTracking().Where(user => user.PhoneNumber == phone && user.PhoneVerifiedAt != null).Select(user => (Guid?)user.Id).FirstOrDefaultAsync(cancellationToken);
        if (holder is not null && holder != userId)
        {
            return false;
        }

        try
        {
            await db.Users.Where(user => user.Id == userId && user.Role == Role.Customer)
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.PhoneNumber, phone).SetProperty(user => user.PhoneVerifiedAt, nowUtc), cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return false;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return false;
        }
    }

    private Task<CustomerIdentity?> FindIdentityAsync(IQueryable<User> users, CancellationToken cancellationToken) =>
        users.AsNoTracking()
            .Where(user => user.IsActive && user.Role == Role.Customer && user.Customer != null && user.Customer.IsActive)
            .Select(user => new CustomerIdentity(user.Id, user.EmailVerifiedAt != null, user.Credential != null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<CustomerIdentity?> FindActiveCustomerByGoogleAsync(string subject, CancellationToken cancellationToken = default) =>
        FindIdentityAsync(db.Users.Where(user => user.GoogleSubject == subject), cancellationToken);

    public Task<CustomerIdentity?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        FindIdentityAsync(db.Users.Where(user => user.ExternalSubject == normalizedEmail || (user.Email != null && user.Email.ToLower() == normalizedEmail)), cancellationToken);

    public async Task<Guid?> CreateGoogleCustomerAsync(Guid organizationId, string normalizedEmail, string fullName, string subject, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var user = await NewCustomerAsync(organizationId, normalizedEmail, fullName, cancellationToken);
        user.Email = normalizedEmail;
        user.EmailVerifiedAt = nowUtc;
        user.GoogleSubject = subject;
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return user.Id;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // Another request made this person a moment ago (same Google id), or the email belongs to an account we must not link (staff).
            db.ChangeTracker.Clear();
            return (await FindActiveCustomerByGoogleAsync(subject, cancellationToken))?.UserId;
        }
    }

    public async Task LinkGoogleAsync(Guid userId, string subject, bool wipeSignIn, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.GoogleSubject, subject).SetProperty(user => user.EmailVerifiedAt, nowUtc), cancellationToken);
        if (wipeSignIn)
        {
            await db.UserCredentials.Where(credential => credential.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.PasswordResetCodes.Where(code => code.UserId == userId && code.UsedAt == null).ExecuteUpdateAsync(set => set.SetProperty(code => code.UsedAt, nowUtc), cancellationToken);
            await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(token => token.RevokedAt, nowUtc).SetProperty(token => token.RevokedByIp, ipAddress), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
