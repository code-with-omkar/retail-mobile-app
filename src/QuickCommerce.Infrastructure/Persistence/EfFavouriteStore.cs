using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

/// <summary>Saved products on SQL Server. The key is (customer, product), so saving twice at once can only ever make one row.</summary>
public sealed class EfFavouriteStore(QuickCommerceDbContext db) : IFavouriteStore
{
    public async Task<IReadOnlyList<Guid>> ListAsync(Guid customerId, int take, CancellationToken cancellationToken = default) =>
        await db.Favourites.AsNoTracking().Where(item => item.CustomerId == customerId).OrderByDescending(item => item.CreatedAt).ThenBy(item => item.ProductId).Take(take).Select(item => item.ProductId).ToListAsync(cancellationToken);

    public Task<int> CountAsync(Guid customerId, CancellationToken cancellationToken = default) => db.Favourites.CountAsync(item => item.CustomerId == customerId, cancellationToken);

    public async Task<bool> AddAsync(Guid customerId, Guid productId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (await db.Favourites.AnyAsync(item => item.CustomerId == customerId && item.ProductId == productId, cancellationToken))
        {
            return false;
        }

        db.Favourites.Add(new FavouriteProduct { CustomerId = customerId, ProductId = productId, CreatedAt = nowUtc });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> RemoveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken = default) =>
        await db.Favourites.Where(item => item.CustomerId == customerId && item.ProductId == productId).ExecuteDeleteAsync(cancellationToken) > 0;
}
