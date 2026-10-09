# MolBhav — Development Playbook (how we build, so Claude can repeat it)

**As of:** 6 Oct 2026 · **Audience:** Claude (Chat / Claude Code) starting a new feature or a new app on the same stack · **Companions:** `ARCHITECTURE.md` (what exists), `PROGRESS.md` (status), `OPERATIONS.md` (commands/secrets), `PROMPT_ARCHIVE.md` (past prompts)

> How to use: paste §2 (Standing context) + the §6 prompt template at the start of every Claude Code task. Point Claude at this file when starting a new app: "Follow `DEVELOPMENT_PLAYBOOK.md`."

---

## 1. Working agreement (owner preferences)
- Short, direct answers. No filler; lead with code or the actionable step.
- **System design → give 2–3 options + recommendation, wait for confirmation** before building. Clear bug/UI fixes → just do them.
- Claude Code prompts use one format: **Model + Skills → Root cause pinned to exact file → Changes → Done-when**.
- Production-ready, fully typed C#/Dart; no placeholders, no omitted logic; full files, not snippets.
- The device shell has no dotnet/flutter: **the owner runs build/test/migrate/gen-l10n and pastes output**. Always end with the exact commands to run.
- Never ask the owner to paste secrets. If one leaks in chat, say so and require rotation.

## 2. Standing context block (paste into every prompt)
```
API:  source/api — .NET 10, Clean Architecture (Domain→Application→Infrastructure→Api), CQRS/MediatR,
      EF Core writes + Dapper reads, PostgreSQL schema per module, Result<T>/Error, ApiResponse<T>,
      RFC 7807, FluentValidation, JWT + rotating refresh, /api/v1, admin policy, UUIDv7, snake_case, xmin.
App:  source/app — Flutter, flutter_bloc cubits + injectable/get_it (NOT Riverpod), go_router, Dio +
      EnvelopeInterceptor, watchCachedApiCall/ResponseCache, ARB en/hi/mr, Mb* design-system widgets.
      injection.config.dart is edited BY HAND (no build_runner) — add the registration + alias yourself.
Rules:
- Mirror the closest sibling file (naming, folders, config, repo, read service, controller, .http).
- Rich domain; no logic in controllers. One migration per logical change; new columns nullable/defaulted.
- Secrets only in user-secrets/env vars/Key Vault. Dev-safe fallback for every provider; fail closed outside Development.
- No hardcoded user-facing strings: ARB in en + hi + mr (and Localization keys for backend text).
- Admin role only via DB (identity.users.role); signup always User. Never auto-link Google accounts by phone/email.
- package_info_plus stays ^10.2.1.
- Tests required: xUnit (happy + failure per handler), Dart tests for cubits/pure helpers.
- Done = dotnet build 0/0, dotnet test, flutter gen-l10n, flutter analyze clean, flutter test; list changed files + manual steps.
```

## 3. The build order that worked (reuse for a new app)
| # | Phase | What | Gate before next |
|---|---|---|---|
| 0 | Decide | Read BRD + ARCHITECTURE; confirm modules, categories, languages, auth methods | Owner confirms options |
| 1 | Foundation | Solution layout, `Result<T>`, envelope, ProblemDetails, JWT + refresh rotation, EF + snake_case, health, Swagger, `.http` files | Build/test green, login works |
| 2 | Core data | Reference data (states, districts, markets, products), price ingestion (per category), CSV upload, caching read services | Real data visible in app |
| 3 | App shell | Theme/`Mb*` widgets, go_router, DI, ARB ×3, cached API calls, onboarding + profile | analyze/test green; device pass |
| 4 | Engagement | Watchlist, alert rules, alerts feed, reports (PDF/CSV), cost estimator | Alerts fire on test data |
| 5 | Identity providers | OTP (MSG91), password, Google sign-in — each with dev fallback | Works in Dev without secrets |
| 6 | Notifications | Device tokens, preferences, FCM + WhatsApp keyed senders, composite sender | Log sender in Dev; real send verified |
| 7 | Money | Razorpay orders, coupons, subscription gating, webhook (HMAC) | Stub in Dev; test-mode payment |
| 8 | Support/share | Support module, FAQ, share + deep links | — |
| 9 | Intelligence | Weather, buying opportunities (daily scan, landed cost), admin scan | Manual scan returns rows |
| 10 | Hardening & hosting | Rate limits, CORS, backups, Docker, Nginx, CI/CD (`OPERATIONS.md` §7) | Health check 200 in staging |

