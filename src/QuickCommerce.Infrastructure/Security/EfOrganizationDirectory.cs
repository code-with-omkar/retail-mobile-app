using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class EfOrganizationDirectory(QuickCommerceDbContext db) : IOrganizationDirectory
{
    public async Task<IReadOnlyList<OrganizationInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Organizations.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name)
            .Select(item => new OrganizationInfo(item.Id, item.Name, item.Code)).ToListAsync(cancellationToken);
}
