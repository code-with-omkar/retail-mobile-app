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
            .UseSqlServer("Server=(local);Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;") // model inspection only; this test never opens a connection
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
        Assert.NotNull(db.Model.FindEntityType(typeof(AuthorizationRole)));
        Assert.NotNull(db.Model.FindEntityType(typeof(AuthorizationPermission)));
        Assert.NotNull(db.Model.FindEntityType(typeof(RolePermission)));
        var userRole = db.Model.FindEntityType(typeof(UserRole));
        Assert.NotNull(userRole);
        Assert.Equal(2, userRole!.FindPrimaryKey()!.Properties.Count);
        Assert.NotNull(userRole.FindNavigation(nameof(UserRole.User)));
        Assert.NotNull(userRole.FindNavigation(nameof(UserRole.Role)));
        var assignment = db.Model.FindEntityType(typeof(UserStoreAssignment));
        Assert.NotNull(assignment);
        Assert.Equal(2, assignment!.FindPrimaryKey()!.Properties.Count);
        Assert.NotNull(assignment.FindNavigation(nameof(UserStoreAssignment.User)));
        Assert.NotNull(assignment.FindNavigation(nameof(UserStoreAssignment.Store)));
        Assert.True(db.Model.FindEntityType(typeof(StoreInventory))!.FindProperty(nameof(StoreInventory.RowVersion))!.IsConcurrencyToken);
        Assert.True(db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.RowVersion))!.IsConcurrencyToken);

        var script = db.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE [StoreInventory]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decimal(18,2)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [Organizations]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [AuthorizationRoles]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [AuthorizationPermissions]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [RolePermissions]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [UserRoles]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [UserStoreAssignments]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationAdmin", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeliveryPartner", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multistorestaff", script, StringComparison.OrdinalIgnoreCase);
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
