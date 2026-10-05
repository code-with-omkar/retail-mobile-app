using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class AuthenticationService(
    QuickCommerceDbContext db,
    IPasswordHasher<User> passwordHasher,
    IAuthorizationScopeService authorizationScopeService,
    ICurrentUser currentUser,
    IConfiguration configuration) : IAuthenticationService
{
    private const string GenericFailure = "Invalid username or password.";

    public async Task<AuthenticationResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users.Include(item => item.Credential)
            .SingleOrDefaultAsync(item => item.Email != null && item.Email.ToLower() == normalizedUsername || item.ExternalSubject == normalizedUsername, cancellationToken);
        if (user is null || !user.IsActive || user.Credential is null || !user.Credential.IsActive || user.Credential.LockedUntil > DateTime.UtcNow)
        {
            return AuthenticationResult.Failure(GenericFailure);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.Credential.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.Credential.FailedLoginCount++;
            if (user.Credential.FailedLoginCount >= 5)
            {
                user.Credential.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            }

            await db.SaveChangesAsync(cancellationToken);
            return AuthenticationResult.Failure(GenericFailure);
        }

        user.Credential.FailedLoginCount = 0;
        user.Credential.LockedUntil = null;
        await db.SaveChangesAsync(cancellationToken);
        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthenticationResult> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var current = await db.RefreshTokens.Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        if (current is null || current.RevokedAt.HasValue || current.ExpiresAt <= DateTime.UtcNow || !current.User.IsActive)
        {
            return AuthenticationResult.Failure("Invalid refresh token.");
        }

        var result = await IssueTokensAsync(current.User, ipAddress, cancellationToken);
        if (!result.Succeeded || result.Response is null)
        {
            return result;
        }

        var replacementHash = HashToken(result.Response.RefreshToken);
        var replacement = await db.RefreshTokens.SingleAsync(token => token.TokenHash == replacementHash, cancellationToken);
        current.RevokedAt = DateTime.UtcNow;
        current.ReplacedByTokenId = replacement.Id;
        current.RevokedByIp = ipAddress;
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<bool> LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var token = await db.RefreshTokens.SingleOrDefaultAsync(item => item.TokenHash == HashToken(refreshToken), cancellationToken);
        if (token is null || token.RevokedAt.HasValue)
        {
            return false;
        }

        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AuthenticationUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        if (!userId.HasValue)
        {
            return null;
        }

        return await MapUserAsync(userId.Value, cancellationToken);
    }

    private async Task<AuthenticationResult> IssueTokensAsync(User user, string? ipAddress, CancellationToken cancellationToken)
    {
        var scope = await authorizationScopeService.ResolveForUserAsync(user.Id, cancellationToken);
        if (scope is null)
        {
            return AuthenticationResult.Failure(GenericFailure);
        }

        var sessionId = Guid.NewGuid();
        var accessMinutes = configuration.GetValue("Jwt:AccessTokenMinutes", 15);
        var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 7);
        var expiresAt = DateTime.UtcNow.AddMinutes(accessMinutes);
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            SessionId = sessionId,
            TokenHash = HashToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(refreshDays),
            CreatedByIp = ipAddress
        });
        await db.SaveChangesAsync(cancellationToken);

        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured for authentication.");
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, sessionId.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email ?? user.ExternalSubject),
            new("organization_id", user.OrganizationId.ToString())
        };
        claims.AddRange(scope.RoleCodes.Select(role => new Claim(ClaimTypes.Role, role)));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return AuthenticationResult.Success(new AuthenticationResponse(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            rawRefreshToken,
            (int)(expiresAt - DateTime.UtcNow).TotalSeconds,
            "Bearer",
            new AuthenticationUserResponse(user.Id, user.DisplayName, user.Email, user.OrganizationId, scope.RoleCodes.ToArray(), scope.PermissionCodes.ToArray(), user.StaffCategory)));
    }

    private async Task<AuthenticationUserResponse?> MapUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        var scope = user is null ? null : await authorizationScopeService.ResolveForUserAsync(userId, cancellationToken);
        return user is null || scope is null
            ? null
            : new AuthenticationUserResponse(user.Id, user.DisplayName, user.Email, user.OrganizationId, scope.RoleCodes.ToArray(), scope.PermissionCodes.ToArray(), user.StaffCategory);
    }

    private Guid? GetUserId() => Guid.TryParse(currentUser.UserId, out var userId) ? userId : null;

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}