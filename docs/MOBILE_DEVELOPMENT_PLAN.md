# QuickCart Mobile — Development Plan

Plan for taking the customer app from a UI prototype on seed data to a released Android app backed by the real API.

- **Task-level tracking:** open [progress-dashboard.html](./progress-dashboard.html). It holds all phases, tasks, acceptance criteria, risks and decisions, and saves your progress in the browser.
- **Starting point:** [PROJECT_PROGRESS.md](./PROJECT_PROGRESS.md) and [TECHNICAL_OVERVIEW.md](./TECHNICAL_OVERVIEW.md).
- **Engineering rules:** [copilot-instructions.md](./copilot-instructions.md). Every task below follows them.
- **Plan date:** 2026-10-07.

## Where we are

The Flutter app has every customer screen built in the Electric Yellow design (light and dark, English and Marathi) on local seed data. Nothing talks to the API yet. Sign-in, OTP, Google/Apple, the map and payments are UI only.

## What the API offers today

Checked against `src/QuickCommerce.Api/Controllers` and `Application/DTOs`.

| App need | API today | Phase |
| --- | --- | --- |
| Customers listing/searching products | **Missing.** `GET /api/products` requires the admin-read policy. | P1 |
| Product detail, categories, nearest store | Public, but no MRP, Marathi names, availability or ETA | P1 |
| Product images | `ImageUrl` field only; no upload or serving | P1 |
| Customer registration, password reset | **Missing.** Only admins create users. | P2 |
| Login, refresh, logout, me | Ready (`Username` + `Password`) | P2 |
| Pack sizes (250 g / 500 g / 1 kg) | **Missing.** Cart is keyed by product; one price per product. | P3 |
| Saved addresses | **Missing.** Checkout takes an address string and coordinates. | P4 |
| Cart, checkout, orders, notifications | Ready; checkout reports price and inventory conflicts | P5–P6 |
| Fees and free-delivery threshold | **Missing.** Constants in the app today. | P5 |
| Online payments, phone OTP, Google/Apple | **Missing** | P7, P8 |

Full list with notes: the **Gaps and risks** tab in the dashboard.

## Decisions made

| # | Decision |
| --- | --- |
| D1 | Email/password accounts first; phone OTP and social sign-in later (P8). Keeps the existing JWT design. |
| D2 | Add `ProductVariant` with an additive migration, with a default variant so old clients keep working. |
| D3 | Online UPI/card payments are part of the first release. |
| D4 | First release is Android only. iOS is backlog. |
| D5 | Real English/Marathi localization (UI done; backend translations in P1). |
| D6 | Riverpod and go_router; theme follows the system with a manual override. |
| D8 | Customer product listing is a new public route with a customer DTO: `GET /api/catalog/products`. |
| D20 | Password reset uses an emailed code typed into the app. |
| D21 | No email verification at registration in P2. |
| D22 | New customers join the configured organization, else the only active one. |
| D13 | Keep SQL Server as the database ([ADR-002](./adr/ADR-002-database-engine-postgresql.md)). |
| D12 | Hand-written DTOs, no code generation ([ADR-001](./adr/ADR-001-mobile-data-layer.md)). |

## Open decisions

| # | Needed before | Question |
| --- | --- | --- |
| D7 | P8 | Approve extending authentication for OTP and social login. `copilot-instructions.md` requires confirmation before changing authentication. |
| D9 | P7 | Payment provider: **Razorpay** (chosen 2026-10-09). |
| D10 | P6 | Push notifications (FCM) in v1? It is a new external service. |
| D11 | P9 | Crash reporting tool (Firebase Crashlytics or Sentry). |

## Phases

Effort uses points (S = 1, M = 3, L = 5). Points show relative size, not dates. Enter your real pace in the dashboard to get a forecast.

