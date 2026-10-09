using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Saved addresses against a real SQL Server: the limit and the one-default rule under parallel requests, promotion on delete, ownership
/// scoping and the table rules. Opt-in, like the other SQL tests: set QUICKCOMMERCE_TEST_CONNECTION_STRING to a connection string for a
/// DISPOSABLE database. Everything it adds is uniquely named and removed again. Never point it at a database you care about.
/// </summary>
public sealed class AddressEfIntegrationTests
{
    private static CustomerAddress New(string label, double latitude = 19.0, double longitude = 72.8) => new()
    {
        Label = label,
        Line = "12 Test Street",
        Latitude = latitude,
        Longitude = longitude,
        ReceiverName = "Test Receiver",
        ReceiverPhone = "9876543210"
    };

    [Fact]
    public async Task Addresses_work_against_a_real_sql_server()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUICKCOMMERCE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer(connectionString).Options;
        await using (var migrate = new QuickCommerceDbContext(options))
        {
            await migrate.Database.MigrateAsync();
        }

        var token = "ad" + Guid.NewGuid().ToString("N")[..10];
        var emails = new[] { $"{token}-a@example.test", $"{token}-b@example.test", $"{token}-c@example.test" };
        Guid organizationId;
        await using (var lookup = new QuickCommerceDbContext(options))
        {
            organizationId = (await lookup.Organizations.AsNoTracking().FirstAsync()).Id;
        }

        foreach (var email in emails)
        {
            await using var db = new QuickCommerceDbContext(options);
            Assert.True(await new EfAccountStore(db).TryCreateCustomerAsync(new NewCustomerAccount(organizationId, "Address Test", "Address", "Test", email, null, "x")));
        }

        var userIds = new List<Guid>();
        var customerIds = new List<Guid>();
        await using (var db = new QuickCommerceDbContext(options))
        {
            foreach (var email in emails)
            {
                var userId = await db.Users.Where(user => user.ExternalSubject == email).Select(user => user.Id).SingleAsync();
                userIds.Add(userId);
                customerIds.Add(await db.Customers.Where(customer => customer.UserId == userId).Select(customer => customer.Id).SingleAsync());
            }
        }

        var (asha, bhavesh, parallel) = (customerIds[0], customerIds[1], customerIds[2]);

        async Task<T> WithStore<T>(Func<EfAddressStore, Task<T>> work)
        {
            await using var db = new QuickCommerceDbContext(options);
            return await work(new EfAddressStore(db));
        }

        async Task<List<CustomerAddress>> Stored(Guid customerId)
        {
            await using var db = new QuickCommerceDbContext(options);
            return await db.CustomerAddresses.AsNoTracking().Where(address => address.CustomerId == customerId).ToListAsync();
        }

