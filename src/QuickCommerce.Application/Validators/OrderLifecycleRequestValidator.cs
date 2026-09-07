using FluentValidation;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Validators;

public sealed class OrderLifecycleRequestValidator : AbstractValidator<ChangeOrderStatusRequest>
{
    public OrderLifecycleRequestValidator()
    {
        RuleFor(request => request.Status)
            .Must(status => status is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Completed or OrderStatus.Rejected)
            .WithMessage("Status is not a supported store operation");
    }
}