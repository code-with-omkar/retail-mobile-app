using Microsoft.AspNetCore.Identity;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Security;

/// <summary>Wraps the ASP.NET Core Identity hasher (the one login already verifies against), so Application stays framework-neutral.</summary>
public sealed class IdentityPasswordHashing(IPasswordHasher<User> hasher) : IPasswordHashing
{
    // The Identity hasher ignores the user instance; it only exists to satisfy the signature.
    private static readonly User Placeholder = new() { ExternalSubject = "", DisplayName = "" };

    public string Hash(string password) => hasher.HashPassword(Placeholder, password);

    public bool Verify(string hash, string password) => hasher.VerifyHashedPassword(Placeholder, hash, password) != PasswordVerificationResult.Failed;
}