        try
        {
            // ----- who is a customer -----
            var owner = await WithStore(store => store.FindOwnerAsync(userIds[0]));
            Assert.Equal((asha, organizationId), (owner!.CustomerId, owner.OrganizationId));
            Assert.Null(await WithStore(store => store.FindOwnerAsync(Guid.NewGuid())));

            // ----- first address is the default, the next is not; listing puts the default first -----
            var home = (await WithStore(store => store.AddAsync(asha, New("Home"), 10))).Address!;
            var work = (await WithStore(store => store.AddAsync(asha, New("Work"), 10))).Address!;
            var gym = (await WithStore(store => store.AddAsync(asha, New("Gym"), 10))).Address!;
            Assert.Equal((true, false, false), (home.IsDefault, work.IsDefault, gym.IsDefault));
            var listed = await WithStore(store => store.ListAsync(asha));
            Assert.Equal("Home", listed[0].Label);
            Assert.Equal(3, listed.Count);

            // ----- ownership: another customer's id is simply not there -----
            var fields = new AddressFields("Hijack", "x", "", "", 19, 72, "x", "9876543210");
            Assert.Null(await WithStore(store => store.GetAsync(bhavesh, home.Id)));
            Assert.Null(await WithStore(store => store.UpdateAsync(bhavesh, home.Id, fields, DateTime.UtcNow)));
            Assert.False(await WithStore(store => store.SetDefaultAsync(bhavesh, work.Id, DateTime.UtcNow)));
            Assert.False(await WithStore(store => store.DeleteAsync(bhavesh, home.Id, DateTime.UtcNow)));
            Assert.Equal(["Gym", "Home", "Work"], (await Stored(asha)).Select(address => address.Label).Order().ToArray());
            Assert.Empty(await Stored(bhavesh));

            // ----- update keeps the id and the default flag -----
            var updated = (await WithStore(store => store.UpdateAsync(asha, work.Id, fields with { Label = "Office", Line = "5 Hill Road" }, DateTime.UtcNow)))!;
            Assert.Equal((work.Id, "Office", "5 Hill Road", false), (updated.Id, updated.Label, updated.Line, updated.IsDefault));

            // ----- parallel default changes always end with exactly one default, and none of them fail -----
            var targets = new[] { home.Id, work.Id, gym.Id };
            var changes = Enumerable.Range(0, 12).Select(index => WithStore(store => store.SetDefaultAsync(asha, targets[index % 3], DateTime.UtcNow))).ToArray();
            Assert.All(await Task.WhenAll(changes), succeeded => Assert.True(succeeded));
            Assert.Single((await Stored(asha)).Where(address => address.IsDefault));

            // ----- deleting the default promotes the most recently changed remaining address -----
            await WithStore(store => store.SetDefaultAsync(asha, home.Id, DateTime.UtcNow));
            await WithStore(store => store.UpdateAsync(asha, gym.Id, fields with { Label = "Gym" }, DateTime.UtcNow.AddHours(1)));
            Assert.True(await WithStore(store => store.DeleteAsync(asha, home.Id, DateTime.UtcNow.AddHours(2))));
            var afterDelete = await Stored(asha);
            Assert.Equal(2, afterDelete.Count);
            Assert.Equal("Gym", afterDelete.Single(address => address.IsDefault).Label);
            Assert.False(await WithStore(store => store.DeleteAsync(asha, home.Id, DateTime.UtcNow)), "deleting twice");

            // ----- the limit and the single default hold under eight parallel creates from one customer -----
            var racers = Enumerable.Range(0, 8).Select(index => WithStore(store => store.AddAsync(parallel, New($"Racer {index}"), 3))).ToArray();
            var results = await Task.WhenAll(racers);
            Assert.Equal(3, results.Count(result => !result.LimitReached));
            Assert.Equal(5, results.Count(result => result.LimitReached));
            var raced = await Stored(parallel);
            Assert.Equal(3, raced.Count);
            Assert.Single(raced, address => address.IsDefault);

            // ----- the table refuses a second default and impossible coordinates, whatever the code does -----
            await using (var duplicate = new QuickCommerceDbContext(options))
            {
                var extra = New("Extra");
                extra.CustomerId = asha;
                extra.IsDefault = true;
                duplicate.CustomerAddresses.Add(extra);
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            }

            await using (var impossible = new QuickCommerceDbContext(options))
            {
                var bad = New("Bad", latitude: 123);
                bad.CustomerId = bhavesh;
                impossible.CustomerAddresses.Add(bad);
                await Assert.ThrowsAsync<DbUpdateException>(() => impossible.SaveChangesAsync());
            }

            // ----- a customer's addresses go with the customer -----
            await using (var remove = new QuickCommerceDbContext(options))
            {
                await remove.Customers.Where(customer => customer.Id == parallel).ExecuteDeleteAsync();
            }

            Assert.Empty(await Stored(parallel));
        }
        finally
        {
            await using var cleanup = new QuickCommerceDbContext(options);
            await cleanup.CustomerAddresses.Where(address => customerIds.Contains(address.CustomerId)).ExecuteDeleteAsync();
            await cleanup.PasswordResetCodes.Where(code => userIds.Contains(code.UserId)).ExecuteDeleteAsync();
            await cleanup.RefreshTokens.Where(refresh => userIds.Contains(refresh.UserId)).ExecuteDeleteAsync();
            await cleanup.UserRoles.Where(role => userIds.Contains(role.UserId)).ExecuteDeleteAsync();
            await cleanup.UserCredentials.Where(credential => userIds.Contains(credential.UserId)).ExecuteDeleteAsync();
            await cleanup.Customers.Where(customer => userIds.Contains(customer.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
        }
    }
}
