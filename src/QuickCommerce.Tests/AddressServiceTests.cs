using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Saved addresses: ownership, the one-default rule, the limit, validation and the serviceability flag. The EF behaviour (parallel requests, indexes) is in AddressEfIntegrationTests.</summary>
public sealed class AddressServiceTests
{
    // Harbor Point Dark Store (seed data) is at 19.076, 72.8777 with an 8 km radius.
    private const double NearHarborLat = 19.076, NearHarborLng = 72.8777;
    private const double DelhiLat = 28.61, DelhiLng = 77.21;

    private sealed class Harness
    {
        public InMemoryCommerceStore Data { get; } = InMemoryCommerceStore.CreateSeeded();
        public FakeAddressStore Store { get; } = new();
        public FakeUser User { get; } = new();
        public Clock Time { get; } = new();
        public AddressSettings Settings { get; } = new();
        public AddressService Service { get; }
        public Guid AshaUser { get; } = Guid.NewGuid();
        public Guid BhaveshUser { get; } = Guid.NewGuid();

        public Harness()
        {
            var organization = Data.Organizations.Single().Id;
            Store.AddOwner(AshaUser, organization);
            Store.AddOwner(BhaveshUser, organization);
            Service = new AddressService(Store, new ServiceabilityService(Data), User, new CustomerAddressRequestValidator(), Settings, Time);
            User.SignIn(AshaUser);
        }

        public Task<AccountResult<CustomerAddressResponse>> Add(string label = "Home", double lat = NearHarborLat, double lng = NearHarborLng) =>
            Service.CreateAsync(Request(label, lat, lng));
    }

    private static CustomerAddressRequest Request(string label = "Home", double lat = NearHarborLat, double lng = NearHarborLng) =>
        new(label, "12 Marine Drive, Mumbai", "Flat 4", "Near the station", lat, lng, "Asha Patil", "98765 43210");

    // ---------- who may call ----------

