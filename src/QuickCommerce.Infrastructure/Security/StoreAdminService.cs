using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;

namespace QuickCommerce.Infrastructure.Security;

public sealed class StoreAdminService(QuickCommerceDbContext db, IAuthorizationScopeService scopeService) : IStoreAdminService
{
    private const string Taken = "Another store already has that name or code.";

    public async Task<StoreAdminResult> CreateAsync(SaveStoreRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(StoreAdminStatus.Forbidden, Message: "Only an administrator can add a store.");
        }

        if (StoreRules.Problem(request) is { } problem)
        {
            return new(StoreAdminStatus.Invalid, Message: problem);
        }

        var store = new Store { OrganizationId = scope.OrganizationId, Name = request.Name.Trim(), Address = request.Address.Trim() };
        Fill(store, request);
        if (await ClashAsync(store, cancellationToken))
        {
            return new(StoreAdminStatus.Conflict, Message: Taken);
        }

        db.Stores.Add(store);
        return await SaveAsync(store, cancellationToken);
    }

    public async Task<StoreAdminResult> UpdateAsync(Guid id, SaveStoreRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await scopeService.ResolveAsync(cancellationToken);
        if (scope?.IsApplicationAdmin != true)
        {
            return new(StoreAdminStatus.Forbidden, Message: "Only an administrator can change a store.");
        }

        if (StoreRules.Problem(request) is { } problem)
        {
            return new(StoreAdminStatus.Invalid, Message: problem);
        }

        var store = await db.Stores.SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == scope.OrganizationId, cancellationToken);
        if (store is null)
        {
            return new(StoreAdminStatus.NotFound, Message: "Store not found.");
        }

        store.Name = request.Name.Trim();
        store.Address = request.Address.Trim();
        Fill(store, request);
        return await ClashAsync(store, cancellationToken) ? new(StoreAdminStatus.Conflict, Message: Taken) : await SaveAsync(store, cancellationToken);
    }

    private static void Fill(Store store, SaveStoreRequest request)
    {
        store.Latitude = Math.Round(request.Latitude, 6);
        store.Longitude = Math.Round(request.Longitude, 6);
        store.ServiceRadiusKm = Math.Round(request.ServiceRadiusKm, 2);
        store.Code = StoreRules.NormalizeCode(request.Code);
        store.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        store.IsActive = request.IsActive;
    }

    private Task<bool> ClashAsync(Store store, CancellationToken cancellationToken) =>
        db.Stores.AsNoTracking().AnyAsync(other => other.Id != store.Id && (other.Code == store.Code || (other.OrganizationId == store.OrganizationId && other.Name == store.Name)), cancellationToken);

    /// <summary>The unique indexes are the judge when two administrators save the same name or code at once.</summary>
    private async Task<StoreAdminResult> SaveAsync(Store store, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return new(StoreAdminStatus.Conflict, Message: Taken);
        }

        return new(StoreAdminStatus.Succeeded, new StoreResponse(store.Id, store.Name, store.Address, store.Latitude, store.Longitude, store.ServiceRadiusKm, store.IsActive, store.Code, store.PhoneNumber));
    }
}
