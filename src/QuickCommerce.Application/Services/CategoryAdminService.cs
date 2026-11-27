using QuickCommerce.Application.Caching;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>Rules for the category tree: two levels at most, no loops, and a name is used once among its siblings.</summary>
public static class CategoryRules
{
    public const int MaxNameLength = 60;

    /// <summary>Null when the change is fine, otherwise what is wrong. [id] is null for a new category.</summary>
    public static (CatalogAdminStatus Status, string Message)? Check(IReadOnlyList<Category> all, Guid? id, string name, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return (CatalogAdminStatus.Invalid, $"Name is required and at most {MaxNameLength} characters.");
        }

        if (parentId is { } parent)
        {
            if (parent == id)
            {
                return (CatalogAdminStatus.Invalid, "A category cannot be its own parent.");
            }

            var parentCategory = all.FirstOrDefault(category => category.Id == parent);
            if (parentCategory is null)
            {
                return (CatalogAdminStatus.Invalid, "Parent category not found.");
            }

            if (parentCategory.ParentCategoryId is not null)
            {
                return (CatalogAdminStatus.Invalid, "Choose a top-level category as the parent; sub-categories go one level deep.");
            }

            if (id is { } own && all.Any(category => category.ParentCategoryId == own))
            {
                return (CatalogAdminStatus.Invalid, "This category has sub-categories, so it cannot become one itself.");
            }
        }

        var clash = all.Any(category => category.Id != id && category.ParentCategoryId == parentId && string.Equals(category.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return clash ? (CatalogAdminStatus.Conflict, "A category with this name already exists here.") : null;
    }
}

public sealed class CategoryAdminService(ICategoryAdminStore store, ICurrentUserContextResolver contextResolver, ICacheService? cache = null) : ICategoryAdminService
{
    private async Task<bool> IsAdminAsync(CancellationToken cancellationToken) =>
        await contextResolver.ResolveAsync(cancellationToken) is { Role: Role.Admin or Role.ApplicationAdmin };

    private static AdminCategoryResponse Map(Category category, int count) => new(category.Id, category.Name, category.ParentCategoryId, category.IsActive, count);

    public async Task<CatalogAdminResult<IReadOnlyList<AdminCategoryResponse>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return CatalogAdminResult<IReadOnlyList<AdminCategoryResponse>>.Fail(CatalogAdminStatus.Forbidden, "Only an administrator can change the catalogue.");
        }

        var rows = await store.ListAsync(cancellationToken);
        return CatalogAdminResult<IReadOnlyList<AdminCategoryResponse>>.Ok(rows.OrderBy(row => row.Category.Name, StringComparer.OrdinalIgnoreCase).Select(row => Map(row.Category, row.ProductCount)).ToArray());
    }

    public async Task<CatalogAdminResult<AdminCategoryResponse>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return CatalogAdminResult<AdminCategoryResponse>.Fail(CatalogAdminStatus.Forbidden, "Only an administrator can change the catalogue.");
        }

        var all = (await store.ListAsync(cancellationToken)).Select(row => row.Category).ToArray();
        if (CategoryRules.Check(all, null, request.Name ?? string.Empty, request.ParentCategoryId) is { } problem)
        {
            return CatalogAdminResult<AdminCategoryResponse>.Fail(problem.Status, problem.Message);
        }

        var category = new Category { Name = request.Name!.Trim(), ParentCategoryId = request.ParentCategoryId, IsActive = request.IsActive };
        await store.AddAsync(category, cancellationToken);
        await ForgetAsync(cancellationToken);
        return CatalogAdminResult<AdminCategoryResponse>.Ok(Map(category, 0));
    }

    public async Task<CatalogAdminResult<AdminCategoryResponse>> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
        {
            return CatalogAdminResult<AdminCategoryResponse>.Fail(CatalogAdminStatus.Forbidden, "Only an administrator can change the catalogue.");
        }

        var rows = await store.ListAsync(cancellationToken);
        if (rows.All(row => row.Category.Id != id))
        {
            return CatalogAdminResult<AdminCategoryResponse>.Fail(CatalogAdminStatus.NotFound, "Category not found.");
        }

        if (CategoryRules.Check(rows.Select(row => row.Category).ToArray(), id, request.Name ?? string.Empty, request.ParentCategoryId) is { } problem)
        {
            return CatalogAdminResult<AdminCategoryResponse>.Fail(problem.Status, problem.Message);
        }

        var name = request.Name!.Trim();
        if (!await store.UpdateAsync(id, name, request.ParentCategoryId, request.IsActive, cancellationToken))
        {
            return CatalogAdminResult<AdminCategoryResponse>.Fail(CatalogAdminStatus.NotFound, "Category not found.");
        }

        await ForgetAsync(cancellationToken);
        var count = rows.First(row => row.Category.Id == id).ProductCount;
        return CatalogAdminResult<AdminCategoryResponse>.Ok(new AdminCategoryResponse(id, name, request.ParentCategoryId, request.IsActive, count));
    }

    private async Task ForgetAsync(CancellationToken cancellationToken)
    {
        if (cache is not null)
        {
            await cache.RemoveAsync(CatalogCacheKeys.Categories, cancellationToken);
            await cache.RemoveAsync(CatalogCacheKeys.CatalogCategories, cancellationToken);
        }
    }
}