    [Fact]
    public async Task Everything_needs_a_signed_in_customer()
    {
        var h = new Harness();
        h.User.SignOut();

        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.ListAsync()).Status);
        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.CreateAsync(Request())).Status);
        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.UpdateAsync(Guid.NewGuid(), Request())).Status);
        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.SetDefaultAsync(Guid.NewGuid())).Status);
        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.DeleteAsync(Guid.NewGuid())).Status);
        Assert.Empty(h.Store.All);
    }

    [Fact]
    public async Task A_user_who_is_not_an_active_customer_is_refused()
    {
        var h = new Harness();
        h.User.SignIn(Guid.NewGuid()); // a signed-in staff member or an inactive customer has no owner row

        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.ListAsync()).Status);
        Assert.Equal(AccountStatus.Unauthorized, (await h.Service.CreateAsync(Request())).Status);
    }

    // ---------- create ----------

    [Fact]
    public async Task The_first_address_becomes_the_default_and_the_next_ones_do_not()
    {
        var h = new Harness();

        var first = await h.Add("Home");
        var second = await h.Add("Work");

        Assert.True(first.Value!.IsDefault);
        Assert.False(second.Value!.IsDefault);
        Assert.Single(h.Store.All, address => address.IsDefault);
    }

    [Fact]
    public async Task Text_is_trimmed_and_the_phone_is_stored_normalized()
    {
        var h = new Harness();

        var created = (await h.Service.CreateAsync(new CustomerAddressRequest("  Home  ", "  12 Marine Drive  ", null, "  ", NearHarborLat, NearHarborLng, "  Asha  ", "+91 98765-43210"))).Value!;

        Assert.Equal(("Home", "12 Marine Drive", "", "", "Asha"), (created.Label, created.Line, created.FlatOrBuilding, created.Landmark, created.ReceiverName));
        Assert.Equal("+919876543210", created.ReceiverPhone);
    }

    [Theory]
    [InlineData("", "Line", "Asha", "9876543210", 19.0, 72.0, "label")]
    [InlineData("Home", "", "Asha", "9876543210", 19.0, 72.0, "address")]
    [InlineData("Home", "Line", "", "9876543210", 19.0, 72.0, "receive")]
    [InlineData("Home", "Line", "Asha", "12345", 19.0, 72.0, "phone")]
    [InlineData("Home", "Line", "Asha", "9876543210", 91.0, 72.0, "Latitude")]
    [InlineData("Home", "Line", "Asha", "9876543210", 19.0, 181.0, "Longitude")]
    [InlineData("Home", "Line", "Asha", "9876543210", 0.0, 0.0, "map")]
    [InlineData("Home", "Line", "Asha", "9876543210", double.NaN, 72.0, "Latitude")]
    public async Task Invalid_addresses_are_rejected_with_a_message_and_nothing_is_stored(string label, string line, string receiver, string phone, double lat, double lng, string messagePart)
    {
        var h = new Harness();

        var result = await h.Service.CreateAsync(new CustomerAddressRequest(label, line, null, null, lat, lng, receiver, phone));

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
        Assert.Contains(messagePart, result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Store.All);
    }

    [Fact]
    public async Task Over_long_fields_are_rejected()
    {
        var h = new Harness();

        Assert.Equal(AccountStatus.InvalidRequest, (await h.Service.CreateAsync(Request() with { Label = new string('a', 41) })).Status);
        Assert.Equal(AccountStatus.InvalidRequest, (await h.Service.CreateAsync(Request() with { Line = new string('a', 301) })).Status);
        Assert.Equal(AccountStatus.InvalidRequest, (await h.Service.CreateAsync(Request() with { FlatOrBuilding = new string('a', 121) })).Status);
        Assert.Equal(AccountStatus.InvalidRequest, (await h.Service.CreateAsync(Request() with { Landmark = new string('a', 121) })).Status);
        Assert.Equal(AccountStatus.InvalidRequest, (await h.Service.CreateAsync(Request() with { ReceiverName = new string('a', 121) })).Status);
        Assert.Empty(h.Store.All);
    }

    [Fact]
    public async Task A_customer_cannot_save_more_than_the_limit()
    {
        var h = new Harness();
        h.Settings.MaxPerCustomer = 3;
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await h.Add($"Place {i}")).Succeeded);
        }

        var over = await h.Add("One too many");

        Assert.Equal(AccountStatus.Conflict, over.Status);
        Assert.Contains("3", over.Message!);
        Assert.Equal(3, h.Store.All.Count);
    }

    [Fact]
    public async Task The_limit_is_per_customer()
    {
        var h = new Harness();
        h.Settings.MaxPerCustomer = 1;
        await h.Add("Mine");

        h.User.SignIn(h.BhaveshUser);

        Assert.True((await h.Add("Theirs")).Succeeded);
    }

    // ---------- list and serviceability ----------

    [Fact]
    public async Task The_list_has_the_default_first_then_the_most_recently_changed()
    {
        var h = new Harness();
        await h.Add("Home");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var work = (await h.Add("Work")).Value!;
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.Add("Gym");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.Service.UpdateAsync(work.Id, Request("Office"));

        var labels = (await h.Service.ListAsync()).Value!.Select(address => address.Label).ToArray();

        Assert.Equal(["Home", "Office", "Gym"], labels);
    }

    [Fact]
    public async Task Each_address_says_whether_it_can_be_delivered_to_and_why_not()
    {
        var h = new Harness();
        await h.Add("Near", NearHarborLat, NearHarborLng);
        await h.Add("Delhi", DelhiLat, DelhiLng);
        // A point a store's radius covers, but where no store carries anything.
        foreach (var store in h.Data.Stores)
        {
            h.Data.Inventory.RemoveAll(row => row.StoreId == store.Id);
        }

        var emptied = (await h.Service.ListAsync()).Value!.ToDictionary(address => address.Label);
        Assert.Equal((false, ServiceabilityReasons.NoStoreAvailable), (emptied["Near"].Serviceable, emptied["Near"].ServiceabilityReason));
        Assert.Equal((false, ServiceabilityReasons.OutsideServiceArea), (emptied["Delhi"].Serviceable, emptied["Delhi"].ServiceabilityReason));

        var seeded = new Harness();
        await seeded.Add("Near", NearHarborLat, NearHarborLng);
        await seeded.Add("Delhi", DelhiLat, DelhiLng);
        var normal = (await seeded.Service.ListAsync()).Value!.ToDictionary(address => address.Label);
        Assert.Equal((true, ServiceabilityReasons.Serviceable), (normal["Near"].Serviceable, normal["Near"].ServiceabilityReason));
        Assert.False(normal["Delhi"].Serviceable);
    }

    [Fact]
    public async Task Serviceability_follows_todays_stores_not_the_day_the_address_was_saved()
    {
        var h = new Harness();
        var saved = (await h.Add("Near")).Value!;
        Assert.True(saved.Serviceable);

        foreach (var store in h.Data.Stores)
        {
            store.IsActive = false;
        }

        Assert.False((await h.Service.ListAsync()).Value!.Single().Serviceable);
    }

    // ---------- update ----------

    [Fact]
    public async Task Update_changes_the_fields_but_not_the_id_or_the_default_flag()
    {
        var h = new Harness();
        var created = (await h.Add("Home")).Value!;
        h.Time.Advance(TimeSpan.FromHours(1));

        var updated = (await h.Service.UpdateAsync(created.Id, new CustomerAddressRequest("Parents", "5 Hill Road", "B-2", "Opp. bakery", 19.08, 72.88, "Mr Patil", "9123456789"))).Value!;

        Assert.Equal((created.Id, true), (updated.Id, updated.IsDefault));
        Assert.Equal(("Parents", "5 Hill Road", "B-2", "Opp. bakery", 19.08, 72.88, "Mr Patil", "9123456789"), (updated.Label, updated.Line, updated.FlatOrBuilding, updated.Landmark, updated.Latitude, updated.Longitude, updated.ReceiverName, updated.ReceiverPhone));
        Assert.Equal(h.Time.GetUtcNow().UtcDateTime, h.Store.All.Single().UpdatedAt);
    }

    [Fact]
    public async Task An_invalid_update_changes_nothing()
    {
        var h = new Harness();
        var created = (await h.Add("Home")).Value!;

        var result = await h.Service.UpdateAsync(created.Id, Request("Home") with { ReceiverPhone = "nope" });

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
        Assert.Equal("9876543210", h.Store.All.Single().ReceiverPhone);
    }

    // ---------- default ----------

    [Fact]
    public async Task Making_another_address_the_default_leaves_exactly_one_default()
    {
        var h = new Harness();
        await h.Add("Home");
        var work = (await h.Add("Work")).Value!;
        await h.Add("Gym");

        var result = await h.Service.SetDefaultAsync(work.Id);

        Assert.True(result.Value!.IsDefault);
        Assert.Equal([work.Id], h.Store.All.Where(address => address.IsDefault).Select(address => address.Id));
        Assert.Equal("Work", (await h.Service.ListAsync()).Value![0].Label);
    }

    // ---------- delete ----------

    [Fact]
    public async Task Deleting_the_default_promotes_the_most_recently_changed_remaining_address()
    {
        var h = new Harness();
        var home = (await h.Add("Home")).Value!;
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.Add("Work");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.Add("Gym");

        var result = await h.Service.DeleteAsync(home.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, h.Store.All.Count);
        Assert.Equal("Gym", h.Store.All.Single(address => address.IsDefault).Label);
    }

    [Fact]
    public async Task Deleting_the_only_address_leaves_none_and_deleting_a_non_default_keeps_the_default()
    {
        var h = new Harness();
        var home = (await h.Add("Home")).Value!;
        var work = (await h.Add("Work")).Value!;

        await h.Service.DeleteAsync(work.Id);
        Assert.Equal(home.Id, h.Store.All.Single(address => address.IsDefault).Id);

        await h.Service.DeleteAsync(home.Id);
        Assert.Empty(h.Store.All);
    }

    [Fact]
    public async Task Deleting_twice_is_not_found_the_second_time()
    {
        var h = new Harness();
        var home = (await h.Add("Home")).Value!;

        Assert.True((await h.Service.DeleteAsync(home.Id)).Succeeded);
        Assert.Equal(AccountStatus.NotFound, (await h.Service.DeleteAsync(home.Id)).Status);
    }

    // ---------- ownership ----------

    [Fact]
    public async Task Another_customers_address_is_not_found_for_every_operation_and_stays_untouched()
    {
        var h = new Harness();
        var mine = (await h.Add("Mine")).Value!;
        h.User.SignIn(h.BhaveshUser);
        var theirs = (await h.Add("Theirs")).Value!;

        h.User.SignIn(h.AshaUser);
        var before = h.Store.Snapshot();
        Assert.Equal(AccountStatus.NotFound, (await h.Service.UpdateAsync(theirs.Id, Request("Hijack"))).Status);
        Assert.Equal(AccountStatus.NotFound, (await h.Service.SetDefaultAsync(theirs.Id)).Status);
        Assert.Equal(AccountStatus.NotFound, (await h.Service.DeleteAsync(theirs.Id)).Status);
        Assert.Equal(before, h.Store.Snapshot());

        var list = (await h.Service.ListAsync()).Value!;
        Assert.Equal([mine.Id], list.Select(address => address.Id));
    }

    [Fact]
    public async Task A_missing_address_looks_the_same_as_someone_elses()
    {
        var h = new Harness();
        h.User.SignIn(h.BhaveshUser);
        var theirs = (await h.Add("Theirs")).Value!;
        h.User.SignIn(h.AshaUser);

        var someoneElses = await h.Service.DeleteAsync(theirs.Id);
        var missing = await h.Service.DeleteAsync(Guid.NewGuid());

        Assert.Equal((someoneElses.Status, someoneElses.Message), (missing.Status, missing.Message));
    }

    // ---------- fakes ----------

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public bool IsAuthenticated { get; private set; }
        public string? UserId { get; private set; }
        public Guid? OrganizationId => null;
        public Guid? StoreId => null;
        public IReadOnlyCollection<string> Roles => ["Customer"];
        public IReadOnlyCollection<string> Permissions => [];

        public void SignIn(Guid userId)
        {
            IsAuthenticated = true;
            UserId = userId.ToString();
        }

        public void SignOut()
        {
            IsAuthenticated = false;
            UserId = null;
        }
    }

    /// <summary>The same contract as the EF store, in memory: scoped by customer, one default, a limit, promotion on delete.</summary>
    private sealed class FakeAddressStore : IAddressStore
    {
        private readonly Dictionary<Guid, AddressOwner> owners = [];
        public List<CustomerAddress> All { get; } = [];

        public void AddOwner(Guid userId, Guid organizationId) => owners[userId] = new AddressOwner(Guid.NewGuid(), organizationId);

        public Task<AddressOwner?> FindOwnerAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(owners.GetValueOrDefault(userId));

        public Task<IReadOnlyList<CustomerAddress>> ListAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerAddress>>(All.Where(address => address.CustomerId == customerId).OrderByDescending(address => address.IsDefault).ThenByDescending(address => address.UpdatedAt).ToArray());

        public Task<CustomerAddress?> GetAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.FirstOrDefault(address => address.Id == addressId && address.CustomerId == customerId));

        public Task<AddAddressResult> AddAsync(Guid customerId, CustomerAddress address, int maxPerCustomer, CancellationToken cancellationToken = default)
        {
            var count = All.Count(item => item.CustomerId == customerId);
            if (count >= maxPerCustomer)
            {
                return Task.FromResult(new AddAddressResult(null, true));
            }

            address.CustomerId = customerId;
            address.IsDefault = count == 0;
            All.Add(address);
            return Task.FromResult(new AddAddressResult(address, false));
        }

        public Task<CustomerAddress?> UpdateAsync(Guid customerId, Guid addressId, AddressFields fields, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var address = All.FirstOrDefault(item => item.Id == addressId && item.CustomerId == customerId);
            if (address is null)
            {
                return Task.FromResult<CustomerAddress?>(null);
            }

            (address.Label, address.Line, address.FlatOrBuilding, address.Landmark) = (fields.Label, fields.Line, fields.FlatOrBuilding, fields.Landmark);
            (address.Latitude, address.Longitude, address.ReceiverName, address.ReceiverPhone, address.UpdatedAt) = (fields.Latitude, fields.Longitude, fields.ReceiverName, fields.ReceiverPhone, nowUtc);
            return Task.FromResult<CustomerAddress?>(address);
        }

        public Task<bool> SetDefaultAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var address = All.FirstOrDefault(item => item.Id == addressId && item.CustomerId == customerId);
            if (address is null)
            {
                return Task.FromResult(false);
            }

            foreach (var other in All.Where(item => item.CustomerId == customerId && item.IsDefault))
            {
                other.IsDefault = false;
            }

            address.IsDefault = true;
            address.UpdatedAt = nowUtc;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid customerId, Guid addressId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var address = All.FirstOrDefault(item => item.Id == addressId && item.CustomerId == customerId);
            if (address is null)
            {
                return Task.FromResult(false);
            }

            All.Remove(address);
            if (address.IsDefault && All.Where(item => item.CustomerId == customerId).OrderByDescending(item => item.UpdatedAt).FirstOrDefault() is { } next)
            {
                next.IsDefault = true;
                next.UpdatedAt = nowUtc;
            }

            return Task.FromResult(true);
        }

        public string Snapshot() => string.Join("|", All.OrderBy(address => address.Id).Select(address => $"{address.Id}:{address.CustomerId}:{address.Label}:{address.IsDefault}:{address.UpdatedAt:O}"));
    }
}
