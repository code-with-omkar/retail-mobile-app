using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class EfCategoryAdminStore(QuickCommerceDbContext db) : ICategoryAdminStore
{
    public async Task<IReadOnlyList<(Category Category, int ProductCount)>> ListAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await db.Products.AsNoTracking().GroupBy(product => product.CategoryId).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        return categories.Select(category => (category, counts.GetValueOrDefault(category.Id))).ToArray();
    }

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Guid id, string name, Guid? parentCategoryId, bool isActive, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
        {
            return false;
        }

        category.Name = name;
        category.ParentCategoryId = parentCategoryId;
        category.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
