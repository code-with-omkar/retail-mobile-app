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
