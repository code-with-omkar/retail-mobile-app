using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class CustomerTrackingTests
{
    [Fact]
    public async Task Customer_history_and_details_return_only_the_authenticated_customers_orders()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var user = data.Users.Single();
        var store = data.Stores.First();
        var ownOrder = CreateOrder(data, user.Id, store.Id);
        var otherUser = new User
        {
            ExternalSubject = "other-customer",
            DisplayName = "Other Customer",
            OrganizationId = user.OrganizationId,
            Organization = user.Organization
        };
        data.Users.Add(otherUser);
        var otherOrder = CreateOrder(data, otherUser.Id, store.Id);
        var service = CreateOrderService(data, user);

        var history = await service.GetHistoryAsync();
        var ownDetails = await service.GetDetailsAsync(ownOrder.Id);
        var otherDetails = await service.GetDetailsAsync(otherOrder.Id);

        Assert.Single(history!);
        Assert.Equal(ownOrder.Id, history![0].Id);
        Assert.Equal(ownOrder.Id, ownDetails!.Id);
        Assert.Null(otherDetails);
    }

    [Fact]
    public async Task Customer_history_rejects_an_order_from_another_organization_store()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var user = data.Users.Single();
        var otherOrganization = new Organization { Name = "Other Retailer" };
        var otherStore = new Store
        {
            OrganizationId = otherOrganization.Id,
            Organization = otherOrganization,
            Name = "Other Store",
            Address = "Other address",
            Latitude = 19,
            Longitude = 72
        };
        data.Organizations.Add(otherOrganization);
        data.Stores.Add(otherStore);
        var otherOrder = CreateOrder(data, user.Id, otherStore.Id);
        var service = CreateOrderService(data, user);

        Assert.Null(await service.GetDetailsAsync(otherOrder.Id));
        Assert.Empty(await service.GetHistoryAsync() ?? []);
    }

    [Fact]
    public async Task Lifecycle_transition_creates_customer_notification_and_read_state_is_reversible()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var user = data.Users.Single();
        var store = data.Stores.First();
        user.Role = Role.StoreStaff;
        user.StoreId = store.Id;
        var order = CreateOrder(data, user.Id, store.Id);
        var operations = new OrderOperationsService(data, CreateResolver(data, user), new QuickCommerce.Application.Validators.OrderLifecycleRequestValidator());

        var transition = await operations.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));

        Assert.Equal(QuickCommerce.Application.Interfaces.OrderOperationStatus.Succeeded, transition.Status);
        var notification = Assert.Single(data.Notifications);
        Assert.Equal(order.Id, notification.OrderId);
        Assert.False(notification.IsRead);

        user.Role = Role.Customer;
        user.StoreId = null;
        var notifications = new NotificationService(data, CreateResolver(data, user));
        var unread = await notifications.GetAsync(unreadOnly: true);
        Assert.NotNull(unread);
        Assert.Single(unread);
        Assert.True(await notifications.SetReadAsync(notification.Id, true));
        unread = await notifications.GetAsync(unreadOnly: true);
        Assert.NotNull(unread);
        Assert.Empty(unread);
        Assert.True(await notifications.SetReadAsync(notification.Id, false));
        unread = await notifications.GetAsync(unreadOnly: true);
        Assert.NotNull(unread);
        Assert.Single(unread);
    }

    private static Order CreateOrder(InMemoryCommerceStore data, Guid userId, Guid storeId)
    {
        var product = data.Products.First();
        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            UserId = userId,
            StoreId = storeId,
            DeliveryAddress = "12 Main Street",
            Latitude = 19.07,
            Longitude = 72.87,
            Items = [new OrderItem { ProductId = product.Id, ProductNameSnapshot = product.Name, UnitPrice = product.Price, Quantity = 1 }],
            StatusHistory = [new OrderStatusHistory { Status = OrderStatus.Pending }]
        };
        order.TotalAmount = product.Price;
        data.Orders.Add(order);
        return order;
    }

    private static CustomerOrderService CreateOrderService(InMemoryCommerceStore data, User user) =>
        new(data, CreateResolver(data, user));

    private static CurrentUserContextResolver CreateResolver(InMemoryCommerceStore data, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ExternalSubject),
            new("organization_id", user.OrganizationId.ToString())
        };
        if (user.StoreId.HasValue)
        {
            claims.Add(new Claim("store_id", user.StoreId.Value.ToString()));
        }

        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        });
        return new CurrentUserContextResolver(currentUser, data);
    }
}