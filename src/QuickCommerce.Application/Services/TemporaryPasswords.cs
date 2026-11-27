using System.Security.Cryptography;

namespace QuickCommerce.Application.Services;

/// <summary>Passwords an administrator reads out to a new member of staff: random, and without letters that look like one another.</summary>
public static class TemporaryPasswords
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    public const int Length = 14;

    public static string Create()
    {
        // At least one of each kind, the rest from all of them, in a random order.
        var letters = new List<char> { Pick(Upper), Pick(Lower), Pick(Digits), '-' };
        while (letters.Count < Length)
        {
            letters.Add(Pick(Upper + Lower + Digits));
        }

        var result = letters.ToArray();
        RandomNumberGenerator.Shuffle(result.AsSpan());
        return new string(result);
    }

    private static char Pick(string from) => from[RandomNumberGenerator.GetInt32(from.Length)];
}
