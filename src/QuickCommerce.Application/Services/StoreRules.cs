using System.Text.RegularExpressions;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Application.Services;

public static partial class StoreRules
{
    public const double MinRadiusKm = 0.5;
    public const double MaxRadiusKm = 50;

    [GeneratedRegex("^[A-Z0-9]{2,8}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^\\+?[0-9][0-9 -]{6,18}$")]
    private static partial Regex PhonePattern();

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Why the store cannot be saved; null when it can.</summary>
    public static string? Problem(SaveStoreRequest request)
    {
        if (!Text(request.Name, 160))
        {
            return "Enter the store's name (up to 160 characters, without < or >).";
        }

        if (!Text(request.Address, 500))
        {
            return "Enter the address (up to 500 characters, without < or >).";
        }

        if (!double.IsFinite(request.Latitude) || !double.IsFinite(request.Longitude) || Math.Abs(request.Latitude) > 90 || Math.Abs(request.Longitude) > 180 || (request.Latitude == 0 && request.Longitude == 0))
        {
            return "Enter where the store is: latitude from -90 to 90 and longitude from -180 to 180.";
        }

        if (!double.IsFinite(request.ServiceRadiusKm) || request.ServiceRadiusKm < MinRadiusKm || request.ServiceRadiusKm > MaxRadiusKm)
        {
            return "The delivery radius is from 0.5 to 50 km.";
        }

        if (!CodePattern().IsMatch(NormalizeCode(request.Code)))
        {
            return "The code is 2 to 8 letters or digits, such as KHG.";
        }

        return string.IsNullOrWhiteSpace(request.PhoneNumber) || PhonePattern().IsMatch(request.PhoneNumber.Trim()) ? null : "Enter a phone number with digits only (spaces and - are fine).";
    }

    private static bool Text(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max && !value.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n'));
}