Rule: **one phase = one Claude Code prompt (or 2–3 sessions for big ones)**; never mix a feature with a refactor.

## 4. Per-feature workflow (the loop)
1. **Read first** — `ARCHITECTURE.md` + the closest sibling feature in the repo. Verify any design doc against the code (old Hangfire/Open-Meteo/Riverpod docs caused drift).
2. **Options + confirm** (system-design only) — 2–3 options, trade-offs, recommendation, ask once.
3. **Write the prompt** (§6) — root cause pinned to exact files; list files to create/change.
4. **API slice** — Domain entity/value objects → Application command/query + validator + handler (+ `IUnitOfWork`) → Infrastructure repo/Dapper read service/provider → Controller + `.http` → one EF migration → xUnit tests.
5. **App slice** — domain entity → data (API + mapper, `runCachedApiCall`/`watchCachedApiCall`) → cubit + state → page using `Mb*` widgets → route → DI registration **and** `injection.config.dart` edit → ARB keys in en/hi/mr → Dart tests.
6. **Verify** (owner runs): `dotnet build; dotnet test; dotnet ef database update` → `flutter pub get; flutter gen-l10n; dart format lib test; flutter analyze; flutter test` → live check on device/Chrome.
7. **Record** — update `PROGRESS.md` (dated entry, migration name, manual steps); add to `PROMPT_ARCHIVE.md` if a prompt was reusable; update `ARCHITECTURE.md` only if a design changed.

## 5. Conventions & patterns to copy
**API**
- Folder per module: `Domain/<Module>`, `Application/<Module>/{Commands,Queries,Dtos}`, `Infrastructure/<Module>`; schema per module in PostgreSQL.
- Errors via `Error`/`Result<T>` — never throw for expected failures; global middleware → RFC 7807.
- Provider abstraction (`IOtpSender`, `INotificationSender`, `IPaymentGateway`): real impl + logging/stub impl, chosen by config; **outside Development an unconfigured provider fails startup/closed**.
- Notifications: `CompositeNotificationSender` → keyed senders per channel; options sections `Push:*`, `Billing:Razorpay:*`, `OtpDelivery:Msg91:*`, `Authentication:Google:*`.
- Background work: hosted service + outbox → `IDomainEventHandler`; dates via `IngestionDates.TodayIst` (IST).
- Reads use Dapper services; writes use EF + `UnitOfWorkBehavior`.
- Every new endpoint gets: authorization attribute, FluentValidation, `.http` sample, test.

**App**
- Cubits only; state immutable (Equatable); `@lazySingleton`/`@injectable` + hand-added entry in `injection.config.dart` (aliases `_i9001…` series — pick the next free one).
- Strings only from ARB; after adding keys always `flutter gen-l10n` (stale generated getters = `undefined_getter` errors).
- Reuse `Mb*` widgets; long text must tolerate Devanagari (ellipsis/Flexible) — `RenderFlex overflowed` almost always = unbounded Row child.
- Lists with filters: load once, filter locally, show counts (avoids refetch bugs).
- Settings toggles persist via `SharedPreferences` cubits (example: `HomeWeatherAnimationCubit`).

**Database**
- 3NF, explicit FKs, indexes with a rationale comment, enums as text, snake_case, `xmin` concurrency, UUIDv7.
- One migration per logical change, descriptive name (`AddMarketOpportunities`, `KeepAlertsWhenRuleDeleted`).

**Secrets**
- Dev: `dotnet user-secrets`; Prod: env vars (`Section__Key`) / Key Vault. Never in `appsettings*.json`, chat, or commits. Feature flags that enable risky integrations stay `false` in appsettings (e.g. `Authentication:Google:Enabled`).
- Rotate anything ever pasted in chat before production.

