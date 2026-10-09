using System.Security.Cryptography;
using System.Text;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using Xunit;

namespace QuickCommerce.Tests;

public sealed class AccountServiceTests
{
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private const string Key = "unit-test-reset-code-key-0123456789abcdef";

    // ---------- fixtures ----------

    private sealed class Harness
    {
        public FakeStore Store { get; } = new();
        public FakeEmail Email { get; } = new();
        public FakeAuth Auth { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public TestClock Clock { get; } = new();
        public AccountSettings Settings { get; } = new() { ResetCodeKey = Key };
        public RegistrationSettings Registration { get; } = new();
        public AccountService Service { get; }

        public Harness()
        {
            Service = new AccountService(
                Store, new FakeHashing(), Auth, Email, User,
                new RegisterCustomerRequestValidator(), new ForgotPasswordRequestValidator(), new ResetPasswordRequestValidator(),
                new ChangePasswordRequestValidator(), new UpdateCustomerProfileRequestValidator(),
                Settings, Registration, Clock);
        }

        public async Task<(Guid UserId, string Code)> CustomerWithResetCodeAsync(string email = "asha@example.com")
        {
            var user = Store.AddCustomer(email, "OldPassword1");
            await Service.RequestPasswordResetAsync(new ForgotPasswordRequest(email), null, "1.2.3.4");
            return (user, ResetCodes.Normalize(CodeFromEmail(Email.Sent.Last())));
        }
    }

    private static string CodeFromEmail(EmailMessage message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message.Body, @"[A-Z0-9]{4}-[A-Z0-9]{4}|[०-९]?[A-Z0-9]{4}-[A-Z0-9]{4}");
        return match.Value;
    }

    private static RegisterCustomerRequest Register(string email = "Asha@Example.com", string password = "Sunrise-42", string name = "Asha Patil", string? phone = "98765 43210") => new(name, email, password, phone);

    // ---------- password policy ----------

    [Theory]
    [InlineData("short1", false)]
    [InlineData("12345678", false)]          // common
    [InlineData("Password123", false)]       // common
    [InlineData("QuickCart123", false)]      // common (product name)
    [InlineData("longenough", true)]         // no composition rule
    [InlineData("correct horse battery staple", true)]
    public void Password_policy_checks_length_and_common_passwords(string password, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.Check(password) is null);

    [Fact]
    public void Password_policy_rejects_the_email_and_overlong_passwords()
    {
        Assert.NotNull(PasswordPolicy.Check("asha@example.com", "ASHA@example.com"));
        Assert.NotNull(PasswordPolicy.Check(new string('a', PasswordPolicy.MaxLength + 1)));
        Assert.Null(PasswordPolicy.Check(new string('a', PasswordPolicy.MaxLength)));
    }

    // ---------- reset codes ----------

    [Fact]
    public void Reset_codes_are_eight_unambiguous_characters_shown_with_a_dash()
    {
        var code = ResetCodes.Generate();
        Assert.Equal(8, code.Length);
        Assert.True(ResetCodes.IsWellFormed(code));
        Assert.DoesNotContain(code, ch => "ILO01".Contains(ch));
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", ResetCodes.Format(code));
        Assert.Equal(code, ResetCodes.Normalize(" " + ResetCodes.Format(code).ToLowerInvariant() + " "));
        Assert.NotEqual(ResetCodes.Generate(), ResetCodes.Generate());
    }

    [Fact]
    public void Reset_code_hash_is_keyed_and_bound_to_the_user()
    {
        var a = Guid.NewGuid();
        var hash = ResetCodes.Hash(Key, a, "ABCDEFGH");

        Assert.Equal(hash, ResetCodes.Hash(Key, a, "ABCDEFGH"));
        Assert.NotEqual(hash, ResetCodes.Hash(Key, Guid.NewGuid(), "ABCDEFGH"));
        Assert.NotEqual(hash, ResetCodes.Hash(Key + "x", a, "ABCDEFGH"));
        Assert.DoesNotContain("ABCDEFGH", hash);
        Assert.True(ResetCodes.Matches(hash, hash));
        Assert.False(ResetCodes.Matches(hash, ResetCodes.Hash(Key, a, "ABCDEFGJ")));
    }

    // ---------- registration ----------

