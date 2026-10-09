using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class NearestStoreQueryValidator : AbstractValidator<NearestStoreQuery>
{
    public NearestStoreQueryValidator()
    {
        RuleFor(query => query.Latitude).InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(query => query.Longitude).InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");
    }
}
