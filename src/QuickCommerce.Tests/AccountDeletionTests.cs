using Microsoft.EntityFrameworkCore;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase 9.11: a customer deletes their own account (a Google Play requirement). Personal data goes; order and payment records stay without it.</summary>
public sealed class AccountDeletionTests
{
    private sealed class Identity(bool authenticated, Guid? userId, params string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => userId?.ToString();
        public Guid? OrganizationId => null;
        public Guid? StoreId => null;
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyCollection<string> Permissions => [];
    }

    private sealed class Hashing : IPasswordHashing
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string hash, string password) => hash == "hash:" + password;
    }

    private sealed class Accounts(Guid? existing, string? passwordHash) : IAccountStore
    {
        public Task<AccountUserInfo?> FindActiveCustomerByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(existing == userId ? new AccountUserInfo(userId, "a@example.test", "Asha") : null);

        public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(passwordHash);

        public Task<OrganizationChoice> ResolveRegistrationOrganizationAsync(Guid? configuredOrganizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCreateCustomerAsync(NewCustomerAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUserInfo?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountResetCodesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateResetCodeAsync(Guid userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResetCodeInfo?> GetOpenResetCodeAsync(Guid userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordFailedResetAttemptAsync(Guid codeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryConsumeResetCodeAsync(Guid codeId, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPasswordAsync(Guid userId, string passwordHash, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeSessionsAsync(Guid userId, string? exceptRefreshTokenHash, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid userId, string fullName, string firstName, string lastName, string? phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Deletion(DeleteOutcome outcome) : IAccountDeletionStore
    {
        public List<Guid> Deleted { get; } = [];

        public Task<DeleteOutcome> DeleteCustomerAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            Deleted.Add(userId);
            return Task.FromResult(outcome);
        }
    }

    private static readonly Guid Me = Guid.NewGuid();

    private static (AccountDeletionService Service, Deletion Store) Create(string? passwordHash = "hash:Sunrise-42x", bool known = true, DeleteOutcome outcome = DeleteOutcome.Deleted, Identity? who = null)
    {
        var store = new Deletion(outcome);
        return (new AccountDeletionService(new Accounts(known ? Me : null, passwordHash), store, new Hashing(), who ?? new Identity(true, Me, "Customer"), TimeProvider.System), store);
    }

    [Fact]
    public async Task With_the_right_password_and_the_word_DELETE_the_account_is_deleted()
    {
        var (service, store) = Create();

        var result = await service.DeleteAsync(new DeleteAccountRequest("DELETE", "Sunrise-42x"));

        Assert.Equal(AccountStatus.Succeeded, result.Status);
        Assert.Equal([Me], store.Deleted);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("  Delete  ")]
    public async Task The_word_may_be_typed_in_any_case(string typed)
    {
        var (service, store) = Create();

        Assert.Equal(AccountStatus.Succeeded, (await service.DeleteAsync(new DeleteAccountRequest(typed, "Sunrise-42x"))).Status);
        Assert.Single(store.Deleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("DELET")]
    [InlineData("DELETE ME")]
    public async Task Anything_but_the_word_DELETE_deletes_nothing(string typed)
    {
        var (service, store) = Create();

        Assert.Equal(AccountStatus.InvalidRequest, (await service.DeleteAsync(new DeleteAccountRequest(typed, "Sunrise-42x"))).Status);
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-password")]
    public async Task An_account_with_a_password_needs_that_password(string? password)
    {
        var (service, store) = Create();

        Assert.Equal(AccountStatus.InvalidRequest, (await service.DeleteAsync(new DeleteAccountRequest("DELETE", password))).Status);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public async Task An_account_without_a_password_deletes_without_one()
    {
        var (service, store) = Create(passwordHash: null);

        Assert.Equal(AccountStatus.Succeeded, (await service.DeleteAsync(new DeleteAccountRequest("DELETE"))).Status);
        Assert.Single(store.Deleted);
    }

    [Fact]
    public async Task An_open_order_keeps_the_account_open_and_says_why()
    {
        var (service, _) = Create(outcome: DeleteOutcome.HasOpenOrders);

        var result = await service.DeleteAsync(new DeleteAccountRequest("DELETE", "Sunrise-42x"));

        Assert.Equal(AccountStatus.Conflict, result.Status);
        Assert.Contains("open orders", result.Message!);
    }

    [Fact]
    public async Task An_account_that_is_gone_is_not_found()
    {
        var (service, store) = Create(known: false);

        Assert.Equal(AccountStatus.NotFound, (await service.DeleteAsync(new DeleteAccountRequest("DELETE", "Sunrise-42x"))).Status);
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(false, "Customer")]
    [InlineData(true, "StoreStaff")]
    [InlineData(true, "Admin")]
    [InlineData(true, "DeliveryPartner")]
    public async Task Only_a_signed_in_customer_can_delete_and_only_their_own_account(bool authenticated, string role)
    {
        var (service, store) = Create(who: new Identity(authenticated, Me, role));

        Assert.Equal(AccountStatus.Unauthorized, (await service.DeleteAsync(new DeleteAccountRequest("DELETE", "Sunrise-42x"))).Status);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public async Task A_token_without_a_valid_user_id_deletes_nothing()
    {
        var (service, store) = Create(who: new Identity(true, null, "Customer"));

        Assert.Equal(AccountStatus.Unauthorized, (await service.DeleteAsync(new DeleteAccountRequest("DELETE", "Sunrise-42x"))).Status);
        Assert.Empty(store.Deleted);
    }
}

[Collection("SqlServer")]
public sealed class AccountDeletionEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static async Task<(Guid UserId, Guid CustomerId)> IdsAsync(CheckoutEfIntegrationTests.World world, int index)
    {
        await using var db = world.Db();
        var userId = await db.Users.Where(user => user.ExternalSubject == world.Emails[index]).Select(user => user.Id).SingleAsync();
        return (userId, await db.Customers.Where(customer => customer.UserId == userId).Select(customer => customer.Id).SingleAsync());
    }

    private static async Task<Guid> AddOrderAsync(CheckoutEfIntegrationTests.World world, Guid userId, OrderStatus status, PaymentState payment = PaymentState.NotRequired, string number = "")
    {
        await using var db = world.Db();
        var order = new Order
        {
            OrderNumber = string.IsNullOrEmpty(number) ? "DEL-" + Guid.NewGuid().ToString("N")[..10] : number,
            UserId = userId,
            StoreId = world.Store.Id,
            TotalAmount = 99,
            Status = status,
            PaymentStatus = payment,
            DeliveryAddress = "Flat 4, Sector 20, Navi Mumbai",
            Latitude = 19.03,
            Longitude = 73.06,
            ReceiverName = "Asha Patil",
            ReceiverPhone = "9876543210",
            DeliveryOtp = "4821"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task SeedPersonalDataAsync(CheckoutEfIntegrationTests.World world, Guid userId, Guid customerId, string phone)
    {
        await using var db = world.Db();
        await db.Users.Where(user => user.Id == userId).ExecuteUpdateAsync(set => set.SetProperty(user => user.PhoneNumber, phone).SetProperty(user => user.PhoneVerifiedAt, DateTime.UtcNow).SetProperty(user => user.GoogleSubject, "g-" + userId.ToString("N")[..12]));
        db.CustomerAddresses.Add(new CustomerAddress { CustomerId = customerId, Label = "Home", Line = "Sector 20", ReceiverName = "Asha Patil", ReceiverPhone = "9876543210", IsDefault = true });
        db.Notifications.Add(new Notification { CustomerId = customerId, Type = NotificationTypes.OrderPlaced, Title = "Order placed", Message = "Your order was placed." });
        db.RefreshTokens.Add(new RefreshToken { UserId = userId, SessionId = Guid.NewGuid(), TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTime.UtcNow.AddDays(1) });
        db.OtpChallenges.Add(new OtpChallenge { Phone = phone, Purpose = "SignIn", CodeHash = new string('a', 64), CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Deleting_erases_the_person_keeps_the_orders_and_leaves_other_customers_alone()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 5);
        var (userId, customerId) = await IdsAsync(world, 0);
        var (otherUser, otherCustomer) = await IdsAsync(world, 1);
        var phone = "+9198" + Random.Shared.Next(10_000_000, 99_999_999);
        await SeedPersonalDataAsync(world, userId, customerId, phone);
        await SeedPersonalDataAsync(world, otherUser, otherCustomer, "+9197" + Random.Shared.Next(10_000_000, 99_999_999));
        var done = await AddOrderAsync(world, userId, OrderStatus.Delivered);
        var othersOrder = await AddOrderAsync(world, otherUser, OrderStatus.Pending);

        DeleteOutcome outcome;
        await using (var db = world.Db())
        {
            outcome = await new EfAccountStore(db).DeleteCustomerAsync(userId, DateTime.UtcNow);
        }

        Assert.Equal(DeleteOutcome.Deleted, outcome);
        await using var verify = world.Db();
        var user = await verify.Users.AsNoTracking().Include(item => item.Customer).SingleAsync(item => item.Id == userId);
        Assert.Equal(("Deleted customer", false, false, null, null, null, null), (user.DisplayName, user.IsActive, user.Customer!.IsActive, user.Email, user.PhoneNumber, user.GoogleSubject, user.PhoneVerifiedAt as DateTime?));
        Assert.StartsWith("deleted:", user.ExternalSubject);
        Assert.False(await verify.UserCredentials.AnyAsync(item => item.UserId == userId));
        Assert.False(await verify.UserRoles.AnyAsync(item => item.UserId == userId));
        Assert.False(await verify.RefreshTokens.AnyAsync(item => item.UserId == userId));
        Assert.False(await verify.CustomerAddresses.AnyAsync(item => item.CustomerId == customerId));
        Assert.False(await verify.Notifications.AnyAsync(item => item.CustomerId == customerId));
        Assert.False(await verify.OtpChallenges.AnyAsync(item => item.Phone == phone));

        var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == done);
        Assert.Equal((99m, OrderStatus.Delivered, "Address removed", null, null, null, 0d, 0d), (order.TotalAmount, order.Status, order.DeliveryAddress, order.ReceiverName, order.ReceiverPhone, order.DeliveryOtp, order.Latitude, order.Longitude));

        // The other customer keeps everything.
        Assert.True(await verify.CustomerAddresses.AnyAsync(item => item.CustomerId == otherCustomer));
        Assert.True(await verify.Notifications.AnyAsync(item => item.CustomerId == otherCustomer));
        Assert.True(await verify.RefreshTokens.AnyAsync(item => item.UserId == otherUser));
        var othersRow = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == othersOrder);
        Assert.Equal(("Asha Patil", "9876543210"), (othersRow.ReceiverName, othersRow.ReceiverPhone));
        Assert.True((await verify.Users.AsNoTracking().SingleAsync(item => item.Id == otherUser)).IsActive);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, PaymentState.NotRequired)]
    [InlineData(OrderStatus.Accepted, PaymentState.NotRequired)]
    [InlineData(OrderStatus.Preparing, PaymentState.NotRequired)]
    [InlineData(OrderStatus.Ready, PaymentState.NotRequired)]
    [InlineData(OrderStatus.OutForDelivery, PaymentState.NotRequired)]
    [InlineData(OrderStatus.AwaitingPayment, PaymentState.Created)]
    [InlineData(OrderStatus.Cancelled, PaymentState.Refunding)]
    public async Task An_open_order_or_a_refund_on_its_way_blocks_deletion_and_nothing_changes(OrderStatus status, PaymentState payment)
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (userId, customerId) = await IdsAsync(world, 0);
        await SeedPersonalDataAsync(world, userId, customerId, "+9198" + Random.Shared.Next(10_000_000, 99_999_999));
        var orderId = await AddOrderAsync(world, userId, status, payment);

        await using (var db = world.Db())
        {
            Assert.Equal(DeleteOutcome.HasOpenOrders, await new EfAccountStore(db).DeleteCustomerAsync(userId, DateTime.UtcNow));
        }

        await using var verify = world.Db();
        var user = await verify.Users.AsNoTracking().SingleAsync(item => item.Id == userId);
        Assert.True(user.IsActive);
        Assert.Equal(world.Emails[0], user.ExternalSubject);
        Assert.True(await verify.CustomerAddresses.AnyAsync(item => item.CustomerId == customerId));
        Assert.Equal("Asha Patil", (await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == orderId)).ReceiverName);
    }

    [Fact]
    public async Task Deleting_twice_or_a_made_up_user_is_not_found_and_the_freed_number_can_be_verified_by_someone_else()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 5);
        var (userId, customerId) = await IdsAsync(world, 0);
        var (otherUser, _) = await IdsAsync(world, 1);
        var phone = "+9198" + Random.Shared.Next(10_000_000, 99_999_999);
        await SeedPersonalDataAsync(world, userId, customerId, phone);

        await using (var db = world.Db())
        {
            Assert.Equal(DeleteOutcome.Deleted, await new EfAccountStore(db).DeleteCustomerAsync(userId, DateTime.UtcNow));
        }

        await using (var db = world.Db())
        {
            Assert.Equal(DeleteOutcome.NotFound, await new EfAccountStore(db).DeleteCustomerAsync(userId, DateTime.UtcNow));
            Assert.Equal(DeleteOutcome.NotFound, await new EfAccountStore(db).DeleteCustomerAsync(Guid.NewGuid(), DateTime.UtcNow));
            Assert.True(await new EfIdentityStore(db).TryMarkPhoneVerifiedAsync(otherUser, phone, DateTime.UtcNow));
            Assert.Null(await new EfIdentityStore(db).FindActiveCustomerByVerifiedPhoneAsync("+910000000000"));
        }

        await using var verify = world.Db();
        Assert.Null(await new EfAccountStore(verify).GetProfileAsync(userId));
        await verify.Users.Where(user => user.Id == otherUser).ExecuteUpdateAsync(set => set.SetProperty(user => user.PhoneNumber, (string?)null).SetProperty(user => user.PhoneVerifiedAt, (DateTime?)null));
    }

    [Fact]
    public async Task A_deleted_account_cannot_sign_in_with_its_old_password_or_email()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var (userId, _) = await IdsAsync(world, 0);
        await using (var db = world.Db())
        {
            Assert.Equal(DeleteOutcome.Deleted, await new EfAccountStore(db).DeleteCustomerAsync(userId, DateTime.UtcNow));
        }

        await using var verify = world.Db();
        Assert.False(await verify.Users.AnyAsync(user => user.ExternalSubject == world.Emails[0] || user.Email == world.Emails[0]));
        Assert.Null(await new EfAccountStore(verify).FindActiveCustomerByEmailAsync(world.Emails[0].ToLowerInvariant()));
        Assert.Null(await new EfAccountStore(verify).FindActiveCustomerByIdAsync(userId));
        Assert.Null(await new EfAccountStore(verify).GetPasswordHashAsync(userId));
    }
}
