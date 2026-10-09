using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(request => request.AddressId).NotEqual(Guid.Empty);

        // A saved address supplies everything; otherwise the address and its point are all required.
        When(request => request.AddressId is null, () =>
        {
            RuleFor(request => request.DeliveryAddress)
                .NotEmpty()
                .MaximumLength(500);
            RuleFor(request => request.Latitude)
                .NotNull()
                .InclusiveBetween(-90, 90);
            RuleFor(request => request.Longitude)
                .NotNull()
                .InclusiveBetween(-180, 180);
        });
    }
}
