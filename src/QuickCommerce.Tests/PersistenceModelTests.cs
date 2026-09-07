using Microsoft.EntityFrameworkCore;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Model_contains_required_tables_and_inventory_concurrency_token()
    {
        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>()
            .UseSqlServer("Server=NCIT-08;Database=retail-mobile-app;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new QuickCommerceDbContext(options);

        Assert.NotNull(db.Model.FindEntityType(typeof(Category)));
        var organization = db.Model.FindEntityType(typeof(Organization));
        Assert.NotNull(organization);
        Assert.NotNull(organization!.FindNavigation(nameof(Organization.Stores)));
        Assert.NotNull(organization.FindNavigation(nameof(Organization.Users)));
        Assert.NotNull(db.Model.FindEntityType(typeof(User))!.FindNavigation(nameof(User.Organization)));
        Assert.NotNull(db.Model.FindEntityType(typeof(User))!.FindNavigation(nameof(User.Store)));
        Assert.NotNull(db.Model.FindEntityType(typeof(Customer)));
        Assert.NotNull(db.Model.FindEntityType(typeof(Cart)));
        Assert.NotNull(db.Model.FindEntityType(typeof(CartItem)));
        Assert.True(db.Model.FindEntityType(typeof(CartItem))!.FindPrimaryKey()!.Properties.Count == 2);
        Assert.NotNull(db.Model.FindEntityType(typeof(Product)));
        Assert.NotNull(db.Model.FindEntityType(typeof(Store)));
        Assert.NotNull(db.Model.FindEntityType(typeof(StoreInventory)));
        Assert.NotNull(db.Model.FindEntityType(typeof(Order)));
        Assert.NotNull(db.Model.FindEntityType(typeof(OrderItem)));
        Assert.NotNull(db.Model.FindEntityType(typeof(OrderStatusHistory)));
        Assert.True(db.Model.FindEntityType(typeof(StoreInventory))!.FindProperty(nameof(StoreInventory.RowVersion))!.IsConcurrencyToken);
        Assert.True(db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.RowVersion))!.IsConcurrencyToken);

        var script = db.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE [StoreInventory]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decimal(18,2)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [Organizations]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OrganizationId", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqlServer_persistence_smoke_test_runs_when_connection_is_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUICKCOMMERCE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new QuickCommerceDbContext(options);
        await db.Database.MigrateAsync();

        Assert.True(await db.Categories.AnyAsync());
        Assert.True(await db.Products.AnyAsync());
        Assert.True(await db.Stores.AnyAsync());
        Assert.True(await db.StoreInventory.AnyAsync());
    }
}
