using Microsoft.EntityFrameworkCore;

namespace QuickCommerce.Infrastructure.Persistence;

internal static class SeedData
{
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoUserId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoCustomerId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid GroceriesId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VegetablesId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid FruitsId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid DairyId = Guid.Parse("10000000-0000-0000-0000-000000000004");

    private static readonly ProductSeed[] Products =
    [
        new("20000000-0000-0000-0000-000000000001", "VEG-TOM", "Tomato", "Fresh red tomatoes", 45m, "1 kg", VegetablesId, "https://images.unsplash.com/photo-1546094096-0df4bcaaa337?w=640"),
        new("20000000-0000-0000-0000-000000000002", "VEG-POT", "Potato", "Everyday cooking potatoes", 38m, "1 kg", VegetablesId, "https://images.unsplash.com/photo-1518977676601-b53f82aba655?w=640"),
        new("20000000-0000-0000-0000-000000000003", "DAI-MILK", "Farm Milk", "Pasteurized full cream milk", 34m, "500 ml", DairyId, "https://images.unsplash.com/photo-1550583724-b2692b85b150?w=640"),
        new("20000000-0000-0000-0000-000000000004", "FRT-APL", "Royal Gala Apples", "Crisp and naturally sweet", 149m, "1 kg", FruitsId, "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=640"),
        new("20000000-0000-0000-0000-000000000005", "GRO-RICE", "Daily Rice", "Long grain rice for every meal", 89m, "1 kg", GroceriesId, "https://images.unsplash.com/photo-1586201375761-83865001e31c?w=640")
    ];

    private static readonly StoreSeed[] Stores =
    [
        new("30000000-0000-0000-0000-000000000001", "Harbor Point Dark Store", "12 Marine Drive", 19.076, 72.8777, 8),
        new("30000000-0000-0000-0000-000000000002", "Cedar Market Hub", "44 Cedar Avenue", 19.102, 72.916, 7),
        new("30000000-0000-0000-0000-000000000003", "North Star Fulfillment", "8 Station Road", 19.045, 72.899, 9)
    ];

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Category>().HasData(
            new { Id = GroceriesId, Name = "Groceries", ParentCategoryId = (Guid?)null, IsActive = true },
            new { Id = VegetablesId, Name = "Vegetables", ParentCategoryId = (Guid?)null, IsActive = true },
            new { Id = FruitsId, Name = "Fruits", ParentCategoryId = (Guid?)null, IsActive = true },
            new { Id = DairyId, Name = "Dairy", ParentCategoryId = (Guid?)null, IsActive = true });

        modelBuilder.Entity<Domain.Organization>().HasData(
            new { Id = OrganizationId, Name = "QuickCart Demo Retailer", IsActive = true });

        modelBuilder.Entity<Domain.Product>().HasData(Products.Select(product => new
        {
            Id = Guid.Parse(product.Id), product.Sku, product.Name, product.Description, product.Price,
            product.UnitOfMeasure, product.CategoryId, product.ImageUrl, IsActive = true
        }));

        modelBuilder.Entity<Domain.Store>().HasData(Stores.Select(store => new
        {
            Id = Guid.Parse(store.Id), OrganizationId, store.Name, store.Address, store.Latitude, store.Longitude,
            store.ServiceRadiusKm, IsActive = true
        }));

        modelBuilder.Entity<Domain.User>().HasData(
            new
            {
                Id = DemoUserId,
                ExternalSubject = "quickcart-demo-user",
                DisplayName = "QuickCart Demo User",
                OrganizationId,
                StoreId = (Guid?)null,
                Role = Domain.Role.Customer,
                IsActive = true
            });

        modelBuilder.Entity<Domain.Customer>().HasData(
            new
            {
                Id = DemoCustomerId,
                UserId = DemoUserId,
                IsActive = true
            });

        var quantities = new[] { 24, 8, 0, 16, 5 };
        var inventory = Stores.SelectMany((store, storeIndex) => Products.Select((product, productIndex) => new
        {
            StoreId = Guid.Parse(store.Id),
            ProductId = Guid.Parse(product.Id),
            AvailableQuantity = Math.Max(0, quantities[productIndex] + storeIndex * 4),
            ReorderThreshold = 5
        }));
        modelBuilder.Entity<Domain.StoreInventory>().HasData(inventory);
    }

    private sealed record ProductSeed(string Id, string Sku, string Name, string Description, decimal Price, string UnitOfMeasure, Guid CategoryId, string ImageUrl);
    private sealed record StoreSeed(string Id, string Name, string Address, double Latitude, double Longitude, double ServiceRadiusKm);
}
