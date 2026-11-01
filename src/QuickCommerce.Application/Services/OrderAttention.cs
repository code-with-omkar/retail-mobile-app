using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>
/// The order a store's order list is shown in: what needs the staff first. A new order (Pending) comes before one already accepted, then packing,
/// ready, on its way, and last everything that is finished. Within each group the newest comes first.
/// The SQL query repeats these numbers; a test keeps the two in step.
/// </summary>
public static class OrderAttention
{
    public static int Rank(OrderStatus status) => status switch
    {
        OrderStatus.Pending => 0,
        OrderStatus.Accepted => 1,
        OrderStatus.Preparing => 2,
        OrderStatus.Ready => 3,
        OrderStatus.OutForDelivery or OrderStatus.Confirmed => 4,
        _ => 5
    };
}
