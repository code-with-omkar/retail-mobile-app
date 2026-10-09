# Mini-plan: Phase P2 — customer accounts (email and password)

**Status:** Proposed. **Nothing has been changed yet.** Approve it and I implement in the order below, with stop points before anything touches your database.
**Date:** 2026-10-07
**Follows:** [CHANGE_PROTOCOL.md](../CHANGE_PROTOCOL.md) and `copilot-instructions.md` section 30. This work **extends** authentication; it does not change it. The existing `login`, `refresh`, `logout` and `me` routes, the JWT design, refresh-token rotation and the 5-failure lockout stay exactly as they are, so the admin portal is unaffected.

## Decisions already made

| # | Decision |
| --- | --- |
| D1 | Email and password first; phone OTP and Google/Apple come in P8. |
| D20 | Password reset uses an **emailed code typed into the app** (30 minutes, 5 attempts), not a link. |
| D21 | **No email verification** at registration in P2. |
| D22 | New customers join the **configured organization** (`Registration:OrganizationId`), otherwise the only active one. If several are active and none is configured, registration fails with a clear configuration error. |
| D23 | **Open:** the real email provider (Gmail SMTP, SendGrid, SES, your own server). P2 is built and tested with a development mailbox and a swappable sender. |

## What I found

- **There is no production way to set a password today.** Only `DevelopmentAuthenticationSeedService` creates credentials. In your dev database 3 of 8 users have one. Users created by an admin get none, so they cannot log in.
- Login accepts email or username, checks `LockedUntil`, locks for 15 minutes after 5 failures, and issues a JWT plus a rotating refresh token. Good; kept.
- `Users` has **no phone field**. The `Email` index is **not unique**; the unique one is on `ExternalSubject`, which holds the lowercased email.
- A customer is a `User` plus a `Customer` row plus a `UserRole` ("Customer"), always inside one organization.
- No email sender and no rate limiting exist anywhere.

## What will change and why

The app can only sign in a handful of seeded demo users. P2 lets a real customer create an account, sign in, recover a forgotten password and stay signed in.

### New API routes (all additive)

| Route | Auth | Purpose |
| --- | --- | --- |
| `POST /api/auth/register` | anonymous | Create customer, return tokens (signed in immediately) |
| `POST /api/auth/forgot-password` | anonymous | Email a one-time code. **Always answers 200** so it never reveals whether an email exists |
| `POST /api/auth/reset-password` | anonymous | `{ email, code, newPassword }` → sets password |
| `POST /api/auth/change-password` | signed in | `{ currentPassword, newPassword }` |
| `GET /api/customer/profile`, `PUT /api/customer/profile` | customer | Name and phone; email shown read-only |

```jsonc
// POST /api/auth/register
{ "fullName": "Asha Patil", "email": "asha@example.com", "password": "••••••••", "phoneNumber": "9876543210" }
// 200 -> same shape as login: { accessToken, refreshToken, expiresIn, tokenType, user }
// 400 field errors | 409 email already registered | 429 too many attempts
```

Behaviour:
- **Password policy (server-side):** 8 to 128 characters, not equal to the email, and not on a short list of the most common passwords. No composition rules (they push people to weak, predictable passwords).
- **Duplicate email:** returns 409 "An account with this email already exists." This reveals that the email is registered. That is the usual trade-off without email verification (D21); the real owner recovers with reset.
- **Reset:** a code is only sent for **active customer accounts**. Staff and admin password flows are a separate decision. A successful reset sets the password, clears any lockout, **revokes every refresh token**, and marks the code used. It does not sign the user in.
- **Reset code:** 8 characters from an unambiguous alphabet (about 40 bits), shown as `ABCD-EFGH`, valid 30 minutes, at most 5 wrong attempts, then dead. Stored only as an **HMAC-SHA256 keyed with a server secret** (a plain hash of a 40-bit code could be brute-forced if the table leaked). Requesting a new code invalidates earlier ones; at most 3 requests per account per hour.
- **Email text** is plain, in English or Marathi following `Accept-Language`.
- **Unknown-email logins** get a dummy password check so response time does not reveal which emails exist.

### Abuse protection

- ASP.NET Core's built-in rate limiter (no new package) on the anonymous auth routes: per client IP, for example login 10 per minute, register 5 per hour, forgot-password 5 per hour, reset-password 10 per hour. Returns 429 with `Retry-After`.
- **Important for IIS behind a load balancer:** the limiter must see the real client IP, so forwarded headers need a configured trusted-proxy list (`Proxy:KnownProxies`). Without it every user would share the balancer's IP and one busy minute would block everyone. Limiter counters are **per server instance** (no shared cache, per your "no Redis" rule), so the effective limit is multiplied by the instance count. The per-account limits above are stored in SQL, so they hold across instances.

### Email sending

`IEmailSender` in Application, with two implementations in Infrastructure chosen by `Email:Mode`:
- `Log` (Development only): writes the email, including the code, to the log and a local dev outbox file, so a code can be read without any mail server. **The API refuses to start with `Log` outside Development.**
- `Smtp`: uses the built-in `System.Net.Mail` client with host, port, TLS and sender from configuration; the password comes from an environment variable or secret store, never source control. (MailKit is the better library but would be a new package; revisit when D23 is decided.)

## Database changes (additive, one migration: `AddCustomerAccountSupport`)

