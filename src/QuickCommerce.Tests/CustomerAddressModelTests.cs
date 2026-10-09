using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>The addresses table as the model describes it (no connection is opened) and the migration that creates it.</summary>
public sealed class CustomerAddressModelTests
{
    [Fact]
    public void Addresses_belong_to_a_customer_with_one_default_each_and_valid_coordinates()
    {
        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer("Server=(local);Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;").Options;
        using var db = new QuickCommerceDbContext(options);
        var address = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CustomerAddress))!;

        Assert.Equal("CustomerAddresses", address.GetTableName());
        Assert.Equal([nameof(CustomerAddress.Id)], address.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(Cascade(), address.GetForeignKeys().Single().DeleteBehavior);
        Assert.Equal(typeof(Customer), address.GetForeignKeys().Single().PrincipalEntityType.ClrType);

        var oneDefault = address.GetIndexes().Single(index => index.GetDatabaseName() == "UX_CustomerAddresses_OneDefaultPerCustomer");
        Assert.True(oneDefault.IsUnique);
        Assert.Equal("[IsDefault] = 1", oneDefault.GetFilter());
        Assert.Equal([nameof(CustomerAddress.CustomerId)], oneDefault.Properties.Select(property => property.Name).ToArray());

        Assert.Contains(address.GetCheckConstraints(), check => check.Name == "CK_CustomerAddresses_Latitude");
        Assert.Contains(address.GetCheckConstraints(), check => check.Name == "CK_CustomerAddresses_Longitude");

        Assert.Equal(40, address.FindProperty(nameof(CustomerAddress.Label))!.GetMaxLength());
        Assert.Equal(300, address.FindProperty(nameof(CustomerAddress.Line))!.GetMaxLength());
        Assert.Equal(120, address.FindProperty(nameof(CustomerAddress.FlatOrBuilding))!.GetMaxLength());
        Assert.Equal(120, address.FindProperty(nameof(CustomerAddress.Landmark))!.GetMaxLength());
        Assert.Equal(120, address.FindProperty(nameof(CustomerAddress.ReceiverName))!.GetMaxLength());
        Assert.Equal(20, address.FindProperty(nameof(CustomerAddress.ReceiverPhone))!.GetMaxLength());
        Assert.False(address.FindProperty(nameof(CustomerAddress.ReceiverPhone))!.IsNullable);

        static DeleteBehavior Cascade() => DeleteBehavior.Cascade;
    }

    [Fact]
    public void The_migration_only_adds_the_addresses_table_and_its_down_only_drops_it()
    {
        var source = MigrationSource("AddCustomerAddresses.cs");
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];
        var down = source[source.IndexOf("protected override void Down", StringComparison.Ordinal)..];

        Assert.Contains("CreateTable", up);
        Assert.Equal(1, Count(up, "CreateTable("));
        foreach (var forbidden in new[] { "DropTable", "DropColumn", "AlterColumn", "DropPrimaryKey", "DropForeignKey", "DELETE FROM", "UPDATE " })
        {
            Assert.DoesNotContain(forbidden, up);
        }

        Assert.Equal(1, Count(down, "DropTable("));
        Assert.Contains("\"CustomerAddresses\"", down);
    }

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    private static string MigrationSource(string suffix)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(ThisFile())!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickCommerce.slnx")))
        {
            directory = directory.Parent;
        }

        var migrations = Path.Combine(directory!.FullName, "src", "QuickCommerce.Infrastructure", "Persistence", "Migrations");
        return File.ReadAllText(Directory.GetFiles(migrations, "*_" + suffix).Single());
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
