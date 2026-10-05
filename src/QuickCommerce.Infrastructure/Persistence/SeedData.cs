using Microsoft.EntityFrameworkCore;

namespace QuickCommerce.Infrastructure.Persistence;

internal static class SeedData
{
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoUserId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUserId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid StoreStaffUserId = Guid.Parse("40000000-0000-0000-0000-000000000003");
    private static readonly Guid CustomerUserId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid MultiStoreStaffUserId = Guid.Parse("40000000-0000-0000-0000-000000000005");
    private static readonly Guid DeliveryPartnerUserId = Guid.Parse("40000000-0000-0000-0000-000000000006");
    private static readonly Guid StoreManagerUserId = Guid.Parse("40000000-0000-0000-0000-000000000007");
    private static readonly Guid StoreEmployeeUserId = Guid.Parse("40000000-0000-0000-0000-000000000008");
    private static readonly Guid DemoCustomerId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerProfileId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid GroceriesId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VegetablesId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid FruitsId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid DairyId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
        modelBuilder.Entity<Domain.AuthorizationRole>().HasData(
            new { Id = Guid.Parse("70000000-0000-0000-0000-000000000001"), Name = "Customer", Code = "Customer", Description = "Default customer role", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("70000000-0000-0000-0000-000000000002"), Name = "Store Staff", Code = "StoreStaff", Description = "Store operations role", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("70000000-0000-0000-0000-000000000003"), Name = "Application Admin", Code = "ApplicationAdmin", Description = "Organization-wide administration role", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("70000000-0000-0000-0000-000000000004"), Name = "Delivery Partner", Code = "DeliveryPartner", Description = "Future delivery operations role", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" });

        modelBuilder.Entity<Domain.AuthorizationPermission>().HasData(
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000001"), Name = "View orders", Code = "orders:read", Description = "Read order data", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000002"), Name = "Operate on orders", Code = "orders:operate", Description = "Accept and manage orders", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000003"), Name = "Manage users", Code = "users:manage", Description = "Create and edit users", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000004"), Name = "Manage roles", Code = "roles:manage", Description = "Create and edit roles", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000005"), Name = "Manage permissions", Code = "permissions:manage", Description = "Create and edit permissions", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000006"), Name = "Read stores", Code = "Store.Read", Description = "Read store data", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000007"), Name = "Read customers", Code = "Customer.Read", Description = "Read customer data", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { Id = Guid.Parse("71000000-0000-0000-0000-000000000008"), Name = "Read deliveries", Code = "Delivery.Read", Description = "Read future delivery data", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" });

        modelBuilder.Entity<Domain.RolePermission>().HasData(
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000001"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000001"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000002"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000001"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000002"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000002"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000001"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000002"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000003"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000004"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000005"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000006"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000007"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000008"), IsActive = true },
            new { RoleId = Guid.Parse("70000000-0000-0000-0000-000000000004"), PermissionId = Guid.Parse("71000000-0000-0000-0000-000000000008"), IsActive = true });

        modelBuilder.Entity<Domain.Category>().HasData(
            new { Id = GroceriesId, Name = "Groceries", ParentCategoryId = (Guid?)null, IsActive = true },
            new { Id = VegetablesId, Name = "Vegetables", ParentCategoryId = (Guid?)null, IsActive = true },
            new { Id = FruitsId, Name = "Fruits", ParentCategoryId = (Guid?)null, IsActive = true },
            new { Id = DairyId, Name = "Dairy", ParentCategoryId = (Guid?)null, IsActive = true });

        modelBuilder.Entity<Domain.Organization>().HasData(
            new { Id = OrganizationId, Name = "QuickCart Demo Retailer", Code = "QUICKCART-DEMO", IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" });

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
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = AdminUserId,
                ExternalSubject = "admin",
                DisplayName = "Demo Administrator",
                FirstName = "Demo",
                LastName = "Administrator",
                Email = "admin@example.test",
                OrganizationId,
                StoreId = (Guid?)null,
                Role = Domain.Role.Admin,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = StoreStaffUserId,
                ExternalSubject = "storestaff",
                DisplayName = "Demo Store Staff",
                FirstName = "Demo",
                LastName = "Store Staff",
                Email = "storestaff@example.test",
                OrganizationId,
                StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Role = Domain.Role.StoreStaff,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = CustomerUserId,
                ExternalSubject = "customer",
                DisplayName = "Demo Customer",
                FirstName = "Demo",
                LastName = "Customer",
                Email = "customer@example.test",
                OrganizationId,
                StoreId = (Guid?)null,
                Role = Domain.Role.Customer,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = MultiStoreStaffUserId,
                ExternalSubject = "multistorestaff",
                DisplayName = "Demo Multi Store Staff",
                FirstName = "Demo",
                LastName = "Multi Store Staff",
                Email = "multistorestaff@example.test",
                OrganizationId,
                StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Role = Domain.Role.StoreStaff,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = DeliveryPartnerUserId,
                ExternalSubject = "deliverypartner",
                DisplayName = "Demo Delivery Partner",
                FirstName = "Demo",
                LastName = "Delivery Partner",
                Email = "deliverypartner@example.test",
                OrganizationId,
                StoreId = (Guid?)null,
                Role = Domain.Role.DeliveryPartner,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = StoreManagerUserId,
                ExternalSubject = "storemanager",
                DisplayName = "Demo Store Manager",
                FirstName = "Demo",
                LastName = "Store Manager",
                Email = "storemanager@example.test",
                OrganizationId,
                StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Role = Domain.Role.StoreStaff,
                StaffCategory = Domain.StaffCategory.StoreManager,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            },
            new
            {
                Id = StoreEmployeeUserId,
                ExternalSubject = "storeemployee",
                DisplayName = "Demo Store Employee",
                FirstName = "Demo",
                LastName = "Store Employee",
                Email = "storeemployee@example.test",
                OrganizationId,
                StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Role = Domain.Role.StoreStaff,
                StaffCategory = Domain.StaffCategory.StoreEmployee,
                IsActive = true,
                CreatedAt = SeedTimestamp,
                CreatedBy = "seed"
            });

        modelBuilder.Entity<Domain.UserRole>().HasData(
            new { UserId = DemoUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000001"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = AdminUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000003"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = StoreStaffUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000002"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = CustomerUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000001"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = MultiStoreStaffUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000002"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = MultiStoreStaffUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000004"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = DeliveryPartnerUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000004"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = StoreManagerUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000002"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = StoreEmployeeUserId, RoleId = Guid.Parse("70000000-0000-0000-0000-000000000002"), IsActive = true, CreatedAt = SeedTimestamp, CreatedBy = "seed" });

        modelBuilder.Entity<Domain.UserStoreAssignment>().HasData(
            new { UserId = StoreStaffUserId, StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"), IsActive = true, EffectiveFrom = SeedTimestamp, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = StoreManagerUserId, StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"), IsActive = true, EffectiveFrom = SeedTimestamp, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = StoreEmployeeUserId, StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"), IsActive = true, EffectiveFrom = SeedTimestamp, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = MultiStoreStaffUserId, StoreId = Guid.Parse("30000000-0000-0000-0000-000000000001"), IsActive = true, EffectiveFrom = SeedTimestamp, CreatedAt = SeedTimestamp, CreatedBy = "seed" },
            new { UserId = MultiStoreStaffUserId, StoreId = Guid.Parse("30000000-0000-0000-0000-000000000002"), IsActive = true, EffectiveFrom = SeedTimestamp, CreatedAt = SeedTimestamp, CreatedBy = "seed" });

        modelBuilder.Entity<Domain.Customer>().HasData(
            new
            {
                Id = DemoCustomerId,
                UserId = DemoUserId,
                IsActive = true
            },
            new
            {
                Id = CustomerProfileId,
                UserId = CustomerUserId,
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
