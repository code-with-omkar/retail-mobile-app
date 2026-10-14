# Manual testing notes

Updated 2026-10-09. Tick items off as you test. Automated tests already pass (backend 432, mobile 416).

## Setup (once)
- [ ] Two APIs running: `http://localhost:5067` (phone) and `http://localhost:5000` (portal). Ask Claude to restart them if needed.
- [ ] Portal: http://localhost:4200, sign in as `storemanager@example.test`. If you changed the user, sign out and in again.
- [ ] Phone: USB connected, app launched with `--dart-define=API_BASE_URL=http://localhost:5067`, `adb reverse tcp:5067 tcp:5067`.
- [ ] Phone signed in as your customer account. Shop from Kharghar (the portal user operates Kharghar).

## A. Order status (portal to phone), task 6.7
Place a cash order on the phone, then in the portal Orders page:
- [ ] Accept: phone shows "Packed" and loses Cancel, within about 15 s; bell gets a notification.
- [ ] Start packing, Mark ready, Complete: phone follows (Packed, Out for delivery, Delivered), notification each time.
- [ ] Reject a second order: phone shows "Declined"; stock returns (check the product is orderable again).
- [ ] Cancel a third order in the app before the shop acts: portal shows no buttons for it.
- [ ] Portal list shows only Kharghar orders (no Panvel / Harbor orders).
- [ ] Decide: should the phone say "Packing" instead of "Packed"? (open question)

## A2. Order numbers and notifications (P10 and P11 applied; offers confirmed working by you 2026-10-10)
- [ ] Place a cash order from Kharghar: number looks like `KHG-YYMMDD-0001` (next order 0002).
- [ ] Bell shows "Order placed"; each portal step (Accept, Start packing, Mark ready, Complete) adds a message that names the order number.
- [x] Switch the app to Marathi: the same notifications read in Marathi.
- [x] Portal, Offers page (admin login): create an offer with Marathi text, send now; it appears in the app within a minute with an Offer label.
- [x] Switch Offers off in the app (Notifications screen); a new offer does not arrive, order messages still do.
- [x] Schedule an offer for a few minutes ahead, then cancel it before it is sent: it never arrives.

## A3. Store-owned offers (P13 applied; confirmed working by you 2026-10-12)
- [x] Admin: create an offer for one chosen store; only customers who ordered from or shopped at that store get it, with "From <store>" on the card.
- [x] Admin: create an offer with no store ticked; every customer with offers on gets it.
- [x] Store staff login (e.g. storemanager): Offers page has no store picker; an offer created goes to the store's own customers.
- [x] Store staff cannot see or cancel another store's offers or an all-stores offer.

## A4. Offer fee (P14 applied; fee settings confirmed working by you 2026-10-12; card payment still to verify)
- [x] Admin, Offers page: set a default fee (e.g. 10) and a different fee for one store; the page shows them.
- [ ] Store staff: Offers page shows the fee; "Create and pay" opens Razorpay; after a test payment the offer shows Scheduled and is sent at its start time.
- [ ] Close Razorpay without paying: the offer shows Awaiting payment and is not sent; "Pay" retries without a second charge.
- [ ] Cancel a paid offer before it is sent: fee refunded (check the Razorpay dashboard).
- [ ] Admin-created offers need no payment. A store with fee 0 pays nothing.

## A5. Paid placement (P15; after the migration is applied)
- [ ] Portal, Placement: enter your store's pincode, see 4 weeks; place a bid, pay with Razorpay; the bid shows Leading.
- [ ] A second store bids higher and pays: the first bid shows Outbid (refunded), the second Leading.
- [ ] Phone: with a delivery address in that pincode the leading store is first, tagged Sponsored.
- [ ] Cancel an unpaid bid; cancel a paid one (refunded). Admin: change the minimum bid and the rules apply.

