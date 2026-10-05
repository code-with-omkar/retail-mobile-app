using QuickCommerce.Application.DTOs;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class AuthorizationScopeTests
{
    private static readonly Guid StoreA = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid StoreB = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid StoreC = Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public void Application_admin_has_organization_wide_store_scope()
    {
        var scope = Scope("ApplicationAdmin", ["Store.Read"]);

        Assert.True(scope.IsApplicationAdmin);
        Assert.True(scope.HasPermission("Any.Permission"));
        Assert.True(scope.CanAccessStore(StoreC));
    }

    [Fact]
    public void Store_staff_is_limited_to_active_assigned_stores()
    {
        var scope = Scope("StoreStaff", ["orders:read"], StoreA, StoreB);

        Assert.False(scope.IsApplicationAdmin);
        Assert.True(scope.HasPermission("orders:read"));
        Assert.True(scope.CanAccessStore(StoreA));
        Assert.True(scope.CanAccessStore(StoreB));
        Assert.False(scope.CanAccessStore(StoreC));
    }

    [Fact]
    public void Delivery_partner_has_role_without_admin_scope()
    {
        var scope = Scope("DeliveryPartner", ["Delivery.Read"]);

        Assert.False(scope.IsApplicationAdmin);
        Assert.True(scope.HasPermission("Delivery.Read"));
        Assert.False(scope.CanAccessStore(StoreA));
    }

    [Fact]
    public void Customer_scope_is_for_own_resources_only()
    {
        var scope = Scope("Customer", []);

        Assert.False(scope.IsApplicationAdmin);
        Assert.False(scope.HasPermission("Store.Read"));
        Assert.False(scope.CanAccessStore(StoreA));
    }

    [Fact]
    public void Relationships_support_many_roles_and_many_permissions()
    {
        var staffRole = new AuthorizationRole { Name = "Store Staff", Code = "StoreStaff" };
        var deliveryRole = new AuthorizationRole { Name = "Delivery Partner", Code = "DeliveryPartner" };
        var orders = new AuthorizationPermission { Name = "Read orders", Code = "Order.Read" };
        var stores = new AuthorizationPermission { Name = "Read stores", Code = "Store.Read" };
        staffRole.RolePermissions.Add(new RolePermission { Role = staffRole, Permission = orders });
        staffRole.RolePermissions.Add(new RolePermission { Role = staffRole, Permission = stores });
        deliveryRole.RolePermissions.Add(new RolePermission { Role = deliveryRole, Permission = orders });
        var user = new User
        {
            ExternalSubject = "multi-role",
            DisplayName = "Multi Role User",
            OrganizationId = Guid.NewGuid()
        };
        user.UserRoles.Add(new UserRole { User = user, Role = staffRole });
        user.UserRoles.Add(new UserRole { User = user, Role = deliveryRole });

        Assert.Equal(2, user.UserRoles.Count);
        Assert.Equal(2, staffRole.RolePermissions.Count);
        Assert.Contains(deliveryRole.RolePermissions, item => item.Permission.Code == "Order.Read");
    }

    private static AuthorizationScope Scope(string role, string[] permissions, params Guid[] stores) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        new HashSet<string>([role], StringComparer.OrdinalIgnoreCase),
        new HashSet<Guid>(stores),
        new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase));
}