    [Fact]
    public async Task Registration_creates_a_normalized_customer_and_signs_in()
    {
        var h = new Harness();

        var result = await h.Service.RegisterAsync(Register(), "9.9.9.9");

        Assert.True(result.Succeeded);
        Assert.Equal("signed-in", result.Value!.AccessToken);
        var created = h.Store.Created.Single();
        Assert.Equal(("asha@example.com", "Asha", "Patil", "Asha Patil", "9876543210", OrgId), (created.Email, created.FirstName, created.LastName, created.FullName, created.PhoneNumber, created.OrganizationId));
        Assert.Equal("hash:Sunrise-42", created.PasswordHash);
        Assert.Equal(("asha@example.com", "Sunrise-42", "9.9.9.9"), (h.Auth.LastLogin!.Username, h.Auth.LastLogin.Password, h.Auth.LastIp));
    }

    [Theory]
    [InlineData("A", "Sunrise-42", "asha@example.com", "9876543210", "full name")]
    [InlineData("Asha Patil", "short", "asha@example.com", "9876543210", "at least 8")]
    [InlineData("Asha Patil", "password123", "asha@example.com", "9876543210", "too common")]
    [InlineData("Asha Patil", "Sunrise-42", "not-an-email", "9876543210", "valid email")]
    [InlineData("Asha Patil", "Sunrise-42", "asha@example.com", "12345", "10 to 15 digits")]
    public async Task Registration_rejects_invalid_input_without_creating_anything(string name, string password, string email, string phone, string expected)
    {
        var h = new Harness();

        var result = await h.Service.RegisterAsync(new RegisterCustomerRequest(name, email, password, phone), null);

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
        Assert.Contains(expected, string.Join(" ", result.Errors!), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Store.Created);
    }

    [Fact]
    public async Task Registration_without_a_phone_is_fine()
    {
        var h = new Harness();

        var result = await h.Service.RegisterAsync(Register(phone: null), null);

        Assert.True(result.Succeeded);
        Assert.Null(h.Store.Created.Single().PhoneNumber);
    }

    [Fact]
    public async Task Registering_an_existing_email_is_a_conflict_even_with_different_capitals_or_a_race()
    {
        var h = new Harness();
        h.Store.AddCustomer("asha@example.com", "x");

        var duplicate = await h.Service.RegisterAsync(Register("ASHA@EXAMPLE.COM"), null);
        h.Store.ForceCreateRace = true;
        var race = await h.Service.RegisterAsync(Register("new@example.com"), null);

        Assert.Equal(AccountStatus.Conflict, duplicate.Status);
        Assert.Equal(AccountStatus.Conflict, race.Status);
    }

    [Fact]
    public async Task Registration_reports_a_configuration_error_when_the_organization_cannot_be_chosen()
    {
        var h = new Harness();
        h.Store.OrganizationError = "Several organizations are active; set Registration:OrganizationId.";

        var result = await h.Service.RegisterAsync(Register(), null);

        Assert.Equal(AccountStatus.ConfigurationError, result.Status);
        Assert.Contains("Registration:OrganizationId", result.Message);
        Assert.Empty(h.Store.Created);
    }

    [Fact]
    public async Task Registration_passes_the_configured_organization_to_the_store()
    {
        var h = new Harness();
        var configured = Guid.NewGuid();
        h.Registration.OrganizationId = configured;

        await h.Service.RegisterAsync(Register(), null);

        Assert.Equal(configured, h.Store.ConfiguredOrganizationSeen);
    }

    // ---------- requesting a reset ----------

    [Fact]
    public async Task Reset_request_for_an_unknown_email_succeeds_but_sends_nothing()
    {
        var h = new Harness();

        var result = await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("nobody@example.com"), null, null);