## A6. Delivery partner (P12; after the migration is applied)
Setup done: demo partner `deliverypartner` has the Kharghar store; its password is in user-secrets (`dotnet user-secrets list` in src/QuickCommerce.Api, key Authentication:DevelopmentDeliveryPartnerPassword).
- [ ] Portal, Orders: a Ready order shows a "Delivery partner" picker; Give. The row shows "by <name>"; "Complete" is gone.
- [ ] Partner app (`delivery_app`, `flutter run --dart-define=API_BASE_URL=http://localhost:5067`): sign in; the order is listed with cash to collect.
- [ ] Partner taps "I picked it up": customer phone shows "Out for delivery", the partner's first name, Call, and a 4-digit code.
- [ ] Partner enters a wrong code: refused. Right code and exact cash: Delivered; the customer gets "delivered".
- [ ] Five wrong codes lock it for 15 minutes.

## A7. Catalogue, pictures and stock (P16; no database change)
- [ ] Portal as admin, Catalog: New product (SKU, name, category, unit, price); it appears in the list.
- [ ] Edit it: change the name/description; add a Marathi name; add a second size with its own price; make it the default.
- [ ] Upload a JPEG/PNG photo; the thumbnail shows. Try a text file renamed .jpg: refused.
- [ ] Portal as store manager, Stock: tick "Sells here" for the new size and set a quantity.
- [ ] Phone: the new product shows with its photo and Marathi name (Profile language), and the new price.
- [ ] Store manager cannot edit products (no Edit button).

## A8. Phone OTP and Google sign-in (P8; after the migration is applied)
OTP needs nothing from you: in development the code is printed in the phone API's log (the line starting "DEVELOPMENT sign-in code").
- [ ] Phone, Profile, Sign in: "Continue with phone", enter your number, Get OTP. Read the code from the API log, type it: you are signed in and the profile shows your number.
- [ ] A wrong code is refused; five wrong codes make the right one fail too (ask for a new code after the countdown).
- [ ] Sign out and sign in again with the same number: the same account (orders kept).
- [ ] With an email/password account: Profile, "Verify phone number", enter your number, code from the log. Then sign out and sign in with the number: you land in that same account.
- [ ] Google (needs setup, below): "Continue with Google" appears and signs in.

**Google setup (once).** Google Cloud console: create a project, OAuth consent screen, then Credentials: (1) a **Web application** client: its id is the server client id; (2) an **Android** client with package `com.quickcart.quickcart_customer` and your debug SHA-1 (`keytool -list -v -keystore %USERPROFILE%\.android\debug.keystore -alias androiddebugkey -storepass android`). Then give the API the web client id (`dotnet user-secrets set "Google:ClientIds:0" "<web client id>"` in src/QuickCommerce.Api) and run the app with `--dart-define=GOOGLE_SERVER_CLIENT_ID=<web client id>`.

## A9. Account deletion (no database change)
Use a throwaway account, not yours.
- [ ] Phone: register a new account, add an address, then Profile, "Delete account". Wrong password is refused; the word DELETE is required.
- [ ] With an order still Pending or being delivered: deletion is refused with a clear message.
- [ ] After deleting: you are signed out; signing in with the old email fails; the old orders still show in the portal, with no customer name or address.

## B. Online payment (blocked until Razorpay methods are enabled), task 7.10
- [ ] Razorpay dashboard (Test mode): enable payment methods / finish activation.
- [ ] Checkout shows "Pay online"; pay with test UPI `success@razorpay` or test card; order ends "Paid".
- [ ] Close Razorpay without paying: "not completed", Try again works, countdown visible.
- [ ] Wait out the 15-minute hold: order cancels by itself.
- [ ] Cancel a paid order (while Pending): shows refund on its way, then refunded (check Razorpay dashboard).
- [ ] Cash on delivery still works as before.

## C. Housekeeping for you
- [ ] Rotate the leaked SQL `sa` password (task 0.14); repo is public.
- [ ] `mobile/test/core/theme/app_theme_test.dart` (from the design-system merge) fails: fix or remove.
- [ ] Remove the two test store assignments for `storemanager` when done (tagged `dev-portal-check`).
- [ ] Decide commit/push: everything since `9d90fb6` is uncommitted; push from your own terminal.

## Dev database backups taken this session (SQL Server backup folder)
`pre-P7-payments`, `pre-admin-credential-reset`, `pre-storemanager-credential-reset`, `pre-storemanager-stores`, `pre-storemanager-primary-store`.
