using System.Globalization;
using System.Text;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Domain;

namespace QuickCommerce.Application.Services;

/// <summary>The arithmetic of a report, kept apart from the database so every number can be checked by hand.</summary>
public static class ReportMath
{
    public const int MaxDays = 92;
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    /// <summary>Revenue counts orders that reached the customer. Open orders are in progress; the rest are lost.</summary>
    public static bool IsDelivered(OrderStatus status) => status is OrderStatus.Delivered or OrderStatus.Completed;

    public static bool IsInProgress(OrderStatus status) => status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Confirmed or OrderStatus.OutForDelivery;

    public static DateTime StartUtc(DateOnly indiaDay) => DateTime.SpecifyKind(indiaDay.ToDateTime(TimeOnly.MinValue) - India, DateTimeKind.Utc);

    /// <summary>The first moment after the last day of the range, in UTC.</summary>
    public static DateTime EndUtc(DateOnly lastIndiaDay) => StartUtc(lastIndiaDay.AddDays(1));

    public static DateTime ToIndia(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc) + India;

    public static string? Check(DateOnly from, DateOnly to) =>
        to < from ? "The end date is before the start date." : to.DayNumber - from.DayNumber + 1 > MaxDays ? $"Choose at most {MaxDays} days." : null;

    public static ReportTotals Totals(IReadOnlyCollection<ReportOrderRow> rows, int newCustomers)
    {
        var delivered = rows.Where(row => IsDelivered(row.Status)).ToArray();
        var revenue = delivered.Sum(row => row.Total);
        var refunded = rows.Where(row => row.PaymentStatus is PaymentState.Refunded or PaymentState.Refunding).ToArray();
        return new ReportTotals(
            rows.Count,
            delivered.Length,
            rows.Count(row => IsInProgress(row.Status)),
            rows.Count(row => row.Status == OrderStatus.Cancelled),
            rows.Count(row => row.Status == OrderStatus.Rejected),
            revenue,
            delivered.Length == 0 ? 0 : decimal.Round(revenue / delivered.Length, 2),
            delivered.Where(row => row.PaymentMethod == PaymentMethods.CashOnDelivery).Sum(row => row.Total),
            delivered.Where(row => row.PaymentMethod == PaymentMethods.Online).Sum(row => row.Total),
            newCustomers,
            refunded.Length,
            refunded.Sum(row => row.Total));
    }

    /// <summary>One entry for every day of the range, empty days included, so a chart never skips a date.</summary>
    public static IReadOnlyList<ReportDay> Days(IReadOnlyCollection<ReportOrderRow> rows, DateOnly from, DateOnly to)
    {
        var byDay = rows.GroupBy(row => DateOnly.FromDateTime(ToIndia(row.CreatedAt))).ToDictionary(group => group.Key);
        var days = new List<ReportDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (byDay.TryGetValue(day, out var group))
            {
                var delivered = group.Where(row => IsDelivered(row.Status)).ToArray();
                days.Add(new ReportDay(day, group.Count(), delivered.Length, delivered.Sum(row => row.Total)));
            }
            else
            {
                days.Add(new ReportDay(day, 0, 0, 0));
            }
        }

