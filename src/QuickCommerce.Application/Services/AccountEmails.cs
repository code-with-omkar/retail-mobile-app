namespace QuickCommerce.Application.Services;

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Plain-text account emails in English or Marathi, chosen from the request's Accept-Language.</summary>
public static class AccountEmails
{
    public static EmailMessage PasswordReset(string to, string formattedCode, int validMinutes, string? language)
    {
        if (IsMarathi(language))
        {
            return new EmailMessage(
                to,
                "QuickCart पासवर्ड रीसेट कोड",
                $"तुमचा QuickCart पासवर्ड रीसेट कोड:\n\n    {formattedCode}\n\nहा कोड {validMinutes} मिनिटांसाठी वैध आहे आणि फक्त एकदाच वापरता येतो.\nतुम्ही हे मागितले नसेल, तर या ईमेलकडे दुर्लक्ष करा; तुमचा पासवर्ड बदलणार नाही.\nहा कोड कोणालाही सांगू नका.");
        }

        return new EmailMessage(
            to,
            "Your QuickCart password reset code",
            $"Your QuickCart password reset code:\n\n    {formattedCode}\n\nIt is valid for {validMinutes} minutes and can be used once.\nIf you did not ask for this, ignore this email; your password will not change.\nNever share this code with anyone.");
    }

    private static bool IsMarathi(string? language) => language?.TrimStart().StartsWith("mr", StringComparison.OrdinalIgnoreCase) == true;
}