| Phase | Goal | Needs | Tasks | Points |
| --- | --- | --- | --- | --- |
| UI | Design system and all screens on seed data (**done**) | — | 5 | 15 |
| P0 | Foundations and contracts: real API running, environment config, Dio client, repositories, Android toolchain, test gate | UI | 9 | 13 |
| P1 | Public catalog and store discovery: public product API, MRP, Marathi names, images, availability, ETA; app on real data | P0 | 12 | 36 |
| P2 | Customer accounts: register, login, password reset, secure sessions, guest browsing | P0 | 11 | 23 |
| P3 | Product variants: migration, API, admin portal, app pack sizes | P1 | 7 | 25 |
| P4 | Addresses and serviceability: saved addresses, Google Maps, "not serviceable" handling | P2 | 7 | 17 |
| P5 | Cart and checkout (cash on delivery): store-aware cart, server fees, every conflict path | P2, P3, P4 | 7 | 23 |
| P6 | Orders, tracking, notifications: history, polling status, bell badge, optional push | P5 | 8 | 20 |
| P7 | Online payments: provider, payment entity, signed idempotent webhooks, reservation, refunds | P5 | 9 | 35 |
| P8 | Phone OTP and Google/Apple sign-in | P2 | 7 | 23 |
| P9 | Quality and Android release: end-to-end tests, accessibility, performance, signing, backend hardening, Play internal testing | P6, P7 | 11 | 35 |

### Suggested order

```text
UI ─► P0 ─┬─► P1 ─► P3 ─┐
          └─► P2 ─► P4 ─┴─► P5 ─┬─► P6 ─┐
                  │             └─► P7 ─┴─► P9
                  └─► P8 (parallel, after D7 is approved)
```

P1 and P2 can run in parallel. P3 must finish before the app cart is wired in P5, because adding variants later would force a rework of cart, checkout and orders.

## Why this order

- **Catalog before accounts:** it unblocks the most visible screens, and it needs a backend change customers cannot work around (product listing is admin-only).
- **Variants before cart:** cart lines, inventory and order snapshots all depend on variant IDs.
- **Payments after a working COD order:** it keeps the first end-to-end order small and testable, and payments add the riskiest backend work.
- **OTP and social last:** they need provider accounts and an authentication-architecture approval, so they should not block shopping.

## Guardrails (from `copilot-instructions.md`)

- Smallest safe change; write a mini-plan before editing and a summary after.
- Database changes are additive and reviewable; never auto-run migrations in production.
- Keep API contracts backward compatible and check the admin portal for every change.
- Enforce authorization and store/organization scope on the server.
- Orders keep price snapshots; inventory changes must be safe under concurrent orders.
- Stay stateless across IIS instances (for example, OTP codes live in SQL, not memory).
- No new infrastructure (Redis, Kafka, SignalR and similar) without a demonstrated need.
- Add tests for pricing, inventory, authorization and orders. Do not edit tests just to make them pass.

## Main risks

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Auth architecture change needs approval (P8) | High | Written approval first; email/password ships earlier. |
| Variants touch cart, inventory, orders (P3) | High | Additive migration, default variant, concurrency and compatibility tests before UI work. |
| Payments: money, webhooks, PCI scope (P7) | High | Provider-hosted payment UI, server-side amounts, signed idempotent webhooks, reconciliation. |
| Public catalog route could leak admin-only data (P1) | Medium | New customer DTO and route; review every field. |
| API changes break the admin portal | Medium | Backward-compatible changes; admin impact check per task. |
| Third-party setup lead time (SMS, Google Cloud, Apple, payment KYC, Play) | Medium | Start account setup early, in parallel. |
| CORS open to any origin, secrets, unverified production database | Medium | Covered by task 9.7 before release. |
| Marathi translations are unreviewed | Low | Native review in P9; English fallback stays. |

## Using the dashboard

1. Open `docs/progress-dashboard.html` in a browser (double-click works; no server needed).
2. Set a task's status, tick its "Done when" criteria, add notes. A task marked Done ticks all its criteria. Progress for tasks not yet Done counts the ticked criteria.
3. Changes are saved in your browser only. Use **Export progress** to save a file, **Import** to load it elsewhere, and **Copy summary** for a standup or status update.
4. To make progress permanent in the repo, send the exported file to Claude and ask it to update the baseline in the `plan-data` block of the HTML. **Reset to file** returns to that baseline.

## Out of scope for the first release

iOS build and TestFlight, hosted web preview, favourites and ratings, a deals/coupon rule engine, delivery slots and scheduled delivery, multi-organization store switching, product analytics.
