using QuickCommerce.Domain;

namespace QuickCommerce.Application.Interfaces;

/// <summary>One order as the reports see it. No customer name, phone or address ever reaches a report.</summary>
public sealed record ReportOrderRow(Guid Id, string OrderNumber, Guid StoreId, Guid UserId, DateTime CreatedAt, OrderStatus Status, string PaymentMethod, PaymentState PaymentStatus, decimal Subtotal, decimal DeliveryFee, decimal HandlingFee, decimal Total, decimal? CommissionAmount = null);

public sealed record ReportTotals(
    int Orders,
    int Delivered,
    int InProgress,
    int Cancelled,
    int Rejected,
    decimal Revenue,
    decimal AverageOrder,
    decimal CashRevenue,
    decimal OnlineRevenue,
    int NewCustomers,
    int Refunds,
    decimal RefundedAmount);

public sealed record ReportDay(DateOnly Date, int Orders, int Delivered, decimal Revenue);

public sealed record ReportHour(int Hour, int Orders);

public sealed record ReportProduct(Guid ProductId, string Name, int Quantity, decimal Revenue);

public sealed record ReportStoreRow(Guid StoreId, string Store, int Orders, int Delivered, decimal Revenue);

/// <summary>What one shop earned us in the period, from delivered orders only: the product sales, our commission on them, the fees the store keeps, and what is left for the store.</summary>
public sealed record EarningsRow(Guid StoreId, string Store, int Orders, decimal Sales, decimal Commission, decimal Fees, decimal Net);

public sealed record LowStockRow(Guid StoreId, string Store, string Product, string Size, int Available, int ReorderThreshold);

/// <summary>What the platform itself took in, in rupees: offer fees and placement bids that were paid, and what went back.</summary>
public sealed record PlatformIncome(decimal OfferFeesPaid, decimal OfferFeesRefunded, decimal PlacementPaid, decimal PlacementRefunded);

public sealed record SalesReport(
    DateOnly From,
    DateOnly To,
    Guid? StoreId,
    string? StoreName,
    ReportTotals Totals,
    IReadOnlyList<ReportDay> Days,
    IReadOnlyList<ReportHour> Hours,
    IReadOnlyList<ReportProduct> TopProducts,
    IReadOnlyList<ReportStoreRow> Stores,
    IReadOnlyList<LowStockRow> LowStock,
    PlatformIncome? Income,
    IReadOnlyList<EarningsRow>? Earnings = null);

public enum ReportStatus { Succeeded, Forbidden, Invalid, NotFound }

public sealed record ReportResult<T>(ReportStatus Status, T? Value = default, string? Message = null)
{
    public static ReportResult<T> Ok(T value) => new(ReportStatus.Succeeded, value);
    public static ReportResult<T> Fail(ReportStatus status, string message) => new(status, default, message);
}

public interface IReportService
{
    /// <summary>Store staff always get their own store. An administrator gets every store, or the one named.</summary>
    Task<ReportResult<SalesReport>> GetAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default);

    /// <summary>The orders of the range as CSV text (order numbers, times, status, money; nothing that identifies a customer).</summary>
    Task<ReportResult<string>> OrdersCsvAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default);
}

/// <summary>The queries behind the reports. <paramref name="storeIds"/> null = every store of the organization.</summary>
public interface IReportStore
{
    Task<IReadOnlyList<ReportOrderRow>> GetOrderRowsAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Best sellers among orders that were delivered or completed in the window, by revenue.</summary>
    Task<IReadOnlyList<ReportProduct>> GetTopProductsAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Customers whose first order from these stores (not counting unpaid holds) was placed inside the window.</summary>
    Task<int> CountNewCustomersAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, int take, CancellationToken cancellationToken = default);
    Task<PlatformIncome> GetIncomeAsync(Guid organizationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> GetStoreNamesAsync(Guid organizationId, IReadOnlyCollection<Guid>? storeIds, CancellationToken cancellationToken = default);
}
