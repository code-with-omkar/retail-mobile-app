# Release readiness and security review

Written 2026-10-14 (tasks 9.6, 9.8, 9.11, 7.8). Items marked **you** need something only the owner can do.

## 1. Building the customer app for Google Play

**One-time**
1. Create the upload key (keep the file and passwords **outside the repository**; losing it means a key reset request to Google):
   `keytool -genkey -v -keystore %USERPROFILE%\quickcart-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload`
2. Copy `mobile/android/key.properties.example` to `mobile/android/key.properties` and fill it in. It is ignored by git.
3. Play Console: create the app, enrol in Play App Signing (Google keeps the real signing key; you only hold the upload key).

**Each release**
1. Raise `version:` in `mobile/pubspec.yaml` (for example `1.0.1+2`; the number after `+` must go up every upload).
2. `cd mobile`, then:
   `flutter build appbundle --release --dart-define=APP_ENV=prod --dart-define=API_BASE_URL=https://<your api> --dart-define=GOOGLE_SERVER_CLIENT_ID=<web client id> --obfuscate --split-debug-info=build/symbols`
3. Upload `build/app/outputs/bundle/release/app-release.aab`. Keep `build/symbols` (it turns crash traces back into readable ones).

A release without `key.properties` is signed with the debug key and only good for trying on your own phone. Set `RELEASE_REQUIRE_SIGNING=true` to make a missing key an error; the GitHub release workflow does.

Done in the build: R8 shrinking and resource shrinking, Razorpay keep rules (`android/app/proguard-rules.pro`), app label "QuickCart", `allowBackup=false` (the refresh token must not travel in a device backup), release build checked.

**Still to do before Play review (you):** app icon and splash artwork, store listing text and screenshots, privacy policy page (what we hold: name, email, phone, addresses, order history, location only when "Use my current location" is tapped; how to delete: Profile, Delete account), the Data safety form (collects: personal info, location, purchase history, app activity; encrypted in transit; deletion available), content rating, and a test account for the reviewers.

## 1b. Google Maps key (customer app)

The map picker in the address editor needs a Maps SDK for Android key.

- **Local builds:** add one line to `mobile/android/local.properties` (already ignored by git): `MAPS_API_KEY=your_key`. Type it yourself; never paste it in chat, commits or screenshots. Without it the app still builds and simply hides the **Choose on map** button.
- **Releases:** add a GitHub secret `MAPS_API_KEY`; the release workflow writes it into `local.properties` for the build.
- **Restrict the key in Google Cloud Console:** Application restriction **Android apps** (package `com.quickcart.quickcart_customer` plus the SHA-1 of the debug key for testing and of the Play app signing key for release), and API restriction **Maps SDK for Android** only. A key in an app can be extracted, so the restrictions are what protect it. Set a daily quota and a budget alert.
- Address lookups still use the phone's own geocoder, which needs no key.

## 2. Continuous integration (`.github/workflows`)

- `ci.yml` on every push and pull request: backend build and tests **with a real SQL Server container**, portal type-check and production build, customer app analyze, test and release build, delivery app analyze and test.
- `release-android.yml` (manual, or a `customer-v*` tag): signed app bundle. Needs these repository secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, `API_BASE_URL_PROD`, and `GOOGLE_SERVER_CLIENT_ID` for Google sign-in. Also create a GitHub environment named `production` (add yourself as required reviewer so nobody can build a release by accident).
- The workflows were written for this repository but have not run on GitHub yet; the commands in them are the ones used locally. Expect to fix a small thing on the first run.

## 3. Account deletion (9.11)

Profile, "Delete account". The customer types DELETE (and their password when the account has one). The API erases name, email, phone, Google link, addresses, carts, notifications, sign-in and sessions, and closes the account. Order and payment records stay with the name, phone, address, location and delivery code removed (the store's books and any refund need them). Refused while an order is open or a refund is in progress. No database change.

## 4. Security review (7.8)

Reviewed: secrets, configuration, logging, browser access, authentication, uploads, payments, data lifecycle.

**Fixed in this pass**
- Browser access was open to every website in every environment. Now only `Cors:AllowedOrigins` may call the API from a browser outside Development (none listed = none allowed, with a start-up warning). Entries must be plain origins. The apps do not use CORS.
- Added HSTS outside Development and response headers on every answer (`nosniff`, `no-referrer`, `X-Frame-Options: DENY`); answers about accounts, orders and admin data are `no-store`.
- Customers can delete their account; a Google sign-in cannot take over an account whose email nobody verified, or any staff account (P8).
- Text-message codes are stored only as a keyed hash; the log sender cannot be registered outside Development; the MSG91 sender never logs the number, code or key.

**Checked and fine**
- No secrets in tracked files; production start-up refuses missing JWT key, connection string and issuer; Razorpay keys and the payment webhook secret come from configuration; `appsettings.Development.json` holds development-only values used only when the environment is Development.
- Logs: request lines carry method, path, status, time and a correlation id only; payment logs carry ids and provider codes, never card or UPI details (those never reach us). Unhandled errors answer with a generic message.
- Swagger and OpenAPI are Development only. Passwords use ASP.NET Identity hashing; logins lock after 5 failures; refresh tokens rotate and are stored hashed; password reset codes are keyed hashes.
- Uploads: content-sniffed, size-limited, server-chosen names, served with `nosniff` and a locked-down content policy.
- Payments: amounts and signatures are checked on the server; webhook and confirm calls are idempotent.

**Left for you or a later pass**
- `AllowedHosts` is `*`; set the real host name(s) in production configuration.
- Per-IP rate limits live in each server's memory; with more than one API instance the effective limit is higher (per-account limits are in SQL and hold). A shared limiter (Redis) is the fix if you scale out.
- Crash reporting (9.5) is not added: it needs a Firebase or Sentry account and care that no phone numbers or tokens are sent.
- A leaked SQL password from earlier development should still be rotated (testing notes C).
- Run a dependency vulnerability check on a schedule (`dotnet list package --vulnerable`, `npm audit`, `flutter pub outdated`); GitHub Dependabot can do this once the repository is on GitHub.
