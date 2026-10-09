using System.Security.Cryptography;
using System.Text;

namespace QuickCommerce.Application.Services;

/// <summary>
/// One-time password reset codes: 8 characters from an unambiguous 31-symbol alphabet (about 40 bits), shown as ABCD-EFGH.
/// Only an HMAC-SHA256 (keyed with a server secret and bound to the user) is stored. A plain hash of a 40-bit code could be
/// brute-forced if the table ever leaked; the key makes that useless without the server secret.
/// </summary>
public static class ResetCodes
{
    // No I, L, O, 0 or 1: they look alike when typed from an email.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 8;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Uppercase and strip dashes and spaces so "abcd-efgh" and "ABCDEFGH" are the same code.</summary>
    public static string Normalize(string? code) =>
        new((code ?? "").Where(c => c != '-' && !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());

    public static bool IsWellFormed(string normalized) => normalized.Length == Length && normalized.All(Alphabet.Contains);

    public static string Format(string code) => code.Length == Length ? $"{code[..4]}-{code[4..]}" : code;

    public static string Hash(string key, Guid userId, string normalizedCode)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId:N}:{normalizedCode}")));
    }

    /// <summary>Constant-time comparison of two hex hashes.</summary>
    public static bool Matches(string expectedHash, string actualHash) =>
        expectedHash.Length == actualHash.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(actualHash));
}
