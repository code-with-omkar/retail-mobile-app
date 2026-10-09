using FluentValidation;
using QuickCommerce.Application.DTOs;

namespace QuickCommerce.Application.Validators;

public sealed class CatalogProductQueryValidator : AbstractValidator<CatalogProductQuery>
{
    public CatalogProductQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or greater.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, CatalogProductQuery.MaxPageSize).WithMessage($"PageSize must be between 1 and {CatalogProductQuery.MaxPageSize}.");
        RuleFor(query => query.Search).MaximumLength(CatalogProductQuery.MaxSearchLength).WithMessage($"Search must be at most {CatalogProductQuery.MaxSearchLength} characters.");
        RuleFor(query => query.CategoryId).NotEqual(Guid.Empty).When(query => query.CategoryId.HasValue).WithMessage("CategoryId is not valid.");
        RuleFor(query => query.StoreId).NotEqual(Guid.Empty).When(query => query.StoreId.HasValue).WithMessage("StoreId is not valid.");
        RuleFor(query => query.StoreId).NotNull().When(query => query.CarriedOnly).WithMessage("CarriedOnly needs a storeId.");
    }
}
