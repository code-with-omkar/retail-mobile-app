# Store offers, offer fees and paid store placement

Status: **decisions accepted 2026-10-10 (all recommended); waiting for go-ahead to build P13.** No code or data change yet. Phases: P13 store-owned offers, P14 offer fee, P15 paid placement. Depends on P11 (offers exist, admin-only today) and P7 (online payment).

## Requests
1. Offers should be announced **by the store**, or the admin chooses **which stores** an offer is for.
2. A **configurable amount** to publish an offer (a fee, possibly zero).
3. **Store order in the list decided by bidding**: a store that pays the winning amount is shown first in that location area.

## Today
- Offers are written by an Application Admin only and go to every customer with offers on (all stores).
- Stores are listed by distance. There is no money flowing from stores, no store billing, no store payments.
- The portal has store staff users scoped to their store, and an admin.

## 1. Offers by store
- An offer belongs to one or more stores (`CampaignStores`). Store staff create offers for **their own store** only; an admin can create for **any chosen stores** (or all).
- Audience: customers who have that store as their selected store, or whose delivery place the store serves. (Simplest first release: customers who have ordered from it or currently have it selected. Needs a customer-to-store link query.)
- Customers see the store's name on the offer.
- Needs: `CampaignStores` table (additive), staff permission to create offers for their store, audience query per store, portal page for store staff. Authorization change: needs approval.

## 2. Fee to publish an offer
- A setting (global default, optional override per store); 0 means free. The fee is shown before publishing.
- Ways to collect (decision): (a) store pays through Razorpay when publishing, offer goes live after payment (reuses P7); (b) store keeps a prepaid balance that is topped up and spent per offer; (c) admin records an offline payment (invoice) and releases the offer.
- Needs: fee settings, a store payment record (offer id, amount, status), refund if the offer is cancelled before sending, receipts.

## 3. Paid placement (bidding)
- Idea: for a location area and a period, stores bid; the highest confirmed bid shows **first** in that area for that period.
- Questions that change the design: what is an "area" (pincode, a radius cell, the store's service area), how long a slot lasts (day, week, month), open or sealed bids, minimum bid and increment, when the winner pays (up front on confirm), what happens to a bidder who is outbid (refund), whether customers are told it is **Sponsored** (recommended; trust, and advertising rules), and how ties and several slots (1, 2, 3) work.
- Needs: placement slots table (area, period, store, amount, status), bidding rules service, payment and refund for stores, ordering applied in the store list (`catalog/stores`), a store-facing portal page, admin controls and reports. Large; build after 1 and 2.

## Suggested order
P13 store-owned offers (1), P14 offer fees (2), P15 paid placement (3).

## Decisions
1. Store staff create offers for their own store; an admin can create for chosen stores.
2. Fee: paid per offer online (Razorpay) before the offer goes live; a setting (global, per-store override, 0 = free).
3. Placement: weekly slot per pincode, highest confirmed and paid bid wins first place, shown as Sponsored; an outbid store is refunded.
