using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuickCommerce.Api.Controllers;
using QuickCommerce.Api.Security;
using QuickCommerce.Application;
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
/// Phase P6: cancelling an order that the shop has not accepted, the store and arrival estimate on the order, and the unread
/// count and mark-all for notifications. The SQL Server behaviour (the race with the shop, the stock) is in OrderCancelEfIntegrationTests.
/// </summary>
public sealed class OrderCancelAndNotificationsTests
{
    private static Store Harbor(InMemoryCommerceStore data) => data.Stores.Single(store => store.Name == "Harbor Point Dark Store");
    private static Product Tomato(InMemoryCommerceStore data) => data.Products.Single(product => product.Name == "Tomato");

    private sealed class Rig
    {
        public required InMemoryCommerceStore Data { get; init; }
        public required CartService Cart { get; init; }
        public required CheckoutService Checkout { get; init; }
        public required CustomerOrderService Orders { get; init; }
        public required NotificationService Notifications { get; init; }
        public required Store Store { get; init; }
        public required Product Tomato { get; init; }

        public int Stock => Data.StockOf(Store, Tomato).AvailableQuantity;
        public Customer Customer => Data.Customers.Single();

        public async Task<OrderResponse> PlaceAsync(int quantity = 2, string key = "place-key-000001-abcd")
        {
            await Cart.AddItemAsync(Store.Id, new AddCartItemRequest(Tomato.Id, quantity));
            var placed = await Checkout.CheckoutAsync(Store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87), key);
            Assert.Equal(CheckoutOperationStatus.Succeeded, placed.Status);
            return placed.Order!;
        }
    }

    private static Rig NewRig(Action<InMemoryCommerceStore>? arrange = null)
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        arrange?.Invoke(data);
        var user = data.Users.Single();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", data.Organizations.Single().Id.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        var estimator = new DeliveryEstimator(new DeliverySettings());
        return new Rig
        {
            Data = data,
            Cart = new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator(), new PricingSettings()),
            Checkout = new CheckoutService(data, resolver, new CheckoutRequestValidator(), new PricingSettings(), null, estimator),
            Orders = new CustomerOrderService(data, resolver),
            Notifications = new NotificationService(data, resolver),
            Store = Harbor(data),
            Tomato = Tomato(data)
        };
    }

    // ---------------- cancelling ----------------

    [Fact]
    public async Task A_pending_order_can_be_cancelled_and_the_stock_goes_back()
    {
        var rig = NewRig();
        var before = rig.Stock;
        var order = await rig.PlaceAsync(quantity: 3);
        Assert.Equal(before - 3, rig.Stock);

        var result = await rig.Orders.CancelAsync(order.Id);

        Assert.Equal(CancelOrderStatus.Succeeded, result.Status);
        Assert.Equal(OrderStatus.Cancelled, result.Order!.Status);
        Assert.Equal(before, rig.Stock);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Cancelled], result.Order.StatusHistory.Select(history => history.Status));
        Assert.Equal(OrderStatus.Cancelled, rig.Data.Orders.Single().Status);
    }

    [Fact]
    public async Task Cancelling_writes_one_notification_for_the_customer_after_the_order_placed_one()
    {
        var rig = NewRig();
        var order = await rig.PlaceAsync();

        await rig.Orders.CancelAsync(order.Id);

        Assert.Equal(2, rig.Data.Notifications.Count);
        var notification = Assert.Single(rig.Data.Notifications, item => item.Type == NotificationTypes.OrderCancelled);
        Assert.Equal((rig.Customer.Id, order.Id, "Order cancelled", $"Your order {order.OrderNumber} was cancelled."), (notification.CustomerId, notification.OrderId, notification.Title, notification.Message));
        Assert.Equal(2, await rig.Notifications.GetUnreadCountAsync());
    }

    [Fact]
    public async Task Cancelling_twice_gives_the_same_order_and_returns_the_stock_once()
    {
        var rig = NewRig();
        var before = rig.Stock;
        var order = await rig.PlaceAsync(quantity: 2);

        var first = await rig.Orders.CancelAsync(order.Id);
        var second = await rig.Orders.CancelAsync(order.Id);

        Assert.Equal(CancelOrderStatus.Succeeded, second.Status);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
        Assert.Equal(before, rig.Stock);
        Assert.Equal(2, rig.Data.Notifications.Count); // order placed, order cancelled: nothing twice
        Assert.Equal(2, rig.Data.Orders.Single().StatusHistory.Count);
    }

    [Theory]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Rejected)]
    public async Task An_order_the_shop_has_taken_up_cannot_be_cancelled_and_nothing_changes(OrderStatus status)
    {
        var rig = NewRig();
        var order = await rig.PlaceAsync(quantity: 2);
        rig.Data.Orders.Single().Status = status;
        var stock = rig.Stock;

        var result = await rig.Orders.CancelAsync(order.Id);

        Assert.Equal((CancelOrderStatus.NotCancellable, OrderCancelReasons.OrderNotCancellable), (result.Status, result.Reason));
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(status, rig.Data.Orders.Single().Status);
        Assert.Equal(stock, rig.Stock);
        Assert.Equal(NotificationTypes.OrderPlaced, Assert.Single(rig.Data.Notifications).Type); // only the one from placing it
    }

    [Fact]
    public async Task The_shop_cannot_accept_an_order_the_customer_has_cancelled()
    {
        var rig = NewRig();
        var order = await rig.PlaceAsync();
        await rig.Orders.CancelAsync(order.Id);

        var result = await rig.Data.TryTransitionOrderAsync(order.Id, rig.Data.Organizations.Single().Id, null, OrderStatus.Accepted);

        Assert.Equal(OrderLifecycleStatus.InvalidTransition, result.Status);
        Assert.Equal(OrderStatus.Cancelled, rig.Data.Orders.Single().Status);
    }

    [Fact]
    public async Task Another_customers_order_is_not_found_and_stays_as_it_was()
    {
        var rig = NewRig();
        var other = new User { ExternalSubject = "other", DisplayName = "Other", OrganizationId = rig.Data.Users.Single().OrganizationId, Organization = rig.Data.Users.Single().Organization };
        rig.Data.Users.Add(other);
        var theirs = new Order
        {
            OrderNumber = "ORD-OTHER-0001", UserId = other.Id, StoreId = rig.Store.Id, DeliveryAddress = "x", TotalAmount = 10,
            Items = [new OrderItem { ProductId = rig.Tomato.Id, VariantId = rig.Data.DefaultVariantOf(rig.Tomato).Id, ProductNameSnapshot = "Tomato", UnitPrice = 10, Quantity = 5 }],
            StatusHistory = [new OrderStatusHistory { Status = OrderStatus.Pending }]
        };
        rig.Data.Orders.Add(theirs);
        var stock = rig.Stock;

        var result = await rig.Orders.CancelAsync(theirs.Id);

        Assert.Equal(CancelOrderStatus.NotFound, result.Status);
        Assert.Equal(OrderStatus.Pending, theirs.Status);
        Assert.Equal(stock, rig.Stock);
        Assert.Equal(CancelOrderStatus.NotFound, (await rig.Orders.CancelAsync(Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task Without_a_signed_in_customer_nothing_can_be_cancelled()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var nobody = new CurrentUserContextResolver(new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }), data);

        var result = await new CustomerOrderService(data, nobody).CancelAsync(Guid.NewGuid());

        Assert.Equal(CancelOrderStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task Every_line_of_the_order_gives_its_stock_back()
    {
        var rig = NewRig();
        var half = new ProductVariant { ProductId = rig.Tomato.Id, Sku = "VEG-TOM-500G", Label = "500 g", Price = 15, SortOrder = 1 };
        rig.Data.Variants.Add(half);
        rig.Data.Inventory.Add(new StoreVariantInventory { StoreId = rig.Store.Id, VariantId = half.Id, AvailableQuantity = 10 });
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 2));
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 4, half.Id));
        var kiloBefore = rig.Stock;
        var placed = await rig.Checkout.CheckoutAsync(rig.Store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87), "lines-key-000001-abcd");
        Assert.Equal((kiloBefore - 2, 6), (rig.Stock, rig.Data.Inventory.Single(row => row.VariantId == half.Id).AvailableQuantity));

        await rig.Orders.CancelAsync(placed.Order!.Id);

        Assert.Equal((kiloBefore, 10), (rig.Stock, rig.Data.Inventory.Single(row => row.VariantId == half.Id).AvailableQuantity));
    }

    // ---------------- store and estimate on the order ----------------

    [Fact]
    public async Task The_order_keeps_the_arrival_estimate_it_was_given_even_if_the_store_moves()
    {
        var rig = NewRig();
        var order = await rig.PlaceAsync();
        var expected = new DeliveryEstimator(new DeliverySettings()).EstimateMinutes(StoreSelectionService.DistanceKm(19.07, 72.87, rig.Store.Latitude, rig.Store.Longitude));
        Assert.Equal(expected, order.EstimatedDeliveryMinutes);

        rig.Store.Latitude += 0.2;
        var later = await rig.Orders.GetDetailsAsync(order.Id);

        Assert.Equal(expected, later!.EstimatedDeliveryMinutes);
        Assert.Equal(expected, rig.Data.Orders.Single().EstimatedDeliveryMinutes);
    }

    [Fact]
    public async Task The_order_shows_the_store_name_and_phone_when_placed_in_the_history_and_in_the_details()
    {
        var rig = NewRig(data => Harbor(data).PhoneNumber = "02212345678");
        var order = await rig.PlaceAsync();

        var history = await rig.Orders.GetHistoryAsync();
        var details = await rig.Orders.GetDetailsAsync(order.Id);

        Assert.Equal(("Harbor Point Dark Store", "02212345678"), (order.StoreName, order.StorePhone));
        Assert.Equal(("Harbor Point Dark Store", "02212345678"), (history!.Single().StoreName, history!.Single().StorePhone));
        Assert.Equal(("Harbor Point Dark Store", "02212345678"), (details!.StoreName, details.StorePhone));
    }

    [Fact]
    public async Task A_store_without_a_phone_gives_none_and_an_old_order_has_no_estimate()
    {
        var rig = NewRig();
        var old = new Order
        {
            OrderNumber = "ORD-OLD-0001", UserId = rig.Data.Users.Single().Id, StoreId = rig.Store.Id, DeliveryAddress = "x", TotalAmount = 10, Status = OrderStatus.Completed,
            Items = [new OrderItem { ProductId = rig.Tomato.Id, ProductNameSnapshot = "Tomato", UnitPrice = 10, Quantity = 1 }],
            StatusHistory = [new OrderStatusHistory { Status = OrderStatus.Completed }]
        };
        rig.Data.Orders.Add(old);

        var details = await rig.Orders.GetDetailsAsync(old.Id);

        Assert.Equal(("Harbor Point Dark Store", null, null), (details!.StoreName, details.StorePhone, details.EstimatedDeliveryMinutes));
    }

    [Fact]
    public async Task A_repeat_of_the_same_checkout_still_carries_the_store()
    {
        var rig = NewRig(data => Harbor(data).PhoneNumber = "02212345678");
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        await rig.Checkout.CheckoutAsync(rig.Store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87), "repeat-key-000001-abcd");

        var again = await rig.Checkout.CheckoutAsync(rig.Store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87), "repeat-key-000001-abcd");

        Assert.True(again.Replayed);
        Assert.Equal(("Harbor Point Dark Store", "02212345678"), (again.Order!.StoreName, again.Order.StorePhone));
    }

    // ---------------- notifications ----------------

    private static Notification Note(Guid customerId, bool read = false) => new() { CustomerId = customerId, Type = "OrderStatusChanged", Title = "t", Message = "m", IsRead = read };

    [Fact]
    public async Task The_unread_count_counts_only_the_customers_own_unread_notifications()
    {
        var rig = NewRig();
        rig.Data.Notifications.AddRange([Note(rig.Customer.Id), Note(rig.Customer.Id), Note(rig.Customer.Id, read: true), Note(Guid.NewGuid())]);

        Assert.Equal(2, await rig.Notifications.GetUnreadCountAsync());
    }

    [Fact]
    public async Task Mark_all_read_changes_only_the_customers_unread_ones_and_a_second_call_changes_nothing()
    {
        var rig = NewRig();
        var others = Note(Guid.NewGuid());
        rig.Data.Notifications.AddRange([Note(rig.Customer.Id), Note(rig.Customer.Id), Note(rig.Customer.Id, read: true), others]);

        Assert.Equal(2, await rig.Notifications.MarkAllReadAsync());
        Assert.Equal(0, await rig.Notifications.MarkAllReadAsync());

        Assert.Equal(0, await rig.Notifications.GetUnreadCountAsync());
        Assert.False(others.IsRead);
    }

    [Fact]
    public async Task Without_a_signed_in_customer_there_is_no_count_and_nothing_is_marked()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var nobody = new CurrentUserContextResolver(new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }), data);
        var service = new NotificationService(data, nobody);

        Assert.Null(await service.GetUnreadCountAsync());
        Assert.Null(await service.MarkAllReadAsync());
    }

    // ---------------- the routes ----------------

    private static object? Field(object body, string name) => body.GetType().GetProperty(name)!.GetValue(body);

    [Fact]
    public async Task The_cancel_route_answers_200_404_and_409_with_the_reason()
    {
        var rig = NewRig();
        var controller = new CustomerOrdersController(rig.Orders);
        var pending = await rig.PlaceAsync();
        await rig.Cart.AddItemAsync(rig.Store.Id, new AddCartItemRequest(rig.Tomato.Id, 1));
        var accepted = (await rig.Checkout.CheckoutAsync(rig.Store.Id, new CheckoutRequest("12 Main Street", 19.07, 72.87), "second-key-00001-abcd")).Order!;
        rig.Data.Orders.Single(order => order.Id == accepted.Id).Status = OrderStatus.Accepted;

        var cancelled = Assert.IsType<OkObjectResult>(await controller.Cancel(pending.Id, CancellationToken.None));
        var notFound = Assert.IsType<NotFoundObjectResult>(await controller.Cancel(Guid.NewGuid(), CancellationToken.None));
        var refused = Assert.IsType<ConflictObjectResult>(await controller.Cancel(accepted.Id, CancellationToken.None));

        Assert.Equal(OrderStatus.Cancelled, ((OrderResponse)Field(cancelled.Value!, "data")!).Status);
        Assert.Equal(false, Field(notFound.Value!, "success"));
        Assert.Equal(OrderCancelReasons.OrderNotCancellable, Field(refused.Value!, "reason"));
    }

    [Fact]
    public async Task The_notification_routes_return_the_count_and_the_number_marked()
    {
        var rig = NewRig();
        rig.Data.Notifications.AddRange([Note(rig.Customer.Id), Note(rig.Customer.Id)]);
        var controller = new NotificationsController(rig.Notifications);

        var count = Assert.IsType<OkObjectResult>(await controller.UnreadCount(CancellationToken.None));
        var marked = Assert.IsType<OkObjectResult>(await controller.ReadAll(CancellationToken.None));
        var after = Assert.IsType<OkObjectResult>(await controller.UnreadCount(CancellationToken.None));

        Assert.Equal(2, ((UnreadCountResponse)Field(count.Value!, "data")!).Count);
        Assert.Equal(2, Field(Field(marked.Value!, "data")!, "updated"));
        Assert.Equal(0, ((UnreadCountResponse)Field(after.Value!, "data")!).Count);
    }
}
