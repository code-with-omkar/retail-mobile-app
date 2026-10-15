# Handoff: where the project stands (2026-10-15)

Read this first in a new session. Branch `mobile-app`, repo root `C:\Users\Omkar\Personal\retail-app\source`.

## Built (all tested; dev database is current at 28 migrations)
P7 online payment, P10 order numbers, P11 notifications, P13 store offers, P14 offer fees, P15 paid placement, P12 delivery partners (+ `delivery_app`), P16 catalogue editing/pictures/stock, P8 phone code + Google sign-in, account deletion, release readiness (signing, CI, security review), P17 reports + new-orders-first list, P18 order again + saved items, P19 order ratings.

## P20 (offers with discounts and pictures, app-open pop-up and fee, offer notification opens, your stores, bought before)
Built and tested (backend 907, customer app 520, portal builds). Migration `AddOfferDiscountsAndImages` is applied to the dev database (backup `retail-mobile-app_before_P20.bak`) and live-checked; commit when the user asks. Manual cases: `test-cases.md` section L. Notes: the highest percent wins (no stacking); staff pay the normal fee plus the admin-set app-open fee when they tick the pop-up; admin offers are free.

## Not committed yet
Everything after commit `5c2a312`: reports, order-list ordering, order again, saved items, ratings (about 52 files), plus docs. Unpushed commits: `66acbc8`, `7825365`, `6eddf9d`, `5c2a312`. The user pushes. `mobile/windows/flutter/generated_plugin*` and `admin-portal/.angular` are never committed.

## Waiting on the user
- Try the manual lists in `docs/test-cases.md` (sections A to K) and `docs/testing-notes.md`; send failed IDs.
- Google sign-in needs a Google Cloud web client id (testing notes A8). MSG91 account for real SMS. Razorpay methods enabled for the live payment test.
- Release steps are in `docs/RELEASE_AND_SECURITY.md`. Rotate the leaked SQL password (testing notes C).

## Ideas not started
Delivery tracking (partner location), substitutions and order notes, push notifications (needs Firebase), Google Maps picker (needs a key), crash reporting (needs an account), portal tests.

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
