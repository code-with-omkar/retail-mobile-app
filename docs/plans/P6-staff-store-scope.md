# Staff store scope: the list and the actions disagree (task 6.10)

Status: **decided 2026-10-09: option B, built and tested.** `OrdersController` now lists (and opens) orders for the staff member's operating store only (`OperatingStoresAsync`); application admins still see every store. Tests: `StaffOrderScopeTests`. (The dev database has two test rows that made it visible, see "How it was found".)

## What happens today

| What staff do | Which stores it uses | Where in the code |
| --- | --- | --- |
| See the orders list (`GET api/orders`) | **Every store the user is assigned to** (`UserStoreAssignments`, active and in date) | `AuthorizationScopeService.ResolveForUserAsync`, `GetScopedOrderSummariesAsync` |
| Change an order's status (`POST api/operations/orders/{id}/status`) | **Only the single `Users.StoreId`** (the user's primary store), which must also match the `store_id` claim in the sign-in token | `OrderOperationsService.ChangeStatusAsync`, `CurrentUserContextResolver`, `TryTransitionOrderAsync` |

So a staff member assigned to two stores sees orders from both but gets "Order not found in the authorized store scope" (404) when acting on the second store's orders.

## How it was found

Testing task 6.7 (portal status changes) with `storemanager@example.test`. Its primary store was Harbor Point; the test orders were at Kharghar and Panvel. To test, the dev database now has two extra assignments for that user (rows created by `dev-portal-check`) and its primary store was set to Kharghar. Backups taken first: `retail-mobile-app_pre-storemanager-stores.bak` and `retail-mobile-app_pre-storemanager-primary-store.bak`.

## Options

**A. Let staff act on every store they are assigned to (recommended).**
`ChangeStatusAsync` checks the order's store against the user's assigned stores (the same set the list uses) instead of the single primary store. An application admin keeps access to all stores. The sign-in token's `store_id` claim stays but is no longer the gate for status changes. No database change.
- Pro: the list and the actions agree; multi-store staff (`multistorestaff`) work as the name suggests.
- Con: it changes who may change an order's status, so it needs your approval (authorization rules). Existing single-store staff are unaffected (their assigned set is one store).
- Tests: a staff user acts on an assigned store's order; is refused (404) for an unassigned store's order; an inactive or expired assignment is refused; an application admin acts everywhere; the list and the actions return the same stores.

**B. Keep it as is and make the list match** (show only the primary store's orders).
- Pro: no change to who can act. Con: assignments to other stores then do nothing for orders, which looks like the opposite of the intent of having assignments.

**C. Leave both as they are** and treat multi-store staff as unsupported for now (document it).

## Not changed by any option

The transition rules (Pending to Accepted or Rejected, then Preparing, Ready, Completed), the customer-side cancel, and payments.
