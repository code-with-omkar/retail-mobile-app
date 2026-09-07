using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(request => request.DeliveryAddress)
            .NotEmpty()
            .MaximumLength(500);
        RuleFor(request => request.Latitude)
            .InclusiveBetween(-90, 90);
        RuleFor(request => request.Longitude)
            .InclusiveBetween(-180, 180);
    }
}