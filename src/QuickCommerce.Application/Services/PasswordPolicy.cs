namespace QuickCommerce.Application.Services;

/// <summary>
/// Server-side password rules: 8 to 128 characters, not the email address, not a very common password.
/// No composition rules (uppercase/digit/symbol), which push people toward predictable passwords.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "p@ssw0rd", "12345678", "123456789", "1234567890", "11111111",
        "00000000", "87654321", "qwerty123", "qwertyui", "qwertyuiop", "1q2w3e4r", "1qaz2wsx", "abc12345", "abcd1234", "iloveyou",
        "letmein1", "welcome1", "welcome123", "admin123", "administrator", "changeme", "changeme123", "monkey123", "dragon123", "football1",
        "baseball1", "superman1", "trustno1", "sunshine1", "princess1", "whatever1", "freedom1", "shadow123", "master123", "login123",
        "india123", "india@123", "mumbai123", "pune1234", "quickcart", "quickcart1", "quickcart123", "grocery123", "shopping1", "test1234"
    };

    /// <summary>Returns null when the password is acceptable, otherwise a message safe to show to the user.</summary>
    public static string? Check(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Password must be at least {MinLength} characters.";
        }

        if (password.Length > MaxLength)
        {
            return $"Password must be at most {MaxLength} characters.";
        }

        if (!string.IsNullOrWhiteSpace(email) && string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Password must not be your email address.";
        }

        if (Common.Contains(password.Trim()))
        {
            return "That password is too common. Choose something harder to guess.";
        }

        return null;
    }
}
