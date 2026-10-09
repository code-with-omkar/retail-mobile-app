using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Infrastructure.Persistence;

public sealed class EfAddressStore(QuickCommerceDbContext db) : IAddressStore
{
    private const int Attempts = 4;

    public Task<AddressOwner?> FindOwnerAsync(Guid userId, CancellationToken cancellationToken = default) => db.Customers
        .AsNoTracking()
        .Where(customer => customer.UserId == userId && customer.IsActive && customer.User.IsActive)
        .Select(customer => new AddressOwner(customer.Id, customer.User.OrganizationId))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerAddress>> ListAsync(Guid customerId, CancellationToken cancellationToken = default) => await db.CustomerAddresses
        .AsNoTracking()
        .Where(address => address.CustomerId == customerId)
        .OrderByDescending(address => address.IsDefault)
        .ThenByDescending(address => address.UpdatedAt)
        .ToListAsync(cancellationToken);

    public Task<CustomerAddress?> GetAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken = default) => db.CustomerAddresses
        .AsNoTracking()
        .FirstOrDefaultAsync(address => address.Id == addressId && address.CustomerId == customerId, cancellationToken);

    public Task<AddAddressResult> AddAsync(Guid customerId, CustomerAddress address, int maxPerCustomer, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        // Changes to one customer's addresses take turns, so two requests at the limit cannot both pass the count and two "first" addresses cannot both become the default.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockCustomerAsync(customerId, cancellationToken);
        var count = await db.CustomerAddresses.CountAsync(item => item.CustomerId == customerId, cancellationToken);
        if (count >= maxPerCustomer)
        {
            return new AddAddressResult(null, true);
        }

        address.CustomerId = customerId;
        address.IsDefault = count == 0;
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AddAddressResult(address, false);
    });

    public async Task<CustomerAddress?> UpdateAsync(Guid customerId, Guid addressId, AddressFields fields, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(item => item.Id == addressId && item.CustomerId == customerId, cancellationToken);
        if (address is null)
        {
            return null;
        }

        address.Label = fields.Label;
        address.Line = fields.Line;
        address.FlatOrBuilding = fields.FlatOrBuilding;
        address.Landmark = fields.Landmark;
        address.Latitude = fields.Latitude;
        address.Longitude = fields.Longitude;
        address.ReceiverName = fields.ReceiverName;
        address.ReceiverPhone = fields.ReceiverPhone;
        address.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return address;
    }

    public Task<bool> SetDefaultAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockCustomerAsync(customerId, cancellationToken);
        if (!await db.CustomerAddresses.AnyAsync(item => item.Id == addressId && item.CustomerId == customerId, cancellationToken))
        {
            return false;
        }

        // Clear first, then set: at no point are there two defaults. A parallel change that interleaves hits the unique index and is retried.
        await db.CustomerAddresses.Where(item => item.CustomerId == customerId && item.IsDefault && item.Id != addressId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsDefault, false).SetProperty(item => item.UpdatedAt, nowUtc), cancellationToken);
        await db.CustomerAddresses.Where(item => item.Id == addressId && item.CustomerId == customerId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsDefault, true).SetProperty(item => item.UpdatedAt, nowUtc), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    });

    public Task<bool> DeleteAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default) => RetryAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockCustomerAsync(customerId, cancellationToken);
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(item => item.Id == addressId && item.CustomerId == customerId, cancellationToken);
        if (address is null)
        {
            return false;
        }

        var wasDefault = address.IsDefault;
        db.CustomerAddresses.Remove(address);
        await db.SaveChangesAsync(cancellationToken);

        if (wasDefault)
        {
            var next = await db.CustomerAddresses.Where(item => item.CustomerId == customerId).OrderByDescending(item => item.UpdatedAt).FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsDefault = true;
                next.UpdatedAt = nowUtc;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    });

    // An application lock named after the customer, held until the transaction ends: changes to one customer's addresses run one after
    // another instead of fighting over the same rows (which ends in deadlocks and index violations). Other customers are not affected.
    private Task LockCustomerAsync(Guid customerId, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @result int;
EXEC @result = sp_getapplock @Resource = {"addresses:" + customerId}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @result < 0 THROW 50300, 'Could not lock the customer addresses.', 1;", cancellationToken);

    // Anything that still slips through (a deadlock with some other statement, or the one-default index) means "someone else got there first":
    // start again on fresh data. Anything else is a real error.
    private async Task<T> RetryAsync<T>(Func<Task<T>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (Exception exception) when (attempt < Attempts && IsRetryable(exception))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsRetryable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
