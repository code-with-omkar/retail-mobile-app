using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Application.Validators;

/// <summary>Shared field rules for the account requests.</summary>
public static class AccountRules
{
    public const int MaxEmailLength = 160;

    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Deliberately simple: one @, something on both sides, a dot in the domain, no spaces. The real check is the delivered email.</summary>
    public static bool IsValidEmail(string? email)
    {
        var value = NormalizeEmail(email);
        if (value.Length is 0 or > MaxEmailLength || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = value.IndexOf('@');
        if (at < 1 || at != value.LastIndexOf('@'))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        return domain.Length >= 3 && domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..");
    }

    /// <summary>Strips spaces, dashes and brackets. Returns null for an empty value; the result still needs <see cref="IsValidPhone"/>.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        return new string(phone.Where(c => c != ' ' && c != '-' && c != '(' && c != ')').ToArray());
    }

    public static bool IsValidPhone(string? normalized) =>
        normalized is null || System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\+?[0-9]{10,15}$");

    public static (string First, string Last) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ("", "") : (parts[0], string.Join(' ', parts.Skip(1)));
    }
}

public sealed class RegisterCustomerRequestValidator : AbstractValidator<RegisterCustomerRequest>
{
    public RegisterCustomerRequestValidator()
    {
        RuleFor(request => request.FullName).Must(name => (name ?? "").Trim().Length is >= 2 and <= 80).WithMessage("Enter your full name (2 to 80 characters).");
        RuleFor(request => request.Email).Must(AccountRules.IsValidEmail).WithMessage("Enter a valid email address.");
        RuleFor(request => request.Password).Custom((password, context) =>
        {
            if (PasswordPolicy.Check(password, context.InstanceToValidate.Email) is { } error)
            {
                context.AddFailure(nameof(RegisterCustomerRequest.Password), error);
            }
        });
        RuleFor(request => request.PhoneNumber).Must(phone => AccountRules.IsValidPhone(AccountRules.NormalizePhone(phone))).WithMessage("Enter a phone number with 10 to 15 digits.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(request => request.Email).Must(AccountRules.IsValidEmail).WithMessage("Enter a valid email address.");
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(request => request.Email).Must(AccountRules.IsValidEmail).WithMessage("Enter a valid email address.");
        RuleFor(request => request.Code).Must(code => ResetCodes.IsWellFormed(ResetCodes.Normalize(code))).WithMessage($"Enter the {ResetCodes.Length}-character code from the email.");
        RuleFor(request => request.NewPassword).Custom((password, context) =>
        {
            if (PasswordPolicy.Check(password, context.InstanceToValidate.Email) is { } error)
            {
                context.AddFailure(nameof(ResetPasswordRequest.NewPassword), error);
            }
        });
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword).NotEmpty().WithMessage("Enter your current password.");
        RuleFor(request => request.NewPassword).Custom((password, context) =>
        {
            if (PasswordPolicy.Check(password) is { } error)
            {
                context.AddFailure(nameof(ChangePasswordRequest.NewPassword), error);
            }
            else if (password == context.InstanceToValidate.CurrentPassword)
            {
                context.AddFailure(nameof(ChangePasswordRequest.NewPassword), "Choose a password different from the current one.");
            }
        });
    }
}

public sealed class UpdateCustomerProfileRequestValidator : AbstractValidator<UpdateCustomerProfileRequest>
{
    public UpdateCustomerProfileRequestValidator()
    {
        RuleFor(request => request.FullName).Must(name => (name ?? "").Trim().Length is >= 2 and <= 80).WithMessage("Enter your full name (2 to 80 characters).");
        RuleFor(request => request.PhoneNumber).Must(phone => AccountRules.IsValidPhone(AccountRules.NormalizePhone(phone))).WithMessage("Enter a phone number with 10 to 15 digits.");
    }
}