- `Users.PhoneNumber nvarchar(20) NULL`. Not unique and unverified for now; verification and uniqueness come with P8.
- New table `PasswordResetCodes (Id, UserId, CodeHash, CreatedAt, ExpiresAt, FailedAttempts, UsedAt, RequestedFromIp)` with a foreign key to `Users` and an index on `(UserId, CreatedAt)` for the per-account limit.
- Nothing dropped, renamed or rewritten. `Down` removes only these. Your 8 existing users and their data are untouched.

## Files likely to change

| Layer | Files |
| --- | --- |
| Domain | `Class1.cs`: `User.PhoneNumber`; new `PasswordResetCode` |
| Application | New DTOs; validators (register, forgot, reset, change password, profile); `PasswordPolicy`; `IEmailSender`; `ICustomerAccountService`; email templates |
| Infrastructure | `CustomerAccountService` (EF and password hasher, same style as `AuthenticationService`); `LoggingEmailSender`, `SmtpEmailSender`; configurations; `DbContext`; one migration and snapshot |
| API | `AuthenticationController`: four new routes; new `CustomerProfileController`; rate limiter, forwarded-headers and email/registration settings (validated at startup) in the existing registration extension; `appsettings.json` sections |
| Tests | New tests (below). Existing tests untouched |
| Docs | Re-export `openapi.json`; `docs/api/README.md`; a short operations note for email and proxy settings |
| Mobile | `AuthRepository`, secure token storage, refresh-once interceptor, session restore, wired Login / Register / Forgot screens, new Reset screen, auth gate, profile, tests |
| Admin portal | none |

## Mobile work

- **Token storage:** `flutter_secure_storage` (Android Keystore) for the refresh token. This is a **new package**; no existing dependency does this and storing tokens in plain preferences would be unsafe. The access token stays in memory.
- **Session:** restore on launch; on a 401 refresh once (one refresh at a time, even if several requests fail together), and sign out if refresh fails.
- **Screens wired:** Login, Register (name, email, password, phone), Forgot password, and a new "enter your code and new password" screen. Server field errors show next to the field; lockout and rate-limit messages are translated to Marathi.
- **Gate:** guests can browse; sign-in is required at checkout, Orders and Profile, returning to where they were.
- **Welcome (phone first):** until OTP exists (P8) the phone field starts registration with the number filled in, and Google/Apple show "coming soon". The "Demo User" shortcuts are removed.
- **Profile:** real name and email; Log out revokes the token on the server and clears storage.

## Risks and how they are handled

| Risk | Handling |
| --- | --- |
| Account takeover through weak reset | HMAC-keyed single-use code, 30-minute expiry, 5 attempts, per-account hourly cap, revokes all sessions on success |
| Email enumeration | Forgot-password always answers 200; unknown-email login does a dummy hash check. Registration does reveal an existing email (accepted, D21) |
| Rate limiter blocks everyone behind the balancer | Forwarded headers with a trusted-proxy list, documented and validated for non-Development |
| Reset code leaks into logs | `Log` mode exists only in Development and the API refuses it elsewhere; codes never logged in `Smtp` mode |
| Customer lands in the wrong organization | D22 rule; clear startup/config error when ambiguous |
| Existing login behaviour changes | It does not; existing auth tests stay as they are and are re-run |
| Squatted email (no verification) | The real owner can reset and take the account; recorded as a known limitation |
| Third-party setup lead time | Email provider decision D23 is not needed to build or test P2 |

## Order of work and stop points

1. Application layer: DTOs, validators, password policy, interfaces. Tests.
2. Infrastructure: services, email senders, entities, configurations. Tests.
3. Generate the migration. **Stop: you read it.** Test the whole chain and the rollback on a throwaway database, then drop it.
4. API routes, rate limiter, settings. `dotnet build` / `dotnet test`.
5. **Stop: you approve applying the migration to `retail-mobile-app`.** Backup first, as before. Then a live check using the development mailbox.
6. Mobile work, `check.ps1`, smoke tests, and a look at the app signing up and resetting a password.

## Tests

- **Policy and validators:** length limits, common passwords, password equal to email, phone format, name and email rules.
- **Register:** creates `User`, `Customer`, credential (hashed), role; duplicate email returns conflict; organization rule (configured, single active, ambiguous); returns usable tokens; case-insensitive email.
- **Reset:** code expiry, single use, wrong-code attempts then lockout of the code, new request invalidates the old, hourly cap, success revokes refresh tokens and clears lockout, unknown or inactive account sends nothing, staff accounts are not eligible, response never differs between known and unknown emails.
- **Change password:** wrong current password; revokes other sessions.
- **Security:** no secrets or codes in logs; `Log` email mode rejected outside Development; existing login, lockout and refresh tests unchanged.
- **Rate limiting:** 429 with `Retry-After`; forwarded headers honoured only from trusted proxies.
- **Persistence:** SQL Server integration test (opt-in, disposable database) for registration, reset and the unique index race.
- **Mobile:** repository, token store, refresh-once under concurrent 401s, session restore, redirect gate, screen states and field errors, Marathi coverage.

## Not included (recommended separately)

- **Account deletion.** Google Play requires apps that let people create accounts to offer account deletion (in the app and via a web link). Not part of P2, but it is a release requirement, so I added task 9.11.
- Email verification, phone verification, OTP and social sign-in (P8).
- Password reset for staff and admins, and a way to activate admin-created users. That needs its own decision about approvals.
- Account lockout notifications, device management and sign-out-everywhere screens.

## Definition of done

Tests and analyzers pass; migration reviewed, tested on a throwaway database and applied to your dev database with a backup; contract re-exported; a new customer can register, log in, reset a forgotten password with the emailed code, and stay signed in across app restarts on a real device or emulator.
