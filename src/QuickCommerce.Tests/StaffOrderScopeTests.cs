using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Task 6.10: staff see the orders of the store they operate (their primary store), which is the same store they can change statuses in,
/// even when they are assigned to other stores as well. Application admins are not limited by store.
/// </summary>
public sealed class StaffOrderScopeTests
{
    private sealed class FixedScope(AuthorizationScope scope) : IAuthorizationScopeService
    {
        public Task<AuthorizationScope?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(scope);
        public Task<AuthorizationScope?> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationScope?>(scope);
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanAccessStoreAsync(Guid storeId, CancellationToken cancellationToken = default) => Task.FromResult(scope.CanAccessStore(storeId));
        public Task<bool> OwnsCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required Store Primary { get; init; }
        public required Store Other { get; init; }
        public required Order PrimaryOrder { get; init; }
        public required Order OtherOrder { get; init; }
        public required OrdersController Controller { get; init; }
        public required OrderOperationsService Operations { get; init; }
    }

    private static Order NewOrder(InMemoryCommerceStore data, Store store)
    {
        var product = data.Products.First();
        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            UserId = data.Users.Single().Id,
            StoreId = store.Id,
            DeliveryAddress = "12 Main Street",
            Items = [new OrderItem { ProductId = product.Id, ProductNameSnapshot = product.Name, UnitPrice = product.Price, Quantity = 1 }],
            StatusHistory = [new OrderStatusHistory { Status = OrderStatus.Pending }],
            TotalAmount = product.Price
        };
        data.Orders.Add(order);
        return order;
    }

    private static Rig Create(bool admin = false)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var stores = data.Stores.Take(2).ToArray();
        var user = data.Users.Single();
        user.Role = admin ? Role.Admin : Role.StoreStaff;
        user.StoreId = stores[0].Id;
        var organization = data.Organizations.Single();
        var primaryOrder = NewOrder(data, stores[0]);
        var otherOrder = NewOrder(data, stores[1]);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ExternalSubject), new("organization_id", organization.Id.ToString()), new("store_id", stores[0].Id.ToString()) };
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        var roles = new HashSet<string> { admin ? "ApplicationAdmin" : "StoreStaff" };
        // Assigned to BOTH stores, but operating only the first.
        var scope = new AuthorizationScope(user.Id, organization.Id, roles, new HashSet<Guid> { stores[0].Id, stores[1].Id }, new HashSet<string>());
        var controller = new OrdersController(null!, null!, resolver, new FixedScope(scope), data) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return new Rig
        {
            Data = data,
            Primary = stores[0],
            Other = stores[1],
            PrimaryOrder = primaryOrder,
            OtherOrder = otherOrder,
            Controller = controller,
            Operations = new OrderOperationsService(data, resolver, new OrderLifecycleRequestValidator())
        };
    }

    private static Guid[] Ids(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        return json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("Id").GetGuid()).ToArray();
    }

    [Fact]
    public async Task Staff_see_only_the_orders_of_the_store_they_operate_even_when_assigned_to_more()
    {
        var rig = Create();

        var ids = Ids(await rig.Controller.Orders(CancellationToken.None));

        Assert.Equal([rig.PrimaryOrder.Id], ids);
    }

    [Fact]
    public async Task Every_order_the_list_shows_can_be_acted_on_and_the_other_stores_order_is_neither_listed_nor_actionable()
    {
        var rig = Create();

        var listed = Ids(await rig.Controller.Orders(CancellationToken.None));
        var mine = await rig.Operations.ChangeStatusAsync(rig.PrimaryOrder.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));
        var theirs = await rig.Operations.ChangeStatusAsync(rig.OtherOrder.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));

        Assert.All(listed, id => Assert.Equal(rig.PrimaryOrder.Id, id));
        Assert.Equal(OrderOperationStatus.Succeeded, mine.Status);
        Assert.Equal(OrderOperationStatus.NotFound, theirs.Status);
        Assert.IsType<NotFoundObjectResult>(await rig.Controller.Get(rig.OtherOrder.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await rig.Controller.Get(rig.PrimaryOrder.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_staff_member_with_no_operating_store_sees_nothing()
    {
        var rig = Create();
        rig.Data.Users.Single().StoreId = null;
        var controller = rig.Controller;

        var result = await controller.Orders(CancellationToken.None);

        Assert.Empty(Ids(result));
    }

    [Fact]
    public async Task An_application_admin_still_sees_every_store()
    {
        var rig = Create(admin: true);

        var ids = Ids(await rig.Controller.Orders(CancellationToken.None));

        Assert.Equal(new[] { rig.PrimaryOrder.Id, rig.OtherOrder.Id }.Order(), ids.Order());
    }
}
