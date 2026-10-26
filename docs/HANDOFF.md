# Handoff: where the project stands (2026-10-26)

Read this first in a new session. Branch `mobile-app`, repo root `C:\Users\Omkar\Personal\retail-app\source`.

## Built, tested and applied (dev database is current at 32 migrations)
P7 online payment, P10 order numbers, P11 notifications, P13 store offers, P14 offer fees, P15 paid placement, P12 delivery partners (+ `delivery_app`), P16 catalogue editing/pictures/stock, P8 phone code + Google sign-in, account deletion, release readiness, P17 reports, P18 order again + saved items, P19 ratings.
- P20: offers with discounts, pictures, app-open pop-up (+ admin-set fee), offer notification opens the offer, Home banners (7 days), Your stores, Bought before.
- P21: order note and out-of-stock choice (Remove, CallMe, Replace).
- P22 A: store-own prices set by managers, original price struck through, pink corner ribbon on discounted cards.
- P22 B: admin-set commission % per store (default + overrides), fixed on each order, "Earnings by shop" in Reports (delivered orders; store keeps fees).
- Notification rule: bell only for placed, delivered, declined, cancelled and payments; accepted/packing/ready/out for delivery only update the timeline.

## Git
All committed on `mobile-app` (latest `b625918`). Unpushed: `d3c1c5a` (CI fix: `LocalImageStore.cs` had been gitignored), `4b173d7`, `b625918`. The user pushes; then read the CI result.

- P23: delivery tracking. Partner app shares its position every 20 s while out for delivery; customer sees distance and ETA only (no map). Latest position only, erased on delivery. Applied, live-checked, committed. Cases: section Q. Tests: backend 972, customer app 528, partner app 20.

## P24 store changes an order (built, tested, migration not applied)
Staff remove or reduce items of an Accepted or Preparing order (portal: Change items). Fees stay; commission recomputed; stock NOT returned; paid online gets an automatic partial refund via the payment maintenance job (live refund needs Razorpay methods enabled); customer sees the change on the order. Migration `AddOrderChanges` generated, **not applied**. Cases: section R. Backend 991, customer app 531.

## Ideas not started
Push notifications (needs Firebase project), Google Maps picker (needs a key), crash reporting (needs an account), portal tests, substitutions that adjust the order.

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