        return days;
    }

    public static IReadOnlyList<ReportHour> Hours(IReadOnlyCollection<ReportOrderRow> rows)
    {
        var counts = rows.GroupBy(row => ToIndia(row.CreatedAt).Hour).ToDictionary(group => group.Key, group => group.Count());
        return Enumerable.Range(0, 24).Select(hour => new ReportHour(hour, counts.GetValueOrDefault(hour))).ToArray();
    }

    public static IReadOnlyList<ReportStoreRow> Stores(IReadOnlyCollection<ReportOrderRow> rows, IReadOnlyDictionary<Guid, string> names) =>
        names.Select(store =>
        {
            var mine = rows.Where(row => row.StoreId == store.Key).ToArray();
            var delivered = mine.Where(row => IsDelivered(row.Status)).ToArray();
            return new ReportStoreRow(store.Key, store.Value, mine.Length, delivered.Length, delivered.Sum(row => row.Total));
        }).OrderByDescending(row => row.Revenue).ThenBy(row => row.Store, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// A cell that starts with =, +, - or @ is read as a formula by spreadsheets; a leading apostrophe stops that. Quotes and commas are escaped.
    /// </summary>
    public static string Csv(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + safe.Replace("\"", "\"\"") + "\"" : safe;
    }

    public static string OrdersCsv(IEnumerable<ReportOrderRow> rows, IReadOnlyDictionary<Guid, string> storeNames)
    {
        var text = new StringBuilder("Order,Placed (India time),Store,Status,Payment method,Payment status,Item total,Delivery fee,Handling fee,Total\r\n");
        foreach (var row in rows.OrderBy(item => item.CreatedAt))
        {
            text.Append(string.Join(',',
                Csv(row.OrderNumber),
                Csv(ToIndia(row.CreatedAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Csv(storeNames.GetValueOrDefault(row.StoreId, "")),
                row.Status,
                row.PaymentMethod,
                row.PaymentStatus,
                row.Subtotal.ToString("0.00", CultureInfo.InvariantCulture),
                row.DeliveryFee.ToString("0.00", CultureInfo.InvariantCulture),
                row.HandlingFee.ToString("0.00", CultureInfo.InvariantCulture),
                row.Total.ToString("0.00", CultureInfo.InvariantCulture))).Append("\r\n");
        }

        return text.ToString();
    }
}

public sealed class ReportService(IReportStore store, ICurrentUserContextResolver contextResolver) : IReportService
{
    /// <summary>Who may look, and at which stores. Null stores = every store of the organization.</summary>
    private async Task<(Guid Organization, IReadOnlyCollection<Guid>? Stores, bool Admin, ReportResult<SalesReport>? Refusal)> ScopeAsync(Guid? requested, CancellationToken cancellationToken)
    {
        var context = await contextResolver.ResolveAsync(cancellationToken);
        if (context is null || context.Role is not (Role.StoreStaff or Role.Admin or Role.ApplicationAdmin))
        {
            return (Guid.Empty, null, false, ReportResult<SalesReport>.Fail(ReportStatus.Forbidden, "Reports are for store staff and administrators."));
        }

        if (context.Role == Role.StoreStaff)
        {
            return context.StoreId is { } own
                ? (context.OrganizationId, [own], false, null)
                : (Guid.Empty, null, false, ReportResult<SalesReport>.Fail(ReportStatus.Forbidden, "Store staff must be assigned to a store."));
        }

        if (requested is { } chosen)
        {
            var known = await store.GetStoreNamesAsync(context.OrganizationId, [chosen], cancellationToken);
            return known.Count == 0
                ? (Guid.Empty, null, true, ReportResult<SalesReport>.Fail(ReportStatus.NotFound, "Store not found."))
                : (context.OrganizationId, [chosen], true, null);
        }

        return (context.OrganizationId, null, true, null);
    }

    public async Task<ReportResult<SalesReport>> GetAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ScopeAsync(storeId, cancellationToken);
        if (scope.Refusal is not null)
        {
            return scope.Refusal;
        }

        if (ReportMath.Check(from, to) is { } problem)
        {
            return ReportResult<SalesReport>.Fail(ReportStatus.Invalid, problem);
        }

        var (organization, stores, admin, _) = scope;
        var fromUtc = ReportMath.StartUtc(from);
        var toUtc = ReportMath.EndUtc(to);
        var rows = (await store.GetOrderRowsAsync(organization, stores, fromUtc, toUtc, cancellationToken)).Where(row => row.Status != OrderStatus.AwaitingPayment).ToArray();
        var names = await store.GetStoreNamesAsync(organization, stores, cancellationToken);
        var report = new SalesReport(
            from,
            to,
            stores?.Single(),
            stores is null ? null : names.Values.SingleOrDefault(),
            ReportMath.Totals(rows, await store.CountNewCustomersAsync(organization, stores, fromUtc, toUtc, cancellationToken)),
            ReportMath.Days(rows, from, to),
            ReportMath.Hours(rows),
            await store.GetTopProductsAsync(organization, stores, fromUtc, toUtc, 10, cancellationToken),
            stores is null ? ReportMath.Stores(rows, names) : [],
            await store.GetLowStockAsync(organization, stores, 50, cancellationToken),
            admin && stores is null ? await store.GetIncomeAsync(organization, fromUtc, toUtc, cancellationToken) : null);
        return ReportResult<SalesReport>.Ok(report);
    }

    public async Task<ReportResult<string>> OrdersCsvAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default)
    {
        var scope = await ScopeAsync(storeId, cancellationToken);
        if (scope.Refusal is not null)
        {
            return ReportResult<string>.Fail(scope.Refusal.Status, scope.Refusal.Message!);
        }

        if (ReportMath.Check(from, to) is { } problem)
        {
            return ReportResult<string>.Fail(ReportStatus.Invalid, problem);
        }

        var rows = (await store.GetOrderRowsAsync(scope.Organization, scope.Stores, ReportMath.StartUtc(from), ReportMath.EndUtc(to), cancellationToken)).Where(row => row.Status != OrderStatus.AwaitingPayment).ToArray();
        return ReportResult<string>.Ok(ReportMath.OrdersCsv(rows, await store.GetStoreNamesAsync(scope.Organization, scope.Stores, cancellationToken)));
    }
}
