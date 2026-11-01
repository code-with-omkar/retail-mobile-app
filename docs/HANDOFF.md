# Handoff: where the project stands (2026-10-26)

Read this first in a new session. Branch `mobile-app`, repo root `C:\Users\Omkar\Personal\retail-app\source`.

## Built, tested and applied (dev database is current at 33 migrations)
P7 online payment, P10 order numbers, P11 notifications, P13 store offers, P14 offer fees, P15 paid placement, P12 delivery partners (+ `delivery_app`), P16 catalogue editing/pictures/stock, P8 phone code + Google sign-in, account deletion, release readiness, P17 reports, P18 order again + saved items, P19 ratings.
- P20: offers with discounts, pictures, app-open pop-up (+ admin-set fee), offer notification opens the offer, Home banners (7 days), Your stores, Bought before.
- P21: order note and out-of-stock choice (Remove, CallMe, Replace).
- P22 A: store-own prices set by managers, original price struck through, pink corner ribbon on discounted cards.
- P22 B: admin-set commission % per store (default + overrides), fixed on each order, "Earnings by shop" in Reports (delivered orders; store keeps fees).
- Notification rule: bell only for placed, delivered, declined, cancelled and payments; accepted/packing/ready/out for delivery only update the timeline.

## Git
All committed on `mobile-app` (latest `65dd7b1`). Unpushed: `d3c1c5a` (CI fix: `LocalImageStore.cs` had been gitignored), `4b173d7`, `b625918`, `65dd7b1`. The user pushes; then read the CI result.

- P23: delivery tracking. Partner app shares its position every 20 s while out for delivery; customer sees distance and ETA only (no map). Latest position only, erased on delivery. Applied, live-checked, committed. Cases: section Q. Tests: backend 972, customer app 528, partner app 20.

- P24: the store removes or reduces items of an Accepted or Preparing order (portal: Change items). Fees stay, commission recomputed, stock NOT returned; paid online gets an automatic partial refund via the payment maintenance job (live refund needs Razorpay methods enabled); the customer sees the change on the order. Applied, live-checked, committed. Cases: section R. Backend 991, customer app 531.

## P25 replacement products (built, tested, migration not applied)
Store offers a replacement for an out-of-stock line (portal: Change items, Replace); the customer accepts or declines in the app within 5 minutes; no answer removes the item and refunds. Dearer replacements allowed for cash orders only. Migration `AddReplacementProposals` generated, **not applied**. Cases: section S. Backend 1003, customer app 535. NEXT (user asked): Google Maps; the user has created a Maps key (never print it; ask where to put it).

## P27 portal tests (built, passing, not committed)
Admin portal now has a test runner: Vitest + jsdom through Angular's unit-test builder (`npm test` in admin-portal; dev packages vitest, jsdom). 68 tests: storage, config, auth service, guards, token-refresh interceptor, API service, and the Commission, Stock and Orders screens. `src/test-setup.js` gives tests an in-memory localStorage (Node 25 breaks the built-in one). CI portal job runs `npm test` before the build. Screens are tested by calling their methods with a stand-in ApiService. Not covered yet: the other screens (stores, products, campaigns, reports, users, roles, approvals, notifications, dashboard, login).

## P26 Google Maps address picker (committed 6e19949; map PAUSED)
Full-screen map with a fixed centre pin in the address editor (**Choose on map**), hidden when the build has no key. The key is read at build time from `mobile/android/local.properties` (`MAPS_API_KEY=`, git-ignored; GitHub secret in the release workflow); the app only asks Android whether a key exists, never reads it. No server change, no migration. On the phone the button shows but the map is blank: Google returns INVALID_ARGUMENT / authorization failure for package com.quickcart.quickcart_customer. Cause is the Google Cloud side (Maps SDK for Android not enabled in the key project, billing not linked, or key/restrictions wrong), not the app. PAUSED by the user; resume by fixing the key, then cases T1-T8. Cases: section T. Customer app 543 tests. Not done: store service-area circles, live delivery map, store/portal maps (the user chose only the address picker for now).

## Ideas not started
Push notifications (needs Firebase project), crash reporting (needs an account), tests for the remaining portal screens, live delivery map, store/portal maps.

## Waiting on the user
- Try the manual lists in `docs/test-cases.md` (sections A to K) and `docs/testing-notes.md`; send failed IDs.
- Google sign-in needs a Google Cloud web client id (testing notes A8). MSG91 account for real SMS. Razorpay methods enabled for the live payment test.
- Release steps are in `docs/RELEASE_AND_SECURITY.md`. Rotate the leaked SQL password (testing notes C).


## How we work (the user's rules)
- Short replies. Decisions one at a time with AskUserQuestion; recommended option first.
- Backend first with tests on a **throwaway** SQL database (`qc_pXX_test_<timestamp>`, created and dropped by Claude). **Stop before touching the dev database `retail-mobile-app`**: ask, back up (`.bak`, verified), `dotnet ef database update`, restart APIs, live-check with a throwaway customer, clean up.
- Commit only when asked; the user pushes. Never print secrets (they live in user-secrets).

## Run recipe (Windows, Git Bash)
- Scratch dir for builds: `ArtifactsPath=C:/Users/usern/AppData/Local/Temp/claude/C--Users-Omkar-Personal-retail-app-source-mobile/2c1b9f76-233d-4c85-8e20-6b03d7041646/scratchpad/art` (run) and `.../art_test` (tests).
- Env: `ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__QuickCommerceDb="Server=NCIT-08;Database=retail-mobile-app;Integrated Security=true;TrustServerCertificate=true"`.
- Phone API 5067: `ASPNETCORE_URLS=http://localhost:5067 dotnet run --no-launch-profile -p:ArtifactsPath=$ART`. Portal API 5000: `dotnet $ART/bin/QuickCommerce.Api/debug/QuickCommerce.Api.dll`. Portal: `npm run dev` in `admin-portal` (4200). Servers die when the shell that started them ends: start them with `run_in_background`.
- Backend tests with SQL: set `QUICKCOMMERCE_TEST_CONNECTION_STRING="Server=NCIT-08;Database=<throwaway>;Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true"`, then `dotnet test QuickCommerce.Tests -p:ArtifactsPath=.../art_test`. Last run: 852 passed.
- `sqlcmd` needs `-E -C` (and `-I` when a script touches filtered indexes).
- Phone: `adb` is at `C:/Users/usern/AppData/Local/Android/Sdk/platform-tools/adb.exe`; `adb reverse tcp:5067 tcp:5067`; `flutter run -d RZ8T10RAY8Z --dart-define=API_BASE_URL=http://localhost:5067` (customer app in `mobile`, partner app in `delivery_app`).
- Heredocs in the bash tool break on long scripts: write files with the Write tool and run node scripts from the scratchpad.
- Development sign-in codes are in the phone API log: line "DEVELOPMENT sign-in code for +91...".
- Customer app tests: 510 pass (`flutter test`), analyzer clean. Portal: `npx ng build --configuration development`.
