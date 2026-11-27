using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

public sealed record AdminCategoryResponse(Guid Id, string Name, Guid? ParentCategoryId, bool IsActive, int ProductCount);

/// <summary>A category is top level when it has no parent; otherwise it is a sub-category of that top-level one.</summary>
public sealed record SaveCategoryRequest(string Name, Guid? ParentCategoryId = null, bool IsActive = true);

public interface ICategoryAdminService
{
    Task<CatalogAdminResult<IReadOnlyList<AdminCategoryResponse>>> ListAsync(CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminCategoryResponse>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default);
    Task<CatalogAdminResult<AdminCategoryResponse>> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken = default);
}

public interface ICategoryAdminStore
{
    /// <summary>Every category, active or not, with how many products are directly in it.</summary>
    Task<IReadOnlyList<(Category Category, int ProductCount)>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>Saves the new name, parent and active flag of an existing category. False when it no longer exists.</summary>
    Task<bool> UpdateAsync(Guid id, string name, Guid? parentCategoryId, bool isActive, CancellationToken cancellationToken = default);
}
