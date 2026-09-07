using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Quantity).InclusiveBetween(1, 1000);
    }
}

public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator()
    {
        RuleFor(request => request.Quantity).InclusiveBetween(1, 1000);
    }
}