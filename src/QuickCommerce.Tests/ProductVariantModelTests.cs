using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>The P3 schema as the model describes it (no database connection is opened) and the migration that creates it.</summary>
public sealed class ProductVariantModelTests
{
    private static IModel DesignTimeModel()
    {
        var options = new DbContextOptionsBuilder<QuickCommerceDbContext>().UseSqlServer("Server=(local);Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;").Options;
        var db = new QuickCommerceDbContext(options);
        return db.GetService<IDesignTimeModel>().Model; // check constraints are only kept in the design-time model
    }

    [Fact]
    public void Variants_have_their_own_price_and_mrp_one_default_per_product_and_a_unique_sku()
    {
        var variant = DesignTimeModel().FindEntityType(typeof(ProductVariant))!;

        Assert.Equal([nameof(ProductVariant.Id)], variant.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(18, variant.FindProperty(nameof(ProductVariant.Price))!.GetPrecision());
        Assert.True(variant.FindProperty(nameof(ProductVariant.Mrp))!.IsNullable);
        Assert.Equal(40, variant.FindProperty(nameof(ProductVariant.Label))!.GetMaxLength());
        Assert.Contains(variant.GetCheckConstraints(), check => check.Name == "CK_ProductVariants_MrpAtLeastPrice");
        Assert.Contains(variant.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(ProductVariant.Sku));
        var oneDefault = variant.GetIndexes().Single(index => index.GetDatabaseName() == "UX_ProductVariants_OneDefaultPerProduct");
        Assert.True(oneDefault.IsUnique);
        Assert.Equal("[IsDefault] = 1", oneDefault.GetFilter());
        Assert.Equal(DeleteBehavior.Restrict, variant.GetForeignKeys().Single().DeleteBehavior);
    }

    [Fact]
    public void Stock_is_per_store_and_variant_with_a_concurrency_token_and_no_negative_quantity()
    {
        var stock = DesignTimeModel().FindEntityType(typeof(StoreVariantInventory))!;

        Assert.Equal(["StoreId", "VariantId"], stock.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.True(stock.FindProperty(nameof(StoreVariantInventory.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(stock.GetCheckConstraints(), check => check.Name == "CK_StoreVariantInventory_NotNegative");
        Assert.Equal(2, stock.GetForeignKeys().Count());
    }

    [Fact]
    public void Cart_lines_are_keyed_by_variant_and_order_lines_carry_nullable_snapshots()
    {
        var model = DesignTimeModel();

        Assert.Equal(["CartId", "VariantId"], model.FindEntityType(typeof(CartItem))!.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        var order = model.FindEntityType(typeof(OrderItem))!;
        Assert.True(order.FindProperty(nameof(OrderItem.VariantId))!.IsNullable);
        Assert.True(order.FindProperty(nameof(OrderItem.VariantLabelSnapshot))!.IsNullable);
        Assert.True(order.FindProperty(nameof(OrderItem.UnitMrpSnapshot))!.IsNullable);
        Assert.Equal(40, order.FindProperty(nameof(OrderItem.VariantLabelSnapshot))!.GetMaxLength());
    }

    [Fact]
    public void The_old_stock_table_is_still_in_the_model_as_the_rollback_copy()
    {
        Assert.NotNull(DesignTimeModel().FindEntityType(typeof(StoreInventory)));
    }

    [Fact]
    public void The_migration_backfills_verifies_and_never_drops_existing_data()
    {
        var source = SourceOf("AddProductVariants.cs");

        // Backfill and verification come before the cart is re-keyed.
        Assert.True(source.IndexOf("INSERT INTO [ProductVariants]", StringComparison.Ordinal) < source.IndexOf("DropPrimaryKey", StringComparison.Ordinal));
        Assert.Contains("INSERT INTO [StoreVariantInventory]", source);
        Assert.Contains("THROW 50001", source);
        Assert.Contains("THROW 50002", source);
        Assert.True(source.IndexOf("THROW 50002", StringComparison.Ordinal) < source.IndexOf("DropPrimaryKey", StringComparison.Ordinal));

        // Up never drops a table or a column.
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];
        Assert.DoesNotContain("DropTable", up);
        Assert.DoesNotContain("DropColumn", up);
        Assert.DoesNotContain("DELETE FROM", up, StringComparison.OrdinalIgnoreCase);
    }

    private static string SourceOf(string migrationSuffix)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(ThisFile())!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickCommerce.slnx")))
        {
            directory = directory.Parent;
        }

        var migrations = Path.Combine(directory!.FullName, "src", "QuickCommerce.Infrastructure", "Persistence", "Migrations");
        return File.ReadAllText(Directory.GetFiles(migrations, "*_" + migrationSuffix).Single());
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