## 6. Claude Code prompt template
```
MODEL: claude-sonnet-5-5 (use opus for design/refactor across modules)
SKILLS: <e.g. engineering:documentation, figma:figma-design-to-code, none>

CONTEXT: <paste §2 standing block>

GOAL: <one sentence>

ROOT CAUSE / CURRENT STATE: <file:line — what is wrong or missing, evidence>

CHANGES
API:  <files to create/change, behaviour, migration name>
App:  <files, widgets, ARB keys (en/hi/mr), DI alias>
Tests: <cases>

CONSTRAINTS: <what must not change>

DONE WHEN
- dotnet build 0 warnings/0 errors; dotnet test green
- flutter gen-l10n; flutter analyze clean; flutter test green
- Migration <Name> generated; manual steps listed
- PROGRESS.md entry added
REPORT: changed files, commands I must run, risks.
```

## 7. Definition of done (checklist)
- [ ] Builds/tests/analyzer clean (owner-confirmed output)
- [ ] Migration applied locally; rollback considered
- [ ] Works in Dev **without** third-party secrets (fallback path)
- [ ] ARB keys in en + hi + mr; `gen-l10n` run
- [ ] No secret, phone number or OTP in logs/commits
- [ ] `.http` sample + tests for new endpoints
- [ ] `PROGRESS.md` updated; docs match code

## 8. Pitfalls we hit (don't repeat)
| Pitfall | Rule |
|---|---|
| Prompt assumed Riverpod; app uses cubits | State the stack in every prompt |
| Design docs from other chats drifted from code | Verify against the repo first |
| Early prompts said "no tests" | Tests are mandatory |
| `undefined_getter` after adding ARB keys | Run `flutter gen-l10n` |
| WhatsApp options read from wrong section | Section is `Push:WhatsApp:*`; `[STUB WhatsApp …]` log = keys not picked up; restart after change |
| WhatsApp "Sent" but not received | Meta HTTP 200 = accepted only. Check recipient allow-list (test number), template category/language/params, payment method, and a webhook for delivery status; log wamid |
| Temporary WhatsApp token expired (401 code 190) | Use a permanent system-user token |
| Razorpay Orders API never sends `subscription.*`; webhook HMAC ≠ checkout signature | Use order/payment events; verify each signature separately |
| Opportunities empty for normal users | Profile state/district text must match `market.states/districts`; need watchlist + fresh price in home district; scan runs daily after 09:00 IST |
| Admin-only features leaking to users | Admin only via DB role + policy; user-facing action must be a separate, rate-limited endpoint |
| Google link failures | Needs Web + Android client IDs, SHA-1 (debug + release), `ServerClientId` set; never auto-link by email/phone |
| Hand-edited DI forgotten | Every new injectable needs an `injection.config.dart` entry |
| Layout overflow with long market names | Flexible + ellipsis on badge/label rows |
| Dev tool unavailable (device bridge down) | Write full file in cloud, deliver via SendUserFile, commit when reachable |

## 9. Starting a new app / category on this foundation
1. Re-read `PRODUCT.md` + BRD; confirm category code, data source, languages.
2. Reuse the shared kernel (auth, notifications, billing, localization); add a **new module + schema**, not a new service.
3. Add category-scoped ingestion schedule (source belongs to category) — never agriculture-only assumptions.
4. Seed reference data + CSV import for categories without a live feed.
5. Clone nearest sibling feature; follow §4 loop; keep §2 block unchanged.
6. Staging deploy per `OPERATIONS.md` §7 before store submission.

## 10. Suggested additions (not yet done)
- A reusable Claude Code **skill** that loads §2 + §6 + §7 automatically.
- Meta WhatsApp delivery-status webhook (unblocks diagnosing "Sent" ≠ delivered).
- Refresh `TIMELINE.html` and add a BRD summary section to `PRODUCT.md`.
- CI job running `dotnet test` + `flutter analyze/test` so "owner pastes output" becomes automatic.
