using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>Phase P8: signing in with a text-message code or with Google, and verifying a phone number.</summary>
public sealed class SignInTests
{
    private sealed class FakeUser
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string? Phone { get; set; }
        public bool PhoneVerified { get; set; }
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? GoogleSubject { get; set; }
        public bool HasPassword { get; set; }
        public bool Sessions { get; set; } = true;
        public bool IsCustomer { get; init; } = true;
    }

    private sealed class FakeStore : IIdentityStore
    {
        public List<FakeUser> Users { get; } = [];
        public List<(Guid Id, string Phone, string Purpose, Guid? UserId, string Hash, DateTime Created, DateTime Expires, int Failed, bool Used)> Codes { get; } = [];

        public Task<int> CountOtpRequestsSinceAsync(string phone, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Codes.Count(item => item.Phone == phone && item.Created >= sinceUtc));

        public Task<OtpRecord?> GetOpenOtpAsync(string phone, string purpose, Guid? userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default)
        {
            var open = Codes.Where(item => item.Phone == phone && item.Purpose == purpose && item.UserId == userId && !item.Used && item.Expires > nowUtc && item.Failed < maxAttempts).OrderByDescending(item => item.Created).FirstOrDefault();
            return Task.FromResult(open.Id == Guid.Empty ? null : new OtpRecord(open.Id, open.Hash, open.Failed, open.Created));
        }

        public Task CreateOtpAsync(string phone, string purpose, Guid? userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < Codes.Count; i++)
            {
                if (Codes[i].Phone == phone && Codes[i].Purpose == purpose && !Codes[i].Used)
                {
                    Codes[i] = Codes[i] with { Used = true };
                }
            }

            Codes.Add((Guid.NewGuid(), phone, purpose, userId, codeHash, createdUtc, expiresUtc, 0, false));
            return Task.CompletedTask;
        }

        public Task RecordFailedOtpAttemptAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var at = Codes.FindIndex(item => item.Id == id);
            Codes[at] = Codes[at] with { Failed = Codes[at].Failed + 1 };
            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeOtpAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var at = Codes.FindIndex(item => item.Id == id);
            if (Codes[at].Used)
            {
                return Task.FromResult(false);
            }

            Codes[at] = Codes[at] with { Used = true };
            return Task.FromResult(true);
        }

        public Task<Guid?> FindActiveCustomerByVerifiedPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Where(user => user.IsCustomer && user.PhoneVerified && user.Phone == phone).Select(user => (Guid?)user.Id).FirstOrDefault());

        public Task<Guid?> CreatePhoneCustomerAsync(Guid organizationId, string phone, string fullName, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var user = new FakeUser { Phone = phone, PhoneVerified = true, HasPassword = false };
            Users.Add(user);
            return Task.FromResult<Guid?>(user.Id);
        }

        public Task<bool> TryMarkPhoneVerifiedAsync(Guid userId, string phone, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (Users.Any(user => user.PhoneVerified && user.Phone == phone && user.Id != userId))
            {
                return Task.FromResult(false);
            }

            var me = Users.Single(user => user.Id == userId);
            me.Phone = phone;
            me.PhoneVerified = true;
            return Task.FromResult(true);
        }

        private static CustomerIdentity? Identity(FakeUser? user) => user is null ? null : new CustomerIdentity(user.Id, user.EmailVerified, user.HasPassword);

        public Task<CustomerIdentity?> FindActiveCustomerByGoogleAsync(string subject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Identity(Users.FirstOrDefault(user => user.IsCustomer && user.GoogleSubject == subject)));

        public Task<CustomerIdentity?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(Identity(Users.FirstOrDefault(user => user.IsCustomer && user.Email == normalizedEmail)));

        public Task<Guid?> CreateGoogleCustomerAsync(Guid organizationId, string normalizedEmail, string fullName, string subject, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (Users.Any(user => user.Email == normalizedEmail))
            {
                return Task.FromResult<Guid?>(null); // an account that is not a customer's (staff) owns this email
            }

            var user = new FakeUser { Email = normalizedEmail, EmailVerified = true, GoogleSubject = subject };
            Users.Add(user);
            return Task.FromResult<Guid?>(user.Id);
        }

        public Task LinkGoogleAsync(Guid userId, string subject, bool wipeSignIn, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var user = Users.Single(item => item.Id == userId);
            user.GoogleSubject = subject;
            user.EmailVerified = true;
            if (wipeSignIn)
            {
                user.HasPassword = false;
                user.Sessions = false;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class OrganizationOnly(Guid? organization) : IAccountStore
    {
        public Task<OrganizationChoice> ResolveRegistrationOrganizationAsync(Guid? configuredOrganizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(organization is null ? new OrganizationChoice(null, "No organization.") : new OrganizationChoice(organization, null));

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCreateCustomerAsync(NewCustomerAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUserInfo?> FindActiveCustomerByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUserInfo?> FindActiveCustomerByIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountResetCodesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateResetCodeAsync(Guid userId, string codeHash, DateTime createdUtc, DateTime expiresUtc, string? ipAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResetCodeInfo?> GetOpenResetCodeAsync(Guid userId, DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordFailedResetAttemptAsync(Guid codeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryConsumeResetCodeAsync(Guid codeId, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPasswordAsync(Guid userId, string passwordHash, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeSessionsAsync(Guid userId, string? exceptRefreshTokenHash, string? ipAddress, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid userId, string fullName, string firstName, string lastName, string? phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Sender : IOtpSender
    {
        public List<(string Phone, string Code)> Sent { get; } = [];
        public bool Fails { get; set; }

        public Task SendAsync(string phone, string code, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("provider down");
            }

            Sent.Add((phone, code));
            return Task.CompletedTask;
        }

        public string LastCode => Sent[^1].Code;
    }

    private sealed class Google : IGoogleTokenValidator
    {
        public bool IsConfigured { get; set; } = true;
        public Dictionary<string, GoogleIdentity> Tokens { get; } = [];
        public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default) => Task.FromResult(Tokens.GetValueOrDefault(idToken));
    }

    private sealed class Issuer(FakeStore store) : ISessionIssuer
    {
        public List<Guid> Issued { get; } = [];

        public Task<AuthenticationResult> IssueAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken = default)
        {
            Issued.Add(userId);
            var user = store.Users.Single(item => item.Id == userId);
            user.Sessions = true;
            var response = new AuthenticationResponse("access-" + userId.ToString("N"), "refresh", 900, "Bearer", new AuthenticationUserResponse(userId, "Customer", user.Email, Guid.NewGuid(), ["Customer"], []));
            return Task.FromResult(AuthenticationResult.Success(response));
        }
    }

    private sealed class FixedUser(UserContext? context) : ICurrentUserContextResolver
    {
        public UserContext? Context { get; set; } = context;
        public Task<UserContext?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Context);
    }

    private sealed class Rig
    {
        public required FakeStore Store { get; init; }
        public required Sender Sender { get; init; }
        public required Google Google { get; init; }
        public required Issuer Issuer { get; init; }
        public required FixedUser Who { get; init; }
        public required TestClock Clock { get; init; }
        public required SignInService Service { get; init; }
        public required Guid Organization { get; init; }
    }

    private const string Phone = "9876543210";
    private const string Plus = "+919876543210";

    private static Rig Create(Guid? organization = null, Action<OtpSettings>? tune = null, bool noOrganization = false)
    {
        var store = new FakeStore();
        var org = organization ?? Guid.NewGuid();
        var sender = new Sender();
        var google = new Google();
        var issuer = new Issuer(store);
        var who = new FixedUser(null);
        var clock = new TestClock(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
        var otp = new OtpSettings();
        tune?.Invoke(otp);
        var service = new SignInService(store, new OrganizationOnly(noOrganization ? null : org), sender, google, issuer, who, otp, new AccountSettings { ResetCodeKey = new string('k', 40) }, new RegistrationSettings(), clock, NullLogger<SignInService>.Instance);
        return new Rig { Store = store, Sender = sender, Google = google, Issuer = issuer, Who = who, Clock = clock, Service = service, Organization = org };
    }

    private static async Task<string> Ask(Rig rig, string phone = Phone)
    {
        var sent = await rig.Service.RequestOtpAsync(new OtpRequest(phone), "1.2.3.4");
        Assert.Equal(SignInStatus.Succeeded, sent.Status);
        return rig.Sender.LastCode;
    }

    private static string Wrong(string right) => right == "000000" ? "111111" : "000000";

    // ---------------- phone numbers ----------------

    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("98765 43210", "+919876543210")]
    [InlineData("98765-43210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("09876543210", "+919876543210")]
    [InlineData("(+91) 6000000000", "+916000000000")]
    [InlineData("5876543210", null)]
    [InlineData("987654321", null)]
    [InlineData("98765432100", null)]
    [InlineData("+14155552671", null)]
    [InlineData("+449876543210", null)]
    [InlineData("abcdefghij", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Indian_mobile_numbers_are_read_in_every_usual_form_and_nothing_else(string? raw, string? expected) => Assert.Equal(expected, Phones.ToE164(raw));

    // ---------------- asking for a code ----------------

    [Fact]
    public async Task A_code_is_texted_to_the_number_in_international_form_and_only_its_hash_is_kept()
    {
        var rig = Create();

        var sent = await rig.Service.RequestOtpAsync(new OtpRequest("98765 43210"), "1.2.3.4");

        Assert.Equal((SignInStatus.Succeeded, 30, 300), (sent.Status, sent.Value!.ResendAfterSeconds, sent.Value.ExpiresInSeconds));
        Assert.Equal(Plus, Assert.Single(rig.Sender.Sent).Phone);
        Assert.Matches("^[0-9]{6}$", rig.Sender.LastCode);
        Assert.DoesNotContain(rig.Sender.LastCode, rig.Store.Codes.Single().Hash);
        Assert.Equal(64, rig.Store.Codes.Single().Hash.Length);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("+14155552671")]
    public async Task A_number_that_is_not_an_indian_mobile_is_refused_and_nothing_is_sent(string phone)
    {
        var rig = Create();

        Assert.Equal(SignInStatus.Invalid, (await rig.Service.RequestOtpAsync(new OtpRequest(phone), null)).Status);
        Assert.Empty(rig.Sender.Sent);
        Assert.Empty(rig.Store.Codes);
    }

    [Fact]
    public async Task The_answer_is_the_same_for_a_number_with_an_account_and_one_without()
    {
        var rig = Create();
        rig.Store.Users.Add(new FakeUser { Phone = Plus, PhoneVerified = true });

        var known = await rig.Service.RequestOtpAsync(new OtpRequest(Phone), null);
        var unknown = await rig.Service.RequestOtpAsync(new OtpRequest("9123456780"), null);

        Assert.Equal((known.Status, known.Value), (unknown.Status, unknown.Value));
    }

    [Fact]
    public async Task Asking_again_too_soon_is_refused_with_how_long_to_wait_and_after_that_a_new_code_replaces_the_old()
    {
        var rig = Create();
        var first = await Ask(rig);

        var early = await rig.Service.RequestOtpAsync(new OtpRequest(Phone), null);
        Assert.Equal((SignInStatus.TooManyRequests, 30), (early.Status, early.RetryAfterSeconds));
        Assert.Single(rig.Sender.Sent);

        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        var second = await Ask(rig);
        Assert.Equal(2, rig.Sender.Sent.Count);

        if (first != second)
        {
            Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, first), null)).Status);
        }

        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, second), null)).Status);
    }

    [Fact]
    public async Task No_more_than_five_codes_an_hour_per_number_and_the_limit_lifts_after_the_hour()
    {
        var rig = Create();
        for (var i = 0; i < 5; i++)
        {
            await Ask(rig);
            rig.Clock.Advance(TimeSpan.FromSeconds(31));
        }

        var sixth = await rig.Service.RequestOtpAsync(new OtpRequest(Phone), null);
        Assert.Equal(SignInStatus.TooManyRequests, sixth.Status);
        Assert.Equal(5, rig.Sender.Sent.Count);

        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.RequestOtpAsync(new OtpRequest(Phone), null)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.RequestOtpAsync(new OtpRequest("9123456780"), null)).Status);
    }

    [Fact]
    public async Task If_the_text_cannot_be_sent_the_caller_is_told_to_try_again_and_the_number_stays_out_of_the_message()
    {
        var rig = Create();
        rig.Sender.Fails = true;

        var result = await rig.Service.RequestOtpAsync(new OtpRequest(Phone), null);

        Assert.Equal(SignInStatus.Unavailable, result.Status);
        Assert.DoesNotContain("provider", result.Message!);
        Assert.DoesNotContain("98765", result.Message!);
    }

    // ---------------- signing in with the code ----------------

    [Fact]
    public async Task The_right_code_makes_an_account_for_a_new_number_and_signs_it_in()
    {
        var rig = Create();
        var code = await Ask(rig);

        var result = await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code, "Asha Patil"), "1.2.3.4");

        Assert.Equal(SignInStatus.Succeeded, result.Status);
        var user = Assert.Single(rig.Store.Users);
        Assert.Equal((Plus, true, false), (user.Phone, user.PhoneVerified, user.HasPassword));
        Assert.Equal(user.Id, Assert.Single(rig.Issuer.Issued));
    }

    [Fact]
    public async Task The_same_number_signs_in_to_the_same_account_every_time()
    {
        var rig = Create();
        var first = await Ask(rig);
        await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, first), null);
        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        var second = await Ask(rig);

        await rig.Service.VerifyOtpAsync(new OtpVerifyRequest("+91 98765 43210", second), null);

        Assert.Single(rig.Store.Users);
        Assert.Equal(2, rig.Issuer.Issued.Count);
        Assert.Single(rig.Issuer.Issued.Distinct());
    }

    [Fact]
    public async Task A_wrong_code_is_refused_the_same_way_whatever_the_reason()
    {
        var rig = Create();
        var code = await Ask(rig);

        var wrong = await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, Wrong(code)), null);
        var noCode = await rig.Service.VerifyOtpAsync(new OtpVerifyRequest("9123456780", "123456"), null);
        var badShape = await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, "12ab56"), null);
        var badPhone = await rig.Service.VerifyOtpAsync(new OtpVerifyRequest("nope", code), null);

        foreach (var result in new[] { wrong, noCode, badShape, badPhone })
        {
            Assert.Equal((SignInStatus.Unauthorized, "Invalid or expired code."), (result.Status, result.Message));
        }

        Assert.Empty(rig.Store.Users);
    }

    [Fact]
    public async Task Five_wrong_codes_burn_the_code_even_for_the_right_one()
    {
        var rig = Create();
        var code = await Ask(rig);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, Wrong(code)), null)).Status);
        }

        Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null)).Status);
        Assert.Empty(rig.Store.Users);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var rig = Create();
        var code = await Ask(rig);
        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null)).Status);

        Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null)).Status);
        Assert.Single(rig.Issuer.Issued);
    }

    [Fact]
    public async Task A_code_stops_working_after_five_minutes()
    {
        var rig = Create();
        var code = await Ask(rig);
        rig.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null)).Status);

        var late = Create();
        var lateCode = await Ask(late);
        late.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.Equal(SignInStatus.Unauthorized, (await late.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, lateCode), null)).Status);
    }

    [Fact]
    public async Task A_code_asked_for_one_number_does_not_open_another()
    {
        var rig = Create();
        var code = await Ask(rig, Phone);
        await Ask(rig, "9123456780");

        Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest("9123456780", code), null)).Status);
        Assert.Empty(rig.Store.Users);
    }

    [Fact]
    public async Task A_number_typed_into_a_profile_but_never_verified_does_not_sign_in_to_that_account()
    {
        var rig = Create();
        var typed = new FakeUser { Phone = Plus, PhoneVerified = false, Email = "owner@example.test", HasPassword = true };
        rig.Store.Users.Add(typed);
        var code = await Ask(rig);

        await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null);

        Assert.Equal(2, rig.Store.Users.Count);
        Assert.DoesNotContain(typed.Id, rig.Issuer.Issued);
    }

    [Fact]
    public async Task Without_an_organization_to_join_a_new_number_cannot_sign_up()
    {
        var rig = Create(noOrganization: true);
        var code = await Ask(rig);

        var result = await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null);

        Assert.Equal(SignInStatus.NotConfigured, result.Status);
        Assert.Empty(rig.Store.Users);
    }

    [Fact]
    public async Task The_settings_can_make_codes_shorter_lived_and_attempts_fewer()
    {
        var rig = Create(tune: settings => { settings.LifetimeMinutes = 1; settings.MaxAttempts = 2; });
        var code = await Ask(rig);
        await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, Wrong(code)), null);
        await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, Wrong(code)), null);

        Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null)).Status);
        Assert.Throws<InvalidOperationException>(() => new OtpSettings { LifetimeMinutes = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new OtpSettings { MaxAttempts = 11 }.Validate());
        new OtpSettings().Validate();
    }

    // ---------------- verifying the number of an account you already have ----------------

    [Fact]
    public async Task A_signed_in_customer_verifies_a_number_and_can_then_sign_in_with_it()
    {
        var rig = Create();
        var me = new FakeUser { Email = "me@example.test", HasPassword = true };
        rig.Store.Users.Add(me);
        rig.Who.Context = new UserContext(me.Id, rig.Organization, null, Role.Customer);

        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.RequestPhoneVerificationAsync(new OtpRequest(Phone), null)).Status);
        var verified = await rig.Service.VerifyPhoneAsync(new OtpVerifyRequest(Phone, rig.Sender.LastCode), null);
        Assert.Equal(SignInStatus.Succeeded, verified.Status);
        Assert.Equal((Plus, true), (me.Phone, me.PhoneVerified));

        rig.Who.Context = null;
        rig.Clock.Advance(TimeSpan.FromSeconds(31));
        var code = await Ask(rig);
        await rig.Service.VerifyOtpAsync(new OtpVerifyRequest(Phone, code), null);
        Assert.Equal(me.Id, Assert.Single(rig.Issuer.Issued));
        Assert.Single(rig.Store.Users);
    }

    [Fact]
    public async Task A_number_another_account_has_verified_cannot_be_verified_again()
    {
        var rig = Create();
        rig.Store.Users.Add(new FakeUser { Phone = Plus, PhoneVerified = true });
        var me = new FakeUser { Email = "me@example.test", HasPassword = true };
        rig.Store.Users.Add(me);
        rig.Who.Context = new UserContext(me.Id, rig.Organization, null, Role.Customer);
        await rig.Service.RequestPhoneVerificationAsync(new OtpRequest(Phone), null);

        var result = await rig.Service.VerifyPhoneAsync(new OtpVerifyRequest(Phone, rig.Sender.LastCode), null);

        Assert.Equal(SignInStatus.Conflict, result.Status);
        Assert.False(me.PhoneVerified);
    }

    [Fact]
    public async Task Only_a_signed_in_customer_can_verify_a_number_and_a_sign_in_code_is_no_use_for_it()
    {
        var rig = Create();
        Assert.Equal(SignInStatus.Forbidden, (await rig.Service.RequestPhoneVerificationAsync(new OtpRequest(Phone), null)).Status);
        rig.Who.Context = new UserContext(Guid.NewGuid(), rig.Organization, Guid.NewGuid(), Role.StoreStaff);
        Assert.Equal(SignInStatus.Forbidden, (await rig.Service.RequestPhoneVerificationAsync(new OtpRequest(Phone), null)).Status);
        Assert.Equal(SignInStatus.Forbidden, (await rig.Service.VerifyPhoneAsync(new OtpVerifyRequest(Phone, "123456"), null)).Status);

        var me = new FakeUser { Email = "me@example.test" };
        rig.Store.Users.Add(me);
        rig.Who.Context = new UserContext(me.Id, rig.Organization, null, Role.Customer);
        var signInCode = await Ask(rig);
        Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.VerifyPhoneAsync(new OtpVerifyRequest(Phone, signInCode), null)).Status);
        Assert.False(me.PhoneVerified);
    }

    [Fact]
    public async Task A_verification_code_asked_for_by_one_customer_is_no_use_to_another()
    {
        var rig = Create();
        var mine = new FakeUser { Email = "a@example.test" };
        var theirs = new FakeUser { Email = "b@example.test" };
        rig.Store.Users.AddRange([mine, theirs]);
        rig.Who.Context = new UserContext(mine.Id, rig.Organization, null, Role.Customer);
        await rig.Service.RequestPhoneVerificationAsync(new OtpRequest(Phone), null);
        var code = rig.Sender.LastCode;

        rig.Who.Context = new UserContext(theirs.Id, rig.Organization, null, Role.Customer);
        var result = await rig.Service.VerifyPhoneAsync(new OtpVerifyRequest(Phone, code), null);

        Assert.Equal(SignInStatus.Unauthorized, result.Status);
        Assert.False(theirs.PhoneVerified);
    }

    // ---------------- Google ----------------

    private static GoogleIdentity Person(string sub = "g-1", string email = "Asha@Example.test", bool verified = true, string? name = "Asha Patil") => new(sub, email, verified, name);

    [Fact]
    public async Task Google_sign_in_is_off_until_it_is_configured()
    {
        var rig = Create();
        rig.Google.IsConfigured = false;

        Assert.Equal(SignInStatus.NotConfigured, (await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("token"), null)).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task A_token_Google_does_not_vouch_for_signs_nobody_in(string token)
    {
        var rig = Create();

        var result = await rig.Service.GoogleSignInAsync(new GoogleSignInRequest(token), null);

        Assert.Equal(SignInStatus.Unauthorized, result.Status);
        Assert.Empty(rig.Store.Users);
    }

    [Fact]
    public async Task An_email_Google_has_not_verified_is_never_trusted()
    {
        var rig = Create();
        rig.Google.Tokens["t"] = Person(verified: false);

        Assert.Equal(SignInStatus.Unauthorized, (await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null)).Status);
        Assert.Empty(rig.Store.Users);
    }

    [Fact]
    public async Task A_new_person_gets_an_account_with_a_verified_email_and_returns_to_it_next_time()
    {
        var rig = Create();
        rig.Google.Tokens["t"] = Person();

        var first = await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null);
        var again = await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null);

        Assert.Equal((SignInStatus.Succeeded, SignInStatus.Succeeded), (first.Status, again.Status));
        var user = Assert.Single(rig.Store.Users);
        Assert.Equal(("asha@example.test", true, "g-1", false), (user.Email, user.EmailVerified, user.GoogleSubject, user.HasPassword));
        Assert.Equal([user.Id, user.Id], rig.Issuer.Issued);
    }

    [Fact]
    public async Task Google_finds_a_person_by_their_google_id_even_if_their_email_has_changed()
    {
        var rig = Create();
        rig.Google.Tokens["t1"] = Person(email: "old@example.test");
        await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t1"), null);
        rig.Google.Tokens["t2"] = Person(email: "new@example.test");

        await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t2"), null);

        Assert.Single(rig.Store.Users);
    }

    [Fact]
    public async Task An_account_whose_email_nobody_verified_loses_its_password_and_sessions_when_the_real_owner_arrives_with_google()
    {
        var rig = Create();
        var squatter = new FakeUser { Email = "asha@example.test", EmailVerified = false, HasPassword = true, Sessions = true };
        rig.Store.Users.Add(squatter);
        rig.Google.Tokens["t"] = Person();

        var result = await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null);

        Assert.Equal(SignInStatus.Succeeded, result.Status);
        Assert.Equal(squatter.Id, Assert.Single(rig.Issuer.Issued));
        Assert.False(squatter.HasPassword);
        Assert.True(squatter.EmailVerified);
        Assert.Equal("g-1", squatter.GoogleSubject);
        Assert.Single(rig.Store.Users);
    }

    [Fact]
    public async Task An_account_whose_email_was_already_verified_keeps_its_password_when_google_is_linked()
    {
        var rig = Create();
        var verified = new FakeUser { Email = "asha@example.test", EmailVerified = true, HasPassword = true };
        rig.Store.Users.Add(verified);
        rig.Google.Tokens["t"] = Person();

        await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null);

        Assert.True(verified.HasPassword);
        Assert.Equal("g-1", verified.GoogleSubject);
    }

    [Fact]
    public async Task A_staff_or_admin_email_cannot_be_taken_over_through_google()
    {
        var rig = Create();
        rig.Store.Users.Add(new FakeUser { Email = "asha@example.test", IsCustomer = false, HasPassword = true });
        rig.Google.Tokens["t"] = Person();

        var result = await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null);

        Assert.Equal(SignInStatus.Forbidden, result.Status);
        Assert.Empty(rig.Issuer.Issued);
    }

    [Fact]
    public async Task A_google_name_that_is_missing_falls_back_to_the_start_of_the_email()
    {
        var rig = Create();
        rig.Google.Tokens["t"] = Person(name: null);

        Assert.Equal(SignInStatus.Succeeded, (await rig.Service.GoogleSignInAsync(new GoogleSignInRequest("t"), null)).Status);
    }
}

