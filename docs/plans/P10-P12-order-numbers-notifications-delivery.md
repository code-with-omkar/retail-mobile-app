# Three requests: order numbers, notification system, delivery partner app

Status: **decisions accepted 2026-10-09. P10 backend built and tested (442 tests); migration applied to the dev database; P11 built, tested and confirmed working by you (backend 477 tests, mobile 425, portal Offers page); P11 migration applied to the dev database; P12 not started.** Phases: P10 order numbers, P11 notifications, P12 delivery app.

## Today
- Order number: `ORD-yyyyMMddHHmmss-<guid>` cut to 32 characters. Hard to read out or remember.
- Notifications: one table, free text `Type`, `Title`, `Message`, English only, written inside the order code. No templates, no language, no preferences, no push, nothing for offers.
- Delivery partner: a role exists (`deliverypartner@example.test`) but an order cannot be assigned to anyone and there is no app or API for it.

## 1. Order numbers by store (small)
- Proposal: `<STORE>-<yyMMdd>-<nnnn>`, for example `KHG-261009-0042`. A short store code, the day, and a per-store per-day counter that restarts each day.
- Needs: `Stores.Code` (short, unique, additive), a counter table (one row per store and day) incremented inside the checkout transaction so two orders never get the same number, a unique index on the number (already exists).
- Old orders keep their numbers. The app, portal and notifications just show the new string (length up to 32 still fits).
- Decisions: format, and who sets store codes (suggest: derived from the name for the dev stores, editable by admins later).

## 2. Notification system
- One catalogue of notification types: OrderPlaced, OrderAccepted, OrderPacking, OutForDelivery, Delivered, Rejected, Cancelled, PaymentReceived, RefundProcessed, Offer, Announcement. Each has a code, a category (Order, Payment, Offer, System), and a template per language (English, Marathi) with placeholders ({orderNumber}, {store}, {eta}).
- Order events create notifications from the template, so wording is changed in one place and Marathi comes for free. Today's rows stay readable.
- Offers/announcements: a "campaign" (title, body, audience such as all customers or a store area, start and end time, optional link to a product or category). Created by admins in the portal; delivered to the in-app list; push later.
- Preferences: customers can switch Offers off; Order and Payment notifications are always on.
- Delivery: in-app list now (already works); push (FCM, decision D10) later reusing the same templates.
- Needs: `NotificationTypes`/templates (data or code), `Notifications` gains `TypeCode`, `Category`, `DataJson` (order id, deep link), `Campaigns` table, customer preferences. All additive.

## 3. Delivery partner app and flow
- Assign: staff assign a ready order to a delivery partner (portal button), or a partner picks from a pool. Order gets `DeliveryPartnerId`; statuses OutForDelivery and Delivered become partner actions.
- Partner interface: login, list "my deliveries", order detail (address, receiver name and phone, items, amount to collect for cash on delivery), call customer, navigate, mark picked up, mark delivered (cash collected, optional proof such as OTP), report a problem.
- Customer side: "Out for delivery" with the partner's first name and phone; notifications from section 2.
- Where it lives: either a separate small Flutter app (own login, own release) or a delivery mode inside the customer app chosen by role. Suggestion: separate app in the same repo.
- Needs: assignment fields, partner endpoints with their own policy (a partner sees only their own orders), tests. Authorization changes need approval.

## Decisions (all the recommended options)
1. Order number format: `KHG-261009-0042` (store code, date, per-store per-day counter).
2. Notifications first release: templates (EN/MR) + order and payment events + offers shown in the app + customers can switch offers off. Push later on the same templates.
3. Delivery: separate small Flutter app in the same repo; store staff assign a Ready order to a partner in the portal.
