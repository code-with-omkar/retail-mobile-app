using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class CustomerAddressRequestValidator : AbstractValidator<CustomerAddressRequest>
{
    public CustomerAddressRequestValidator()
    {
        RuleFor(request => request.Label).NotEmpty().WithMessage("Give the address a label such as Home or Work.").MaximumLength(40).WithMessage("The label can have at most 40 characters.");
        RuleFor(request => request.Line).NotEmpty().WithMessage("The address is required.").MaximumLength(300).WithMessage("The address can have at most 300 characters.");
        RuleFor(request => request.FlatOrBuilding).MaximumLength(120).WithMessage("Flat or building can have at most 120 characters.");
        RuleFor(request => request.Landmark).MaximumLength(120).WithMessage("The landmark can have at most 120 characters.");
        RuleFor(request => request.Latitude).InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(request => request.Longitude).InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");
        // 0,0 is what an unset pin looks like; an address there is a mistake, not a place.
        RuleFor(request => request).Must(request => !(request.Latitude == 0 && request.Longitude == 0)).WithMessage("Pick the location on the map.");
        RuleFor(request => request.ReceiverName).NotEmpty().WithMessage("Who will receive the delivery?").MaximumLength(120).WithMessage("The receiver name can have at most 120 characters.");
        RuleFor(request => request.ReceiverPhone).Must(phone => AccountRules.IsValidPhone(AccountRules.NormalizePhone(phone))).WithMessage("Enter a phone number with 10 to 15 digits.");
    }
}
