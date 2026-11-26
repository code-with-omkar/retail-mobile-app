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

## P40 Portal sign-in and user saving fixes (built, tested; NO migration)
- Saving a user in User Master always failed with 400: the portal sends role and staff category by name and the API only read numbers. The user endpoints now read and return names (numbers still accepted). A non-staff user keeps only the primary store.
- Portal sign-in: delivery partner and customer accounts are refused on the login page with a message naming their app (nothing stored, server session closed; a saved one is dropped). Store staff land on /dashboard (they were sent to the admin-only page and saw Access Denied).
- The header greeting follows the time of day.
- Login page shows its own error message (toasts only exist inside the signed-in shell); the printed dev credentials are removed; store staff land on Today and no longer see Overview; a refused sign-in closes its session with the token.
- **Store managers work across all assigned stores** (orders list and detail, status steps, change items, replacements, giving to a partner of the order's store). Store employees stay on the primary store. Reports, stock, offers, Today, placement, ratings and behaviour still use the primary store. Portal Orders has a store filter. Tests: server 1265 (17 HTTP tests on qc_http_test), portal 256.
- Portal Orders keeps each row in place after an action (new orders on top, changed row marked); a reload sorts by status again. Portal 259 tests. The order flow was tried on the phone and portal and reported working (2026-11-26).
- Open bug: changing a user's role in User Master does not add the new role link, leaving the account with no role.

## P39 Design pass 2 and sub-categories (built, tested, not committed; NO migration; phone rebuilt with all but the chips)
- **Look**: full dark theme in the v4 style (near-black page, no gradient, soft dark tiles, white text, yellow actions; the old purple literals are gone); bottom nav and cart pill smaller (60 px capsule, 48 px tap targets kept for accessibility); a 24 px rounded page-coloured sheet overlaps the bottom of the festival hero.
- **Icons**: 184 Material icons replaced by Lucide rounded-stroke icons (package `lucide_icons_flutter`, bundled, works offline). Stars, hearts, radio buttons and the Apple mark stay Material (Lucide has no filled forms). Tests use the same icons.
- **Sub-categories**: server `GET/POST/PUT api/admin/catalog/categories` (administrator; ICategoryAdminService, CategoryRules: two levels, no loops, no repeated name among siblings, name 1-60), EfCategoryAdminStore; a main category's product list also includes its sub-categories' products (Ef and in-memory, paged catalogue only); catalogue caches of categories cleared on change (page caches expire by their own TTL). Portal **Categories** screen (add, edit, move under another, switch off; tree with product counts). App: Categories tab rail lists main categories, chips "All" + sub-categories above the products when a category has any; Home category tiles show main categories only; Category.parentId; Marathi "सर्व".
- Tests: server 1261, app 662, portal 251 (SQL test on qc_http_test passed). Cases: section AD.
- **Mistake to know about**: `dart format lib` was run once by accident and re-wrapped lines. Formatting-only files were restored from git; 25 files with real uncommitted work (and the new ad files) stay re-wrapped (same behaviour, noisy diff, 27 style-only analyzer infos such as curly braces). The project does not use dart format.

## P38 Ads, colourful order cards, more offer designs (built, tested, not committed; TWO MIGRATIONS NOT APPLIED to the dev DB: WidenOfferCardDesign and AddAds)
- **Ads** (admin-set, shown in the customer app). Server: Ad entity (place, picture, title, tap target None/Product/Category/Offer/Url, one store or all, dates, on/off, order, view and tap counters), AdRules (place known, picture must be our own upload path, https only for links, dates, title length), AdService (admin only to manage), EfAdStore (live query by place and store; counters are single-statement updates), AdsController: admin GET/POST/PUT/DELETE api/admin/ads; open to guests GET api/ads?placement=&storeId=, POST api/ads/{id}/view and /tap (rate limited like analytics). Places: home, home_middle, categories, search, cart, order, product. The picture is uploaded with the existing offer picture endpoint. Migration AddAds (table Ads, check constraints on place, target and counts).
- Portal: **Ads** screen (admin): form with place, title, store, order, dates, picture upload with preview, tap target (category and offer pickers, product search, web link), list with views, taps and tap rate, status, edit, switch on/off, delete.
- App: AdBanner(place) (3:1 picture, small Ad mark, title over it, several ads swipe with dots, one view per ad per app run, tap counted then opens product, category, offer or an https page via UrlOpener); placed on Home (top and between rows), Categories, search results, cart, order placed and order screen, product page. No ad or a failure draws nothing.
- **Order cards**: coloured band and tinted header by state, shop name first, order number and date, items and bold total, a rating row: the shop's average (count) and what the customer gave (shop and delivery stars) or a yellow Rate this order button on a delivered order. Server: the order list now carries storeRating, storeRatingCount, myStoreStars, myDeliveryStars (IOrderRatingLookup, two queries for the whole list). No migration.
- **Offer designs**: six everyday designs the admin can pick besides the eight festivals: fresh, weekend, bigsaver, combo, freeship, newin (own colours, artwork, bilingual names, SPECIAL OFFER chip; no Home greeting or product tags). Migration WidenOfferCardDesign widens the database check.
- **Product page**: a dark cart bar above Add (items and total, View cart) and a cart button with a count at the top; Added to cart offers View cart.
- Also: Profile row values align right; the Diwali window runs to 30 Nov so the look and animation show (set back to 12 Nov when no longer wanted); transparent status and navigation bars (the page colour runs behind them).
- Tests: server 1254 (SQL ones on a throwaway DB too), portal 243, app 654+ (see dashboard). To go live: ask first, back up, dotnet ef database update (WidenOfferCardDesign, AddAds), restart APIs, rebuild the phone, walk section AC of test-cases.md.
- Not done: ad scheduling by time of day, ads for a customer group, a paid-ads price list.

## P37 Clean v4 restyle of the customer app (built, tested, not committed; app only)
- From the design file https://claude.ai/artifact/DoZbGUX2mXZGGejNvKgLyN (page Clean v4 FINAL: Home, Home scrolled, Shop categories, Orders, Live tracking, Profile). The other screens (sign-in, OTP, search, detail, cart, checkout) have no v4 drawing, so they got the v4 tokens only.
- Theme (core/theme.dart): light is a white page (#FFFFFF), tiles #F5F5F7, lines #ECECF0, ink #141118, yellow #FFD400 buttons, success green #14A44D, error red #C42F1D (Pal.green and Pal.pink now hold these). Dark theme keeps its dark ground. Text on the page uses pal.onGround / mutedOnGround instead of fixed white. Fonts bundled in assets/fonts (OFL): Bricolage (headings), PlusJakartaSans (text), Mukta (Marathi). QC.display() is the heading style.
- Home: festival hero while a festival runs; section order offers, stores, Popular (rank badges), Forgot something, categories, deals tiles; slim offer strips for offers without a picture. Product tiles: picture tile with soft tint, green discount pill, yellow add button on the corner, name and size on separate lines; grids three across. Categories tab: left rail plus grid. Orders: white cards and status pills (StatusPill). Live order: arrival heading and four-step bar. Profile: header with Edit, quick cards, grouped lists. Nav capsule and cart pill sized as in the design.
- Second pass (checked on the phone with a signed-in test customer, screenshots): sign-in and login, cart, checkout, order placed, order screen, Orders, Profile, notifications and search results all follow the v4 look. Fixes from that pass: text buttons in the v4 font, shop bar as a white card with a tinted icon and a yellow Change pill, stepper minus as an ink circle, free-delivery banner as a light card, checkout Change link in ink, arriving line as a light green card, darker green for FREE and savings text, search results three across, no pink left except error red. A throwaway test customer and two test orders were made in the dev database for this check (to be deleted).
- Not done: the map half of live tracking (Google Maps key paused), sub-category chips (no data), the design's own stroke icons (Material icons used), the star rating on Orders (no rating in the order data), overlap of the hero by the white sheet.
- Tests: app 634. Walk test-cases AB. Look at sign-in, search and checkout on the phone: they were not checked on screen.

## P36 Crash reporting and order polish (built, tested, not committed; no migration)
- Crash reporting with Sentry, off until a key is set, and scrubbed so no person, request body, headers, cookies, query string or tap trail is sent. API: Sentry:Dsn (Hosting/CrashReporting.cs). Portal: window.__SENTRY_DSN__ (core/crash-reporting.ts). Both apps: --dart-define=SENTRY_DSN=... (core/crash_reporting.dart, delivery_app/lib/crash_reporting.dart). Not verified: an Android release/debug build with the sentry_flutter native parts (build the apps once before relying on it), and nothing was sent to a real Sentry project (no account yet).
- Customer app: a cancelled or declined order says what happened to the money (not charged / refund of the amount on its way, 5 to 7 working days / refunded / could not refund), in Marathi too (endedMoneyLine).
- Partner app: Delivered checks a 4-digit code and a cash amount on the phone before asking the server, and a red note shows when the cash typed is less or more than due.
- Tests: backend 1235, portal 232, partner app 27, customer app see dashboard. Walk test-cases AA.
- Not done from the same request: push notifications (needs a Firebase project and google-services.json first; the server side, device registry and sender can be built before that) and the Clean v4 restyle (large; not started). Order tracking screen: already has the timeline, ETA, partner distance and the new money line; I found no further gap without a list from you.

## P35 Multi-organization switching (built, tested, not committed; no migration)
- A platform administrator (role ApplicationAdmin only, not the ordinary Admin) can look at another organization: the portal sends header X-Organization-Id on every request; the server (AuthorizationScopeService, CurrentUserContextResolver) then answers for that organization. Anyone else sending the header, or an unknown or closed organization, gets 403.
- Read-only there: ReadOnlyInOtherOrganizationMiddleware refuses any change (not /api/auth) with 403 and logs every request made in another organization (user, home organization, other organization, method, path).
- GET api/admin/organizations (platform admin only) lists active organizations with the home and current one. Portal: OrgSwitcher at the top of the shell (only for ApplicationAdmin and only when there is more than one organization), yellow read-only banner, choice kept in browser storage and forgotten at sign-out.
- Tests: backend 1230 (plus an HTTP test with SQL that creates and removes two organizations), portal 230. Dev DB has one organization only, so walk test-cases Z after adding a second (ask me).
- Not done: a person who belongs to several organizations (staff or admin with more than one membership) and changing data in another organization.

## P34 Festival follow-ups (built, tested, not committed; no new migration)
- Admin can change the card design and picture of an existing offer: PUT api/admin/campaigns/{id}/card ({cardDesign, imageUrl}); administrator only (403), unknown design/foreign picture 400, cancelled 409. Portal: "Change card" on each offer row (design picker with preview, keep or drop the picture).
- App: products of a running festival-design offer carry a small tag on their picture (DIWALI DEAL, BAPPA SPECIAL, ...) and Home shows a Marathi greeting chip (शुभ दीपावली, ...) under the date-based festival banner. Both come from the active offers; nothing new on the server.
- Tests: backend 1229, app 624, portal 224. Walk test-cases Y13-Y17.
- Festive finish: while a festival-design offer runs, Home gets a soft wash of the festival colour at the top (FestivalGlow) and the four category tiles take the festival colour (test-cases Y18-Y19). App only.
- Still not done from the design file: whole-app Clean v4 restyle.

## P33 Offer card designs and festival card upload (built, tested, not committed; MIGRATION AddOfferCardDesign NOT applied to the dev DB)
- From the design file https://claude.ai/artifact/DoZbGUX2mXZGGejNvKgLyN (Festival offer cards): an offer can have one of eight festival card designs (diwali, ganesh, navratri, holi, sankranti, padwa, eid, christmas). The card draws the festival colours, motif, a countdown chip (from the discount end), English and Marathi festival names, the offer title as the line, and a button. No design is the plain banner as before.
- Admin only: the portal offer form has a Card design picker (Plain plus the eight) with a live preview; store staff do not see it and the API refuses a design from them (403). The existing picture upload is the festival card upload: with a design chosen, an uploaded picture replaces the built-in artwork (chip and button stay on top); best size 700 x 352 (2:1).
- Backend: Campaign.CardDesign (max 20, check constraint on the eight keys), OfferCardDesigns (keys, normalize: lower case, plain/empty = none), request/response/ActiveOffer/OfferDetail carry it (migration AddOfferCardDesign). Portal: shared app-offer-card component. App: FestivalCard widget (eight painted motifs), used in the Home offers rail (176 high) and the offer screen; Marathi strings added. Layout shrinks text instead of overflowing.
- Tests: backend 1227, app 621, portal 222. To go live: ask first, back up, dotnet ef database update (AddOfferCardDesign), restart APIs, rebuild the phone app, walk section Y of test-cases.md.
- Not in this pass (the design file also has them): festive product tag, festive category tile tint, Marathi greeting chip and the full festival takeover of Home; changing a design on an existing offer (offers cannot be edited today, only cancelled); restyling the whole app to the Clean v4 final design.

## P32 Customer behaviour analytics (built, tested, not committed; MIGRATION AddAnalytics NOT applied to the dev DB)
- Anonymous only: events carry a random session id made at each app start, never an account, phone, email or address (the server drops any detail that has 7 or more digits or an @). Steps: app_open, search, product_view, add_to_cart, cart_open, checkout_start, coupon_applied, coupon_refused (by kind), slot_chosen, order_placed (cash/online plus coupon/slot), store_chosen, out_of_service_area.
- Backend: AnalyticsEvents (raw, 90 days) and AnalyticsDaily (totals kept for good; the row with no store is the total of all stores). POST api/analytics/events (anonymous, rate limited 60/min per IP, batch of 1-25, unknown names refused). GET api/reports/behaviour (staff own store, admin one or all; at most 92 days; days older than 90 come from the daily totals). AnalyticsMaintenanceService once a day: rolls up then purges. Coupon preview now also returns a Reason kind.
- App: core/analytics.dart (batches of 20, or 15 s, or when the app goes to the background; best effort, never throws; session id in memory only), wired into home search, product, cart, checkout, coupon, delivery time, store choice. Portal: Behaviour screen (funnel bars, top searches, coupon refusals, sessions per day).
- Tests: backend 1205 (36 rules, 15 service, 7 SQL, 2 HTTP among them), app 599, portal 206. To go live: ask first, back up, dotnet ef database update (AddAnalytics), restart APIs, rebuild the phone app, walk section X of test-cases.md.
- Limits to know: anyone can post fake events (rate limited), so treat funnels as indicative; top searches and refusal reasons are only kept for 90 days.

## P31 Today screen and store alerts (built, tested, not committed; NO migration)
- GET api/store-today[?storeId] (StockManagement policy: store staff get their own store, an administrator must name one). TodayRules (pure) builds counts (open statuses whatever the day; placed/delivered/cancelled/revenue for the India day) and alerts: new order waiting 5 min (urgent 10), ready not collected 20 (urgent 40), out of stock, low stock (at or below reorder), delivery windows at 80 percent or full. Limits are fixed in code and quoted on the screen. Upcoming: open scheduled orders, soonest window first.
- Portal: Today screen (auto refresh 30 s). Tests: 23 rules + 5 service + 4 SQL + 1 HTTP matrix; portal 196. Restart the APIs to expose the route (no database step); docs/test-cases.md section W.

## E2E test (built, passing locally, new CI job not yet run on GitHub, not committed)
- mobile/test/integration/e2e_coupon_slot_test.dart: the customer app's real ApiClient/repositories against the real API + SQL: the admin (login username admin) gives one store hourly windows with capacity 1 and makes a flat coupon; a customer previews the code, lists windows, orders with both, sees the window full and the code used, is refused a second order for the full window (SlotUnavailable), cancels, and gets the window and code back; everything set up is removed again. Opt-in: --dart-define=SMOKE_API_URL=... SMOKE_ACCOUNTS=true E2E_ADMIN_PASSWORD=<dev admin password>. Run it on a throwaway DB. Login/registration are rate limited per IP, so many runs in a few minutes give 429.
- CI: new e2e job in .github/workflows/ci.yml (SQL container, dotnet ef database update, API in Development with a CI-only admin password, then only this test file; api_smoke_test.dart is NOT run in CI because it expects data only the dev DB has). Check the first run on GitHub.
- Not covered yet: on-device integration_test (9.1 stays open).

## P30 HTTP-level API tests (built, passing, not committed)
- ApiHarness (WebApplicationFactory<Program>, package Microsoft.AspNetCore.Mvc.Testing 10.0.14, `public partial class Program`) runs the real API in memory on the opt-in test SQL DB with the seeded demo users (admin, store staff, customer, delivery partner); tokens come from the app's own ISessionIssuer.
- ApiHttpTests (7): who may call the coupon, delivery-time and commission endpoints (401/403/200/400/404/409), a coupon round trip, delivery times set by an admin showing for a customer. ApiRouteCoverageTests (2, no DB needed): every route must ask for a sign-in unless its prefix is in the deliberate OpenPrefixes list, so a new unprotected endpoint fails the build.
- Found and removed: the unused Visual Studio template GET /WeatherForecast (was open to anyone). 1112 backend tests pass. Throwaway DB qc_http_test on NCIT-08 (drop when done).

## P29 Delivery slots (built; migration APPLIED to the dev DB 2026-11-02; not committed)
- Customers choose "as soon as possible" or an hourly-style window at checkout. Admin default settings plus per-store overrides (hours, window length 30-180 min, orders per window, cut-off, days ahead 1-7, ASAP on/off). India time; windows stored as UTC on the order.
- Backend: DeliverySlotSettings, SlotBookings, Order.DeliverySlotStart/End (migration AddDeliverySlots); SlotRules (pure, shared by the list and checkout); the window is re-checked inside the order transaction and a place is taken with one atomic MERGE; place given back on customer cancel, staff reject and unpaid-hold expiry. API: GET/PUT/DELETE api/admin/delivery-slots[/default|/stores/{id}], GET api/stores/{id}/delivery-slots; checkout takes deliverySlotStart. Portal order list sorts ASAP first then by window. 1103 backend tests pass on a throwaway DB (qc_slot_test on NCIT-08, drop when done).
- Portal: Delivery times screen + window on each order (188 tests). Customer app: Delivery time section at checkout, window on success/order screens, Marathi labels (580 tests). Partner app: window on card and detail (22 tests).
- Applied: backup retail-mobile-app_pre_slots_20261102.bak (verified), AddDeliverySlots applied, both APIs rebuilt and restarted. Still to do: phone rebuild, then you walk section V of test-cases.md (scheduling is off until an administrator switches it on in Delivery times). Scheduled orders are visible to the store at once; an ETA countdown is not shown for them.

## P28 Coupons (built; migration APPLIED to the dev DB 2026-11-01; not committed)
- Admin-only coupon codes (Percent with cap, Flat, Free delivery; minimum order, date window, total and per-customer limits, first order only). The business pays: commission and store earnings are on the price before the coupon.
- Backend: Coupon and CouponRedemption tables, Order.CouponCode/CouponDiscount (migration AddCoupons); CouponRules (pure, shared); checkout applies it inside the order transaction with an atomic use counter; use is released on customer cancel, staff reject and unpaid-hold expiry; a store reducing an order recomputes the discount (percent follows, never grows) and the refund is the drop in what was paid. API: GET/POST/PUT api/admin/coupons, POST api/carts/{storeId}/coupon (preview). Reports: coupon cost card and CSV column. 1072 backend tests pass on a throwaway DB.
- Portal: Coupons screen (administrators). 179 portal tests.
- Customer app: coupon box in the cart, discount in cart/checkout/order bill, code sent with the order. 566 tests. Server messages are English only.
- Applied: backup retail-mobile-app_pre_coupons_20261101.bak (in the SQL Server Backup folder, verified), AddCoupons applied, both APIs rebuilt and restarted (5067 and 5000, health ready, coupon routes answer 401 without sign-in). Still to do: you walk section U of test-cases.md (no coupon exists yet; create one in the portal first). Phone app needs a rebuild for the coupon box, picture cache and accessibility fixes. Throwaway DBs on NCIT-08: qc_coupon_test, qc_throwaway_p27 (drop when done).

## 9.2 / 9.4 / 9.7 hardening (built, passing, not committed)
- 9.7: start-up warning when Media:RootPath is unset; "Several servers" checklist in RELEASE_AND_SECURITY.md.
- 9.4: product and offer pictures use cached_network_image (disk cache, decoded at display size). New package: the phone app needs a rebuild. flutter pub add also printed a Windows symlink message (Developer Mode); if the Android build complains, turn it on.
- 9.2: test/accessibility_test.dart (tap targets, labels, palette contrast). Fixed: See all / Voice search / EN-मराठी toggle now 48 dp; muted text colour lightened and the light gradient end darkened slightly for contrast.

## P27 portal tests (built, passing, not committed)
Admin portal now has a test runner: Vitest + jsdom through Angular's unit-test builder (`npm test` in admin-portal; dev packages vitest, jsdom). 170 tests: storage, config, auth service, guards, token-refresh interceptor, API service, and every screen except notifications, not-found and unauthorized (static pages). Writing them found and fixed five bugs: Stores page looped over the signal instead of its value; Products save messages were wiped at once; Placement bid message/error were wiped by the market reload; User master stayed on Loading after a failed load, save or status change. `src/test-setup.js` gives tests an in-memory localStorage (Node 25 breaks the built-in one). CI portal job runs `npm test` before the build. Screens are tested by calling their methods with a stand-in ApiService.

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