[Collection("SqlServer")]
public sealed class SignInEfIntegrationTests
{
    private static string? ConnectionString => CheckoutEfIntegrationTests.ConnectionString;

    private static string NewPhone() => "+9198" + Random.Shared.Next(10_000_000, 99_999_999);

    private static async Task<Guid> OrganizationAsync(CheckoutEfIntegrationTests.World world)
    {
        await using var db = world.Db();
        return (await db.Organizations.AsNoTracking().FirstAsync()).Id;
    }

    private static async Task CleanAsync(CheckoutEfIntegrationTests.World world, string[] phones, string[] emails)
    {
        await using var db = world.Db();
        var ids = await db.Users.Where(user => (user.PhoneNumber != null && phones.Contains(user.PhoneNumber)) || (user.Email != null && emails.Contains(user.Email))).Select(user => user.Id).ToListAsync();
        await db.OtpChallenges.Where(item => phones.Contains(item.Phone)).ExecuteDeleteAsync();
        await db.RefreshTokens.Where(token => ids.Contains(token.UserId)).ExecuteDeleteAsync();
        await db.UserCredentials.Where(credential => ids.Contains(credential.UserId)).ExecuteDeleteAsync();
        await db.UserRoles.Where(role => ids.Contains(role.UserId)).ExecuteDeleteAsync();
        await db.Customers.Where(customer => ids.Contains(customer.UserId)).ExecuteDeleteAsync();
        await db.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Codes_are_counted_closed_when_replaced_and_used_up_exactly_once_even_when_tried_together()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var phone = NewPhone();
        var now = DateTime.UtcNow;
        try
        {
            await using (var db = world.Db())
            {
                var store = new EfIdentityStore(db);
                await store.CreateOtpAsync(phone, "SignIn", null, new string('a', 64), now.AddSeconds(-60), now.AddMinutes(4), "1.1.1.1");
                await store.CreateOtpAsync(phone, "SignIn", null, new string('b', 64), now, now.AddMinutes(5), "1.1.1.1");
            }

            OtpRecord? open;
            await using (var db = world.Db())
            {
                var store = new EfIdentityStore(db);
                Assert.Equal(2, await store.CountOtpRequestsSinceAsync(phone, now.AddHours(-1)));
                open = await store.GetOpenOtpAsync(phone, "SignIn", null, now, 5);
                Assert.Equal(new string('b', 64), open!.CodeHash);
                Assert.Null(await store.GetOpenOtpAsync(phone, "VerifyPhone", null, now, 5));
                Assert.Null(await store.GetOpenOtpAsync(phone, "SignIn", null, now.AddMinutes(6), 5));
            }

            var consumed = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var db = world.Db();
                return await new EfIdentityStore(db).TryConsumeOtpAsync(open!.Id, now);
            }));
            Assert.Equal(1, consumed.Count(won => won));

            await using var verify = world.Db();
            Assert.Null(await new EfIdentityStore(verify).GetOpenOtpAsync(phone, "SignIn", null, now, 5));
        }
        finally
        {
            await CleanAsync(world, [phone], []);
        }
    }

    [Fact]
    public async Task Wrong_attempts_are_counted_and_a_code_with_too_many_is_no_longer_open()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var phone = NewPhone();
        var now = DateTime.UtcNow;
        try
        {
            await using (var db = world.Db())
            {
                await new EfIdentityStore(db).CreateOtpAsync(phone, "SignIn", null, new string('c', 64), now, now.AddMinutes(5), null);
            }

            var open = await Open(world, phone, now);
            for (var i = 0; i < 5; i++)
            {
                await using var db = world.Db();
                await new EfIdentityStore(db).RecordFailedOtpAttemptAsync(open!.Id);
            }

            Assert.Null(await Open(world, phone, now));
        }
        finally
        {
            await CleanAsync(world, [phone], []);
        }
    }

    private static async Task<OtpRecord?> Open(CheckoutEfIntegrationTests.World world, string phone, DateTime now)
    {
        await using var db = world.Db();
        return await new EfIdentityStore(db).GetOpenOtpAsync(phone, "SignIn", null, now, 5);
    }

    [Fact]
    public async Task Two_sign_ups_with_the_same_number_at_once_make_one_account()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var phone = NewPhone();
        var organization = await OrganizationAsync(world);
        try
        {
            var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
            {
                await using var db = world.Db();
                return await new EfIdentityStore(db).CreatePhoneCustomerAsync(organization, phone, "Customer", DateTime.UtcNow);
            }));

            Assert.All(ids, id => Assert.NotNull(id));
            Assert.Single(ids.Distinct());
            await using var verify = world.Db();
            var user = await verify.Users.AsNoTracking().Include(item => item.Customer).Include(item => item.UserRoles).SingleAsync(item => item.PhoneNumber == phone);
            Assert.Equal((Role.Customer, true, true), (user.Role, user.PhoneVerifiedAt != null, user.Customer != null));
            Assert.Null(await verify.UserCredentials.FirstOrDefaultAsync(credential => credential.UserId == user.Id));
            Assert.Single(user.UserRoles);
            Assert.Equal(user.Id, await new EfIdentityStore(verify).FindActiveCustomerByVerifiedPhoneAsync(phone));
        }
        finally
        {
            await CleanAsync(world, [phone], []);
        }
    }

    [Fact]
    public async Task A_verified_number_belongs_to_one_account_but_typed_numbers_can_repeat()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 2, stock: 5);
        var phone = NewPhone();
        try
        {
            Guid first, second;
            await using (var db = world.Db())
            {
                first = await db.Users.Where(user => user.ExternalSubject == world.Emails[0]).Select(user => user.Id).SingleAsync();
                second = await db.Users.Where(user => user.ExternalSubject == world.Emails[1]).Select(user => user.Id).SingleAsync();
                await db.Users.Where(user => user.Id == first || user.Id == second).ExecuteUpdateAsync(set => set.SetProperty(user => user.PhoneNumber, phone));
            }

            await using (var db = world.Db())
            {
                var store = new EfIdentityStore(db);
                Assert.Null(await store.FindActiveCustomerByVerifiedPhoneAsync(phone));
                Assert.True(await store.TryMarkPhoneVerifiedAsync(first, phone, DateTime.UtcNow));
                Assert.True(await store.TryMarkPhoneVerifiedAsync(first, phone, DateTime.UtcNow));
            }

            await using (var db = world.Db())
            {
                var store = new EfIdentityStore(db);
                Assert.False(await store.TryMarkPhoneVerifiedAsync(second, phone, DateTime.UtcNow));
                Assert.Equal(first, await store.FindActiveCustomerByVerifiedPhoneAsync(phone));
            }

            await using var verify = world.Db();
            Assert.Null((await verify.Users.AsNoTracking().SingleAsync(user => user.Id == second)).PhoneVerifiedAt);
        }
        finally
        {
            await using var db = world.Db();
            await db.Users.Where(user => user.PhoneNumber == phone).ExecuteUpdateAsync(set => set.SetProperty(user => user.PhoneNumber, (string?)null).SetProperty(user => user.PhoneVerifiedAt, (DateTime?)null));
        }
    }

    [Fact]
    public async Task Linking_google_to_an_unverified_account_removes_its_password_and_sessions_and_finds_it_by_google_id_afterwards()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var subject = "g-" + Guid.NewGuid().ToString("N")[..12];
        Guid userId;
        try
        {
            await using (var db = world.Db())
            {
                userId = await db.Users.Where(user => user.ExternalSubject == world.Emails[0]).Select(user => user.Id).SingleAsync();
                db.RefreshTokens.Add(new RefreshToken { UserId = userId, SessionId = Guid.NewGuid(), TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTime.UtcNow.AddDays(1) });
                await db.SaveChangesAsync();
            }

            await using (var db = world.Db())
            {
                var identity = (await new EfIdentityStore(db).FindActiveCustomerByEmailAsync(world.Emails[0].ToLowerInvariant()))!;
                Assert.Equal((false, true), (identity.EmailVerified, identity.HasPassword));
                await new EfIdentityStore(db).LinkGoogleAsync(identity.UserId, subject, wipeSignIn: true, "9.9.9.9", DateTime.UtcNow);
            }

            await using var verify = world.Db();
            Assert.Null(await verify.UserCredentials.FirstOrDefaultAsync(credential => credential.UserId == userId));
            Assert.All(await verify.RefreshTokens.Where(token => token.UserId == userId).ToListAsync(), token => Assert.NotNull(token.RevokedAt));
            var found = await new EfIdentityStore(verify).FindActiveCustomerByGoogleAsync(subject);
            Assert.Equal((userId, true, false), (found!.UserId, found.EmailVerified, found.HasPassword));
        }
        finally
        {
            await using var db = world.Db();
            await db.RefreshTokens.Where(token => token.User.ExternalSubject == world.Emails[0]).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_new_google_customer_is_created_once_even_when_two_requests_race_and_a_taken_email_is_not_overwritten()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var organization = await OrganizationAsync(world);
        var email = $"g{Guid.NewGuid():N}@example.test"[..28];
        var subject = "g-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            var ids = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                await using var db = world.Db();
                return await new EfIdentityStore(db).CreateGoogleCustomerAsync(organization, email, "Asha Patil", subject, DateTime.UtcNow);
            }));
            Assert.All(ids, id => Assert.NotNull(id));
            Assert.Single(ids.Distinct());

            await using var db2 = world.Db();
            var other = await new EfIdentityStore(db2).CreateGoogleCustomerAsync(organization, email, "Someone Else", "g-other-" + Guid.NewGuid().ToString("N")[..8], DateTime.UtcNow);
            Assert.Null(other);
            var user = await db2.Users.AsNoTracking().SingleAsync(item => item.Email == email);
            Assert.Equal(("Asha Patil", true, subject), (user.DisplayName, user.EmailVerifiedAt != null, user.GoogleSubject));
        }
        finally
        {
            await CleanAsync(world, [], [email]);
        }
    }

    [Fact]
    public async Task A_verified_number_stays_verified_only_while_it_is_the_same_number_and_phone_only_accounts_show_no_email()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(customers: 1, stock: 5);
        var phone = NewPhone();
        var organization = await OrganizationAsync(world);
        var otherPhone = NewPhone();
        try
        {
            Guid userId;
            await using (var db = world.Db())
            {
                userId = await db.Users.Where(user => user.ExternalSubject == world.Emails[0]).Select(user => user.Id).SingleAsync();
                Assert.True(await new EfIdentityStore(db).TryMarkPhoneVerifiedAsync(userId, phone, DateTime.UtcNow));
            }

            await using (var db = world.Db())
            {
                var store = new EfAccountStore(db);
                Assert.True((await store.GetProfileAsync(userId))!.PhoneVerified);
                await store.UpdateProfileAsync(userId, "Same Person", "Same", "Person", phone[3..]);
            }

            await using (var db = world.Db())
            {
                var profile = (await new EfAccountStore(db).GetProfileAsync(userId))!;
                Assert.Equal((phone, true), (profile.PhoneNumber, profile.PhoneVerified));
                await new EfAccountStore(db).UpdateProfileAsync(userId, "Same Person", "Same", "Person", otherPhone);
            }

            await using (var db = world.Db())
            {
                var profile = (await new EfAccountStore(db).GetProfileAsync(userId))!;
                Assert.Equal((otherPhone, false), (profile.PhoneNumber, profile.PhoneVerified));
                Assert.Null(await new EfIdentityStore(db).FindActiveCustomerByVerifiedPhoneAsync(phone));
            }

            Guid phoneOnly;
            await using (var db = world.Db())
            {
                phoneOnly = (await new EfIdentityStore(db).CreatePhoneCustomerAsync(organization, phone, "Customer", DateTime.UtcNow))!.Value;
            }

            await using var verify = world.Db();
            var onlyPhone = (await new EfAccountStore(verify).GetProfileAsync(phoneOnly))!;
            Assert.Equal(("", phone, true), (onlyPhone.Email, onlyPhone.PhoneNumber, onlyPhone.PhoneVerified));
        }
        finally
        {
            await using var db = world.Db();
            await db.Users.Where(user => user.PhoneNumber == otherPhone).ExecuteUpdateAsync(set => set.SetProperty(user => user.PhoneNumber, (string?)null));
            await CleanAsync(world, [phone, otherPhone], []);
        }
    }
}
