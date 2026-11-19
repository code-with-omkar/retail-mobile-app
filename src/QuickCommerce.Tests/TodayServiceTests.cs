using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Who may open the Today screen and for which store, with the store and slot data faked (no database).</summary>
public sealed class TodayServiceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid MyStore = Guid.NewGuid();
    private static readonly Guid OtherStore = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc);

    private sealed class FakeNow : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeToday : ITodayStore
    {
        public List<Guid> Asked { get; } = [];

        public Task<Store?> GetStoreAsync(Guid organizationId, Guid storeId, CancellationToken cancellationToken = default)
        {
            Asked.Add(storeId);
            return Task.FromResult<Store?>(organizationId == Org && (storeId == MyStore || storeId == OtherStore)
                ? new Store { Id = storeId, Name = storeId == MyStore ? "Mine" : "Other", Address = "x", OrganizationId = Org }
                : null);
        }

        public Task<IReadOnlyList<TodayOrderRow>> GetOrderRowsAsync(Guid storeId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TodayOrderRow>>([new TodayOrderRow(Guid.NewGuid(), "T-1", OrderStatus.Pending, Now.AddMinutes(-12), Now.AddMinutes(-12), null, false, null, 80, null, null)]);

        public Task<IReadOnlyList<TodayStockRow>> GetLowStockAsync(Guid storeId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TodayStockRow>>([new TodayStockRow("Milk", "1 l", 0, 5)]);
    }

    private sealed class FakeSlots : ISlotStore
    {
        public Task<DeliverySlotSetting?> GetDefaultAsync(CancellationToken cancellationToken = default) => Task.FromResult<DeliverySlotSetting?>(null);
        public Task<IReadOnlyDictionary<Guid, DeliverySlotSetting>> GetOverridesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, DeliverySlotSetting>>(new Dictionary<Guid, DeliverySlotSetting>());
        public Task SetAsync(Guid? storeId, SlotSettings? settings, string? by, DateTime now, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<DateTime, int>> GetBookedAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<DateTime, int>>(new Dictionary<DateTime, int>());
        public Task<IReadOnlyList<Store>> GetStoresAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Store>>([]);
    }

    private sealed class Who(UserContext? context) : ICurrentUserContextResolver
    {
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(context);
    }

    private static (TodayService Service, FakeToday Store) Make(UserContext? who)
    {
        var store = new FakeToday();
        return (new TodayService(store, new FakeSlots(), new Who(who), new FakeNow()), store);
    }

    private static UserContext Staff(Guid? store) => new(Guid.NewGuid(), Org, store, Role.StoreStaff);
    private static UserContext Admin => new(Guid.NewGuid(), Org, null, Role.Admin);

    [Fact]
    public async Task A_customer_or_a_signed_out_caller_is_refused()
    {
        Assert.Equal(TodayStatus.Forbidden, (await Make(new UserContext(Guid.NewGuid(), Org, null, Role.Customer)).Service.GetAsync(MyStore)).Status);
        Assert.Equal(TodayStatus.Forbidden, (await Make(new UserContext(Guid.NewGuid(), Org, null, Role.DeliveryPartner)).Service.GetAsync(MyStore)).Status);
        Assert.Equal(TodayStatus.Forbidden, (await Make(null).Service.GetAsync(MyStore)).Status);
    }

    [Fact]
    public async Task Store_staff_see_their_own_store_whatever_store_they_ask_for()
    {
        var (service, store) = Make(Staff(MyStore));

        var result = await service.GetAsync(OtherStore);

        Assert.Equal(TodayStatus.Succeeded, result.Status);
        Assert.Equal(MyStore, result.Value!.StoreId);
        Assert.Equal("Mine", result.Value.StoreName);
        Assert.Equal([MyStore], store.Asked);
    }

    [Fact]
    public async Task Store_staff_without_a_store_are_refused()
    {
        Assert.Equal(TodayStatus.Forbidden, (await Make(Staff(null)).Service.GetAsync(null)).Status);
    }

    [Fact]
    public async Task An_administrator_must_name_a_store_and_it_must_exist_in_their_organisation()
    {
        var (service, _) = Make(Admin);

        Assert.Equal(TodayStatus.Invalid, (await service.GetAsync(null)).Status);
        Assert.Equal(TodayStatus.NotFound, (await service.GetAsync(Guid.NewGuid())).Status);
        var ok = await service.GetAsync(OtherStore);
        Assert.Equal(TodayStatus.Succeeded, ok.Status);
        Assert.Equal("Other", ok.Value!.StoreName);
    }

    [Fact]
    public async Task The_summary_carries_the_counts_and_the_alerts_built_from_the_stores_data()
    {
        var value = (await Make(Staff(MyStore)).Service.GetAsync(null)).Value!;

        Assert.Equal(1, value.Counts.New);
        Assert.Equal(Now, value.AsOf);
        Assert.Contains(value.Alerts, alert => alert.Kind == "PendingTooLong" && alert.Severity == "urgent");
        Assert.Contains(value.Alerts, alert => alert.Kind == "OutOfStock");
    }
}
