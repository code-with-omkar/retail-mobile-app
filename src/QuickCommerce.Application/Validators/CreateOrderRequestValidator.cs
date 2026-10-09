using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(request => request.UserId)
            .NotEmpty();
        RuleFor(request => request.DeliveryAddress)
            .NotEmpty()
            .MaximumLength(500);
        RuleFor(request => request.Latitude)
            .InclusiveBetween(-90, 90);
        RuleFor(request => request.Longitude)
            .InclusiveBetween(-180, 180);
        RuleFor(request => request.Items)
            .NotEmpty();
        RuleForEach(request => request.Items)
            .ChildRules(item =>
            {
                item.RuleFor(line => line.ProductId).NotEmpty();
                item.RuleFor(line => line.VariantId).NotEqual(Guid.Empty).When(line => line.VariantId.HasValue).WithMessage("VariantId is not valid.");
                item.RuleFor(line => line.Quantity).GreaterThan(0);
            });
    }
}
