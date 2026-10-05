using QuickCommerce.Domain;

namespace QuickCommerce.Application.DTOs;

public sealed record AdminDashboardSnapshot(
    int TotalStores,
    int ActiveStores,
    int TotalProducts,
    int ActiveProducts,
    int TotalCustomers,
    int TotalOrders,
    int TodaysOrders,
    int ActiveOrders,
    int CompletedOrders,
    int PendingOrders,
    int CancelledOrders,
    decimal TotalSales,
    decimal TodaysSales,
    IReadOnlyList<ActiveOrderSummary> ActiveOrdersList);

public sealed record ActiveOrderSummary(
    Guid Id,
    string OrderNumber,
    string Customer,
    string Store,
    OrderStatus Status,
    DateTime OrderTime,
    decimal Amount,
    string? DeliveryPartner);