        Assert.True(result.Succeeded);
        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.Store.Codes);
    }

    [Fact]
    public async Task Reset_request_for_a_known_customer_emails_a_code_and_stores_only_its_keyed_hash()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("asha@example.com", "OldPassword1");

        var result = await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("Asha@Example.com"), null, "1.2.3.4");

        Assert.True(result.Succeeded);
        var mail = h.Email.Sent.Single();
        Assert.Equal("asha@example.com", mail.To);
        var code = CodeFromEmail(mail);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        var stored = h.Store.Codes.Single();
        Assert.Equal(ResetCodes.Hash(Key, user, ResetCodes.Normalize(code)), stored.CodeHash);
        Assert.DoesNotContain(ResetCodes.Normalize(code), stored.CodeHash);
        Assert.Equal(h.Clock.UtcNow.UtcDateTime.AddMinutes(30), stored.ExpiresUtc);
        Assert.Contains("30 minutes", mail.Body);
    }

    [Fact]
    public async Task Reset_email_is_in_marathi_when_requested()
    {
        var h = new Harness();
        h.Store.AddCustomer("asha@example.com", "x");

        await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("asha@example.com"), "mr-IN,mr;q=0.9", null);

        Assert.Contains("पासवर्ड", h.Email.Sent.Single().Subject);
    }

    [Fact]
    public async Task Inactive_accounts_get_no_reset_email_and_a_mail_outage_does_not_change_the_answer()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("gone@example.com", "x");
        h.Store.Deactivate(user);
        h.Store.AddCustomer("asha@example.com", "x");
        h.Email.Fail = true;

        var inactive = await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("gone@example.com"), null, null);
        var outage = await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("asha@example.com"), null, null);

        Assert.True(inactive.Succeeded);
        Assert.True(outage.Succeeded);
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task At_most_three_codes_per_account_per_hour_then_silence_until_the_hour_passes()
    {
        var h = new Harness();
        h.Store.AddCustomer("asha@example.com", "x");

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("asha@example.com"), null, null)).Succeeded);
        }

        Assert.Equal(3, h.Email.Sent.Count);
        h.Clock.Advance(TimeSpan.FromMinutes(61));
        await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("asha@example.com"), null, null);
        Assert.Equal(4, h.Email.Sent.Count);
    }

    [Fact]
    public async Task A_new_request_invalidates_the_previous_code()
    {
        var h = new Harness();
        var (_, oldCode) = await h.CustomerWithResetCodeAsync();
        await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("asha@example.com"), null, null);

        var result = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", oldCode, "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
    }

    // ---------- using the code ----------

    [Fact]
    public async Task The_right_code_sets_the_password_clears_lockout_and_signs_out_every_session()
    {
        var h = new Harness();
        var (user, code) = await h.CustomerWithResetCodeAsync();
        h.Store.LockOut(user);

        var result = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("ASHA@example.com", code.ToLowerInvariant(), "BrandNew-77"), "5.5.5.5");

        Assert.True(result.Succeeded);
        Assert.Equal("hash:BrandNew-77", h.Store.PasswordHash(user));
        Assert.False(h.Store.IsLockedOut(user));
        Assert.Equal((user, (string?)null), h.Store.LastRevocation);
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        var h = new Harness();
        var (_, code) = await h.CustomerWithResetCodeAsync();
        await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "BrandNew-77"), null);

        var again = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "Another-88"), null);

        Assert.Equal(AccountStatus.InvalidRequest, again.Status);
    }

    [Fact]
    public async Task Five_wrong_codes_kill_the_code_even_if_the_next_guess_is_right()
    {
        var h = new Harness();
        var (user, code) = await h.CustomerWithResetCodeAsync();

        for (var i = 0; i < 5; i++)
        {
            var wrong = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", i == 0 ? "ZZZZ-ZZZZ" : "ZZZZ-ZZZY", "BrandNew-77"), null);
            Assert.Equal("Invalid or expired code.", wrong.Message);
        }

        var right = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, right.Status);
        Assert.Equal("hash:OldPassword1", h.Store.PasswordHash(user));
    }

    [Fact]
    public async Task A_code_expires_after_thirty_minutes()
    {
        var h = new Harness();
        var (_, code) = await h.CustomerWithResetCodeAsync();
        h.Clock.Advance(TimeSpan.FromMinutes(31));

        var result = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task Every_reset_failure_gives_the_same_answer_so_nothing_is_revealed()
    {
        var h = new Harness();
        var (_, code) = await h.CustomerWithResetCodeAsync();

        var unknownAccount = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("nobody@example.com", code, "BrandNew-77"), null);
        var wrongCode = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", "ZZZZ-ZZZZ", "BrandNew-77"), null);

        Assert.Equal(unknownAccount.Message, wrongCode.Message);
        Assert.Equal(unknownAccount.Status, wrongCode.Status);
        Assert.Equal(AccountStatus.InvalidRequest, wrongCode.Status);
    }

    [Fact]
    public async Task A_code_from_one_account_does_not_work_for_another()
    {
        var h = new Harness();
        var (_, codeForAsha) = await h.CustomerWithResetCodeAsync("asha@example.com");
        h.Store.AddCustomer("ravi@example.com", "x");
        await h.Service.RequestPasswordResetAsync(new ForgotPasswordRequest("ravi@example.com"), null, null);

        var result = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("ravi@example.com", codeForAsha, "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected_and_does_not_burn_the_code()
    {
        var h = new Harness();
        var (user, code) = await h.CustomerWithResetCodeAsync();

        var weak = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "password123"), null);
        var good = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, weak.Status);
        Assert.Contains("too common", weak.Message);
        Assert.True(good.Succeeded);
        Assert.Equal("hash:BrandNew-77", h.Store.PasswordHash(user));
    }

    [Fact]
    public async Task A_malformed_code_is_rejected_before_any_lookup()
    {
        var h = new Harness();
        await h.CustomerWithResetCodeAsync();

        var result = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", "123", "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
        Assert.Contains("8-character", result.Message);
    }

    [Fact]
    public async Task Losing_the_race_to_consume_the_code_is_a_failure()
    {
        var h = new Harness();
        var (user, code) = await h.CustomerWithResetCodeAsync();
        h.Store.ConsumeAlwaysFails = true;

        var result = await h.Service.ResetPasswordAsync(new ResetPasswordRequest("asha@example.com", code, "BrandNew-77"), null);

        Assert.Equal(AccountStatus.InvalidRequest, result.Status);
        Assert.Equal("hash:OldPassword1", h.Store.PasswordHash(user));
    }

    // ---------- change password ----------

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_keeps_only_the_callers_session()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("asha@example.com", "OldPassword1");
        h.User.SignIn(user);
        const string refreshToken = "my-refresh-token";

        var wrong = await h.Service.ChangePasswordAsync(new ChangePasswordRequest("Nope-12345", "BrandNew-77", refreshToken), null);
        var ok = await h.Service.ChangePasswordAsync(new ChangePasswordRequest("OldPassword1", "BrandNew-77", refreshToken), "7.7.7.7");

        Assert.Equal(AccountStatus.InvalidRequest, wrong.Status);
        Assert.Contains("current password is incorrect", wrong.Message);
        Assert.True(ok.Succeeded);
        Assert.Equal("hash:BrandNew-77", h.Store.PasswordHash(user));
        Assert.Equal((user, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)))), h.Store.LastRevocation);
    }

    [Fact]
    public async Task Change_password_requires_sign_in_and_a_different_valid_password()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("asha@example.com", "OldPassword1");

        var signedOut = await h.Service.ChangePasswordAsync(new ChangePasswordRequest("OldPassword1", "BrandNew-77"), null);
        h.User.SignIn(user);
        var same = await h.Service.ChangePasswordAsync(new ChangePasswordRequest("OldPassword1", "OldPassword1"), null);
        var weak = await h.Service.ChangePasswordAsync(new ChangePasswordRequest("OldPassword1", "short"), null);

        Assert.Equal(AccountStatus.Unauthorized, signedOut.Status);
        Assert.Equal(AccountStatus.InvalidRequest, same.Status);
        Assert.Equal(AccountStatus.InvalidRequest, weak.Status);
        Assert.Equal("hash:OldPassword1", h.Store.PasswordHash(user));
    }

    [Fact]
    public async Task Without_a_refresh_token_every_session_is_signed_out()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("asha@example.com", "OldPassword1");
        h.User.SignIn(user);

        await h.Service.ChangePasswordAsync(new ChangePasswordRequest("OldPassword1", "BrandNew-77"), null);

        Assert.Equal((user, (string?)null), h.Store.LastRevocation);
    }

    // ---------- profile ----------

    [Fact]
    public async Task Profile_reads_and_updates_only_the_signed_in_customer()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("asha@example.com", "x");
        var other = h.Store.AddCustomer("ravi@example.com", "x");
        h.User.SignIn(user);

        var updated = await h.Service.UpdateProfileAsync(new UpdateCustomerProfileRequest("  Asha  Kulkarni ", "+91 98765-43210"), CancellationToken.None);
        var read = await h.Service.GetProfileAsync();

        Assert.True(updated.Succeeded);
        Assert.Equal(("Asha  Kulkarni".Replace("  ", " "), "asha@example.com", "+919876543210"), (read.Value!.FullName.Replace("  ", " "), read.Value.Email, read.Value.PhoneNumber));
        Assert.Equal("ravi@example.com", h.Store.ProfileOf(other).Email);
        Assert.Null(h.Store.ProfileOf(other).PhoneNumber);
    }

    [Fact]
    public async Task Profile_requires_sign_in_and_valid_input()
    {
        var h = new Harness();
        var user = h.Store.AddCustomer("asha@example.com", "x");

        var signedOut = await h.Service.GetProfileAsync();
        h.User.SignIn(user);
        var badName = await h.Service.UpdateProfileAsync(new UpdateCustomerProfileRequest("A", null), CancellationToken.None);
        var badPhone = await h.Service.UpdateProfileAsync(new UpdateCustomerProfileRequest("Asha Patil", "abc"), CancellationToken.None);

        Assert.Equal(AccountStatus.Unauthorized, signedOut.Status);
        Assert.Equal(AccountStatus.InvalidRequest, badName.Status);
        Assert.Equal(AccountStatus.InvalidRequest, badPhone.Status);
    }

    // ---------- fakes ----------

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => now;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FakeHashing : IPasswordHashing
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string hash, string password) => hash == "hash:" + password;
    }

    private sealed class FakeEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("smtp down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuth : IAuthenticationService
    {
        public LoginRequest? LastLogin { get; private set; }
        public string? LastIp { get; private set; }

        public Task<AuthenticationResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
        {
            LastLogin = request;
            LastIp = ipAddress;
            return Task.FromResult(AuthenticationResult.Success(new AuthenticationResponse("signed-in", "refresh", 900, "Bearer", new AuthenticationUserResponse(Guid.NewGuid(), "x", request.Username, OrgId, ["Customer"], []))));
        }

        public Task<AuthenticationResult> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthenticationUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; private set; }
        public string? UserId { get; private set; }
        public Guid? OrganizationId => OrgId;
        public Guid? StoreId => null;
        public IReadOnlyCollection<string> Roles => ["Customer"];
        public IReadOnlyCollection<string> Permissions => [];

        public void SignIn(Guid userId)
        {
            IsAuthenticated = true;
            UserId = userId.ToString();
        }
    }

    private sealed class FakeStore : IAccountStore
    {
        private sealed class Account
        {
            public Guid Id = Guid.NewGuid();
            public string Email = "";
            public string FullName = "";
            public string? Phone;
            public string Hash = "";
            public bool Active = true;
            public bool LockedOut;
        }

        public sealed record Code(Guid Id, Guid UserId, string CodeHash, DateTime CreatedUtc, DateTime ExpiresUtc, int FailedAttempts, bool Used);

        private readonly List<Account> accounts = [];
        private readonly List<Code> codes = [];

        public List<NewCustomerAccount> Created { get; } = [];
        public IReadOnlyList<Code> Codes => codes;
        public bool ForceCreateRace { get; set; }
        public bool ConsumeAlwaysFails { get; set; }
        public string? OrganizationError { get; set; }
        public Guid? ConfiguredOrganizationSeen { get; private set; }
        public (Guid UserId, string? ExceptHash) LastRevocation { get; private set; }

        public Guid AddCustomer(string email, string password)
        {
            var account = new Account { Email = email, FullName = "Test User", Hash = "hash:" + password };
            accounts.Add(account);
            return account.Id;
        }

        public void Deactivate(Guid id) => accounts.Single(a => a.Id == id).Active = false;
        public void LockOut(Guid id) => accounts.Single(a => a.Id == id).LockedOut = true;
        public bool IsLockedOut(Guid id) => accounts.Single(a => a.Id == id).LockedOut;
        public string PasswordHash(Guid id) => accounts.Single(a => a.Id == id).Hash;
        public CustomerProfileResponse ProfileOf(Guid id) { var a = accounts.Single(x => x.Id == id); return new(a.Id, a.FullName, a.Email, a.Phone); }

        public Task<OrganizationChoice> ResolveRegistrationOrganizationAsync(Guid? configured, CancellationToken ct = default)
        {
            ConfiguredOrganizationSeen = configured;
            return Task.FromResult(OrganizationError is null ? new OrganizationChoice(configured ?? OrgId, null) : new OrganizationChoice(null, OrganizationError));
        }

        public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(accounts.Any(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> TryCreateCustomerAsync(NewCustomerAccount account, CancellationToken ct = default)
        {
            if (ForceCreateRace)
            {
                return Task.FromResult(false);
            }

            Created.Add(account);
            accounts.Add(new Account { Email = account.Email, FullName = account.FullName, Phone = account.PhoneNumber, Hash = account.PasswordHash });
            return Task.FromResult(true);
        }

        public Task<AccountUserInfo?> FindActiveCustomerByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(accounts.Where(a => a.Active && a.Email.Equals(email, StringComparison.OrdinalIgnoreCase)).Select(a => new AccountUserInfo(a.Id, a.Email, a.FullName)).FirstOrDefault());

        public Task<AccountUserInfo?> FindActiveCustomerByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(accounts.Where(a => a.Active && a.Id == id).Select(a => new AccountUserInfo(a.Id, a.Email, a.FullName)).FirstOrDefault());

        public Task<int> CountResetCodesSinceAsync(Guid userId, DateTime since, CancellationToken ct = default) => Task.FromResult(codes.Count(c => c.UserId == userId && c.CreatedUtc >= since));

        public Task CreateResetCodeAsync(Guid userId, string hash, DateTime created, DateTime expires, string? ip, CancellationToken ct = default)
        {
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].UserId == userId && !codes[i].Used)
                {
                    codes[i] = codes[i] with { Used = true };
                }
            }

            codes.Add(new Code(Guid.NewGuid(), userId, hash, created, expires, 0, false));
            return Task.CompletedTask;
        }

        public Task<ResetCodeInfo?> GetOpenResetCodeAsync(Guid userId, DateTime now, int maxAttempts, CancellationToken ct = default) =>
            Task.FromResult(codes.Where(c => c.UserId == userId && !c.Used && c.ExpiresUtc > now && c.FailedAttempts < maxAttempts).OrderByDescending(c => c.CreatedUtc).Select(c => new ResetCodeInfo(c.Id, c.CodeHash, c.FailedAttempts)).FirstOrDefault());

        public Task RecordFailedResetAttemptAsync(Guid codeId, CancellationToken ct = default)
        {
            var i = codes.FindIndex(c => c.Id == codeId);
            codes[i] = codes[i] with { FailedAttempts = codes[i].FailedAttempts + 1 };
            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeResetCodeAsync(Guid codeId, DateTime now, CancellationToken ct = default)
        {
            var i = codes.FindIndex(c => c.Id == codeId);
            if (ConsumeAlwaysFails || codes[i].Used)
            {
                return Task.FromResult(false);
            }

            codes[i] = codes[i] with { Used = true };
            return Task.FromResult(true);
        }

        public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<string?>(accounts.Single(a => a.Id == userId).Hash);

        public Task SetPasswordAsync(Guid userId, string hash, DateTime now, CancellationToken ct = default)
        {
            var account = accounts.Single(a => a.Id == userId);
            account.Hash = hash;
            account.LockedOut = false;
            return Task.CompletedTask;
        }

        public Task RevokeSessionsAsync(Guid userId, string? exceptHash, string? ip, DateTime now, CancellationToken ct = default)
        {
            LastRevocation = (userId, exceptHash);
            return Task.CompletedTask;
        }

        public Task<CustomerProfileResponse?> GetProfileAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(accounts.Where(a => a.Id == userId).Select(a => (CustomerProfileResponse?)new CustomerProfileResponse(a.Id, a.FullName, a.Email, a.Phone)).FirstOrDefault());

        public Task UpdateProfileAsync(Guid userId, string fullName, string first, string last, string? phone, CancellationToken ct = default)
        {
            var account = accounts.Single(a => a.Id == userId);
            account.FullName = fullName;
            account.Phone = phone;
            return Task.CompletedTask;
        }
    }
}
