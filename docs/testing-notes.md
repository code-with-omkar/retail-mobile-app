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
