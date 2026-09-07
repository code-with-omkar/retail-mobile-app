using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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

public sealed class OrderOperationsTests
{
    [Fact]
    public async Task Store_staff_can_complete_the_valid_order_lifecycle_and_history()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = data.Stores.First();
        var user = data.Users.Single();
        user.Role = Role.StoreStaff;
        user.StoreId = store.Id;
        var order = CreateOrder(data, store.Id);
        var service = CreateService(data);

        foreach (var status in new[] { OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed })
        {
            var result = await service.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(status));
            Assert.Equal(OrderOperationStatus.Succeeded, result.Status);
        }

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(
            new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed },
            order.StatusHistory.Select(history => history.Status));
    }

    [Fact]
    public async Task Pending_can_be_rejected_but_invalid_transitions_are_blocked()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = data.Stores.First();
        var user = data.Users.Single();
        user.Role = Role.StoreStaff;
        user.StoreId = store.Id;
        var order = CreateOrder(data, store.Id);
        var service = CreateService(data);

        var rejected = await service.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Rejected));
        var invalid = await service.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));

        Assert.Equal(OrderOperationStatus.Succeeded, rejected.Status);
        Assert.Equal(OrderOperationStatus.InvalidTransition, invalid.Status);
        Assert.Equal(new[] { OrderStatus.Pending, OrderStatus.Rejected }, order.StatusHistory.Select(history => history.Status));
    }

    [Fact]
    public async Task Staff_cannot_operate_another_store_and_customers_cannot_operate_orders()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var firstStore = data.Stores[0];
        var otherStore = data.Stores[1];
        var user = data.Users.Single();
        user.Role = Role.StoreStaff;
        user.StoreId = firstStore.Id;
        var otherOrder = CreateOrder(data, otherStore.Id);
        var service = CreateService(data);

        var otherStoreResult = await service.ChangeStatusAsync(otherOrder.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));
        user.Role = Role.Customer;
        var customerResult = await service.ChangeStatusAsync(otherOrder.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));

        Assert.Equal(OrderOperationStatus.NotFound, otherStoreResult.Status);
        Assert.Equal(OrderOperationStatus.Unauthorized, customerResult.Status);
    }

    [Fact]
    public async Task Organization_admin_can_operate_orders_across_its_stores()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var store = data.Stores[1];
        var user = data.Users.Single();
        user.Role = Role.Admin;
        user.StoreId = null;
        var order = CreateOrder(data, store.Id);
        var service = CreateService(data);

        var result = await service.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted));

        Assert.Equal(OrderOperationStatus.Succeeded, result.Status);
    }

    private static Order CreateOrder(InMemoryCommerceStore data, Guid storeId)
    {
        var product = data.Products.First();
        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            UserId = data.Users.Single().Id,
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

    private static OrderOperationsService CreateService(InMemoryCommerceStore data)
    {
        var user = data.Users.Single();
        var organization = data.Organizations.Single();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ExternalSubject),
            new("organization_id", organization.Id.ToString())
        };
        if (user.StoreId.HasValue)
        {
            claims.Add(new Claim("store_id", user.StoreId.Value.ToString()));
        }

        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        return new OrderOperationsService(data, resolver, new OrderLifecycleRequestValidator());
    }
}