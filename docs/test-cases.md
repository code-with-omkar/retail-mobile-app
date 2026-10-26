# Manual test cases

For the features built since the last round of testing. Tick the **Pass** column as you go; write what you saw in **Note** when something is off and send it to me.

## Setup

| What | How |
| --- | --- |
| APIs | Phone API `http://localhost:5067`, portal API `http://localhost:5000`. Ask Claude to restart them if they are down. |
| Portal | http://localhost:4200. Logins: `admin` (administrator), `storemanager` (store staff of Kharghar). Passwords are in user-secrets: `dotnet user-secrets list` in `src/QuickCommerce.Api` (keys `Authentication:Development...Password`). |
| Delivery partner | username `deliverypartner`, password in the same list (`...DeliveryPartnerPassword`). |
| Phone | USB connected and unlocked, `adb reverse tcp:5067 tcp:5067`, customer app launched with `--dart-define=API_BASE_URL=http://localhost:5067`. |
| **Text-message codes** | In development nothing is sent. The code is written to the phone API's log. Read the newest one with: `Select-String "DEVELOPMENT sign-in code" $env:TEMP\api.log \| Select-Object -Last 1` (PowerShell). The line ends with the 6-digit code. |
| Test data | Use made-up numbers like `9000011111`, `9000022222` and throwaway accounts. Do not use your own account for the deletion cases. |

---

## A. Phone code sign-in (customer app)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| A1 | Profile, Sign in / Register. | Welcome shows **Continue with phone**, then Log in and Create account. No Google button (it is not set up). | ☐ | |
| A2 | Continue with phone, type `12345`, Get OTP. | "Enter a 10-digit number". Nothing sent. | ☐ | |
| A3 | Type `5876543210` (starts with 5). | Same message: not a mobile number. | ☐ | |
| A4 | Type `9000011111`, Get OTP. | Code screen: "Enter the 6-digit code sent to +91 90•••••11", countdown "Resend code in 0:30". | ☐ | |
| A5 | Type a wrong code (for example 000000), Verify. | "That code is not valid or has expired…". Boxes cleared. Still signed out. | ☐ | |
| A6 | Type the right code from the log, Verify. | Signed in, back on Home. Profile shows the phone number and **Phone number verified**. | ☐ | |
| A7 | Profile, Log out. Sign in again with the same number (wait for the countdown, get a new code). | Same account (name and orders the same). Only one account exists for that number. | ☐ | |
| A8 | Ask for a code, then tap **Resend code** after the countdown, then use the **first** code. | The first code no longer works. The newest does. | ☐ | |
| A9 | Ask for a code and enter wrong codes five times, then the right one. | After five wrong tries even the right code is refused; ask for a new one. | ☐ | |
| A10 | Ask for a code, wait 5 minutes, use it. | Refused (expired). | ☐ | |
| A11 | Ask for codes for the same number six times in an hour (waiting 30 s between). | The sixth is refused: "Too many attempts…". | ☐ | |
| A12 | Use the same right code twice (go back and enter it again). | Second use is refused. | ☐ | |
| A13 | Switch to Marathi (Profile, Language) and open the phone and code screens. | Texts are in Marathi, nothing cut off. | ☐ | |

## B. Verify the phone of an email account (customer app)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| B1 | Create an email account with phone `9000022222` (Create account). Profile. | A **Verify phone number** row ("So you can sign in with a code"). | ☐ | |
| B2 | Tap it, Get OTP, enter the code from the log. | "Number verified…". Profile now shows **Phone number verified**. | ☐ | |
| B3 | Log out, then Continue with phone, `9000022222`, code. | You land in the **same** account (the email account's name/orders), not a new one. | ☐ | |
| B4 | Edit profile and change the phone to another number. | Profile shows **Verify phone number** again (a new number must be verified). | ☐ | |
| B5 | With a second account, try to verify `9000022222`. | "This number is already verified on another account." | ☐ | |
| B6 | Before verifying: a phone typed into an email account's profile but **not** verified; sign in with that number by code. | You get a **new** account, not the email one. | ☐ | |

## C. Google sign-in (only after the setup in testing notes A8)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| C1 | Welcome. | **Continue with Google** is shown. | ☐ | |
| C2 | Tap it, choose an account. | Signed in. Profile shows your Google name and email. | ☐ | |
| C3 | Close the account picker without choosing. | Nothing happens, no error. | ☐ | |
| C4 | Create an email account with the same address first, then use Google with that address. | You enter that account, and the old password stops working (use "Forgot password" to confirm). | ☐ | |

## D. Account deletion (throwaway account only)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| D1 | Create an account, add an address. Profile, Delete account. | Explanation, a password box and a "Type DELETE" box. | ☐ | |
| D2 | Press Delete my account with nothing filled in. | Both boxes complain. Nothing deleted. | ☐ | |
| D3 | Wrong password with DELETE. | "Your password is not correct." | ☐ | |
| D4 | Place a cash order, then try to delete. | "Finish or cancel your open orders first." Account stays. | ☐ | |
| D5 | Cancel the order (or let the store complete it), then delete with the right password and DELETE. | "Your account was deleted." You are signed out on Home. | ☐ | |
| D6 | Try to log in with the old email and password. | Refused. | ☐ | |
| D7 | Portal, Orders: find that account's old order. | The order is still listed; the customer shows as "Deleted customer", with no address. | ☐ | |
| D8 | Phone-code account: delete it. | No password box; DELETE alone works. | ☐ | |

## E. Catalogue, pictures and stock (portal, then phone)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| E1 | Portal as `admin`: Catalog. | List with thumbnails, search box, **New product**. | ☐ | |
| E2 | New product: SKU `TC-1`, name, category, unit `250 g`, price `40`, MRP `50`, Create. | Created; the editor opens with one size marked **default**. | ☐ | |
| E3 | Create another with the same SKU. | "That SKU is already used." | ☐ | |
| E4 | Price `0`, or MRP below price. | Refused with a clear message. | ☐ | |
| E5 | Edit details: change name and description, Save. Add a Marathi name, Save Marathi. | Saved. After you Remove Marathi it is gone. | ☐ | |
| E6 | Add a second size `500 g`, price `70`. Make it default. | Product price in the list becomes ₹70; the first size can now be switched off. | ☐ | |
| E7 | Try to switch off the default size. | Not allowed (box disabled / message). | ☐ | |
| E8 | Upload a JPEG/PNG photo. | Thumbnail shows. | ☐ | |
| E9 | Rename a text file to `x.jpg` and upload it. | Refused ("Use a JPEG, PNG or WebP picture"). | ☐ | |
| E10 | Upload a photo over 2 MB. | Refused ("2 MB or smaller"). | ☐ | |
| E11 | Replace the photo, then Remove picture. | Old picture replaced; after Remove the placeholder letter shows. | ☐ | |
| E12 | Portal as `storemanager`: Catalog. | List only, **no** Edit or New buttons. | ☐ | |
| E13 | Stock: tick **Sells here** for the new size, quantity 10, Save. | "…saved." | ☐ | |
| E14 | Phone: Kharghar store, find the product. | Shows with its photo, the price, and Marathi name when the language is Marathi. | ☐ | |
| E15 | Untick Sells here, Save. Refresh the phone list. | The product disappears from Kharghar's list. | ☐ | |
| E16 | `admin`, Stock: pick a store and change stock. | Works for any store; a store manager sees only their own store. | ☐ | |
| E17 | Switch the product off (On sale unticked). | Gone from the phone for every store. | ☐ | |

## F. Delivery partner

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| F1 | Customer app: place a cash order from Kharghar. Portal as `storemanager`: Accept, Start packing, Mark ready. | Status Ready. A **Delivery partner** picker and Give button appear. | ☐ | |
| F2 | Pick Demo Delivery Partner, Give. | Row shows "by Demo Delivery Partner". **Complete** is gone. | ☐ | |
| F3 | Partner app: sign in as `deliverypartner`. | The order is listed with address and "Collect ₹… cash". | ☐ | |
| F4 | Partner: open it, check Call customer, Navigate, items. | Phone dialler and maps open; items match the order. | ☐ | |
| F5 | Partner: **I picked it up**. | Status On the way. Customer app: order shows partner name, Call and a **4-digit code**. | ☐ | |
| F6 | Partner: wrong code, Delivered. | "That code is not right." Still On the way. | ☐ | |
| F7 | Partner: right code, but cash `1`. | Refused: cash must equal the amount. | ☐ | |
| F8 | Partner: right code, exact cash. | Delivered. Customer sees "delivered" and the code card disappears. | ☐ | |
| F9 | Five wrong codes in a row on another order. | Locked for 15 minutes with a clear message. | ☐ | |
| F10 | Online-paid order: partner app. | Shows "Paid online", no cash box; Delivered needs code only. | ☐ | |
| F11 | As `storemanager`, try to give a Ready order of another store, or choose a partner of another store. | Not possible (not in the list / refused). | ☐ | |

## G. Paid placement (portal)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| G1 | `storemanager`: Placement, pincode `410210`, Show weeks. | Four upcoming weeks, "No bid yet", least to bid ₹500. | ☐ | |
| G2 | Bid ₹400. | Refused (below the minimum). | ☐ | |
| G3 | Bid ₹500 and pay with Razorpay (needs methods enabled). | Bid shows **Leading**. | ☐ | |
| G4 | Bid again without paying, then Cancel. | Becomes Cancelled, no charge. | ☐ | |
| G5 | `admin`: bid higher for another store and pay. | First bid becomes **Outbid (refunded)**. | ☐ | |
| G6 | Phone with an address in that pincode in the leading week. | The leading store is first with a **Sponsored** tag, only if it delivers there. | ☐ | |
| G7 | `admin`: change the minimum bid in Rules. | New bids obey it. | ☐ | |

## H. Who can do what (spot checks)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| H1 | Partner app login with a customer or store-staff account. | "This account is not a delivery partner." | ☐ | |
| H2 | Customer app login with the `admin` or `storemanager` account. | "This account cannot be used in the customer app." | ☐ | |
| H3 | Portal sidebar as `storemanager`. | Catalog (read only), Stock, Orders, Offers, Placement; no user or role admin. | ☐ | |
| H4 | Open http://localhost:5067/media/products/ in a browser. | Not found (no folder listing). | ☐ | |

## I. Reports and the order list (portal)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| I1 | `storemanager`: Reports. | Last 7 days for Kharghar: revenue, orders, average, lost, cash/online, new customers, refunds; a bar per day; busiest hours; top products; running-low list. | ☐ | |
| I2 | Choose Today, Last 30 days, then Choose dates with an end before the start. | Each reloads; the bad range shows a clear message. | ☐ | |
| I3 | Place and deliver a cash order, then Reports, Today. | Orders +1, revenue rises by its total only after it is Delivered. | ☐ | |
| I4 | Cancel an order. | Counted under Lost, not in revenue. | ☐ | |
| I5 | `admin`: Reports, All stores. | Same figures for every store, a By store table, Platform income. Pick one store: table and income disappear. | ☐ | |
| I6 | Download orders (CSV), open in Excel. | One row per order with India times and money; no customer names, phones or addresses. | ☐ | |
| I7 | Orders page as `storemanager`; place a new order on the phone and wait up to 15 seconds. | The order appears at the top by itself, highlighted with **NEW**. | ☐ | |
| I8 | Accept it, then place another new order. | The new one is above the accepted one; delivered/cancelled orders stay at the bottom. | ☐ | |

## J. Order again and saved items (customer app)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| J1 | Orders, open a finished order, **Reorder**. | The Cart opens with the same items and quantities from the same store. | ☐ | |
| J2 | Before J1, put 1 of the same item in the cart. | Quantity becomes the old one plus the reordered one (not replaced). | ☐ | |
| J3 | In the portal Stock page, set one item of that order to 0 and reduce another to 1; reorder. | Zero item is listed as not available, the other is added at 1 and the cart says it was reduced. | ☐ | |
| J4 | Cart has items from a **different** store; Reorder. | A question "Start a new cart?". Keep my cart changes nothing; Order again empties it and fills it from the order. | ☐ | |
| J5 | Tap Reorder twice quickly. | Items are added once. | ☐ | |
| J6 | Open a product, tap the heart. | Heart fills; "Saved items" shows the product. | ☐ | |
| J7 | Tap the heart again. | Heart empties; the product leaves Saved items. | ☐ | |
| J8 | Log out, open a product, tap the heart. | You are taken to sign in; after signing in you return to the product. Nothing saved while logged out. | ☐ | |
| J9 | Save three products one after another. | Saved items shows the newest first. | ☐ | |
| J10 | Switch off a saved product in the portal Catalog. | It disappears from Saved items for the customer; switch it on again and it returns. | ☐ | |
| J11 | Sign in on another phone/account. | Each customer has their own list. | ☐ | |
| J12 | Marathi language. | Saved items, the heart tooltips and the reorder question are in Marathi. | ☐ | |

## K. Ratings (customer app and portal)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| K1 | Open a **delivered** order in the customer app. | A card "How was <store>?" with five stars, a comment box and Send rating (disabled until a star is chosen). | ☐ | |
| K2 | Choose 4 stars, write a comment, Send. | "Thank you for your rating."; the card now says "Your rating" with the stars and comment. | ☐ | |
| K3 | An order brought by the delivery partner. | Also "How was the delivery?" with its own stars (optional). A collected order has no delivery stars. | ☐ | |
| K4 | Change rating, pick other stars, Send. | The rating is replaced (still only one). Cancel leaves it as it was. | ☐ | |
| K5 | Type a comment containing < or >. | Refused with a clear message; stars kept. | ☐ | |
| K6 | A comment in Marathi with an emoji. | Accepted and shown back as written. | ☐ | |
| K7 | An order that is not delivered yet (Pending, On the way) or was cancelled. | No rating card. | ☐ | |
| K8 | Portal as `storemanager`: Reports, scroll to Customer ratings. | Average, count, spread bars, partner averages, latest comments with order numbers; no customer names. | ☐ | |
| K9 | Portal as `admin`, All stores then one store. | Ratings follow the store chosen. | ☐ | |
| K10 | Delete a throwaway account that gave a rating (A: see section D). | The rating's stars still count in the portal; the comment is gone. | ☐ | |

## L. Offers, discounts, your stores (customer app and portal)

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| L1 | Portal as `admin`: Offers, Offer fees. Set the app-open fee, e.g. 150. | Saved; staff see it in their form. | ☐ | |
| L2 | Portal: new offer, title, message, upload a JPEG picture, pick a product, 20% off, end tomorrow, Create. | Offer in the list with picture, "20% off <product>, until ...". | ☐ | |
| L3 | Customer app Home (store covered by the offer). | "Offers for you" banner with the picture and "20% off". | ☐ | |
| L4 | Find the product. | New price, old price struck through, saving shown; every size is cheaper. | ☐ | |
| L5 | Add it to the cart and check out. | Cart and order use the lower price; order detail keeps the old price as the MRP. | ☐ | |
| L6 | Tap the offer notification (bell). | The offer screen opens (picture, message, products, "Shop at ..."). | ☐ | |
| L7 | Tap the banner, then "Shop at <store>". | Same offer screen; the button goes to that store. | ☐ | |
| L8 | Portal: End discount now. Refresh the app. | Prices back to normal; banner gone. | ☐ | |
| L9 | Portal: new quiet offer (notification off) with a discount. | No notification arrives, the banner and prices still change. | ☐ | |
| L10 | Portal as store staff: tick "show as a pop-up when the app opens". | The price shown includes the extra app-open fee. | ☐ | |
| L11 | Pay it (when online payment is on), reopen the customer app. | The pop-up shows once; Close hides it; it does not come back until the app is restarted. | ☐ | |
| L12 | Two offers on one product (10% and 30%). | 30% applies, not 40%. | ☐ | |
| L13 | Sign in as a customer with past orders. | "Your stores" shows up to four stores you bought from, newest first; tap one that delivers to you to switch. | ☐ | |
| L14 | Open that store's Home. | "Bought before" shows what you ordered there; add one to the cart. | ☐ | |
| L15 | Cancel an order, look again. | A cancelled order does not count in "Your stores" or "Bought before". | ☐ | |

## M. Order note and out-of-stock choice

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| M1 | Customer app Checkout. | "If an item is out of stock" with Call me (chosen), Remove it, Replace it; and a "Note for the store" box (200 characters). | ☐ | |
| M2 | Choose Replace it, write "Ring the bell twice", Place order. | Order placed. Order detail shows "Your note" and "Replace it". | ☐ | |
| M3 | Portal as `storemanager`, Orders. | The order row shows the note and "If an item is out of stock: replace with a similar item". | ☐ | |
| M4 | Place an order with no note and nothing chosen. | Detail shows only "Call me"; the portal row has no note line. | ☐ | |
| M5 | Type a note with < or >. | Refused; the order is not placed. | ☐ | |
| M6 | Note in Marathi. | Accepted and shown back as written. | ☐ | |
| M7 | Switch the app to Marathi. | Checkout headings, choices and "Your note" are in Marathi. | ☐ | |

## N. Store prices and the discount ribbon

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| N1 | Portal as `storemanager`, Stock: set a Store price below the shared one, Save. | Saved; a "% below the shared price" note shows. | ☐ | |
| N2 | Customer app Home / product list at that store. | Lower price with the old price struck through; pink "X% OFF" ribbon on the card. | ☐ | |
| N3 | Another store's customer view. | Still the shared price, no ribbon. | ☐ | |
| N4 | Add it to the cart, check out. | Cart and order use the store price; order detail keeps the old price. | ☐ | |
| N5 | Change the store price while it is in a cart, then check out. | Customer is asked to review the new price, then can order. | ☐ | |
| N6 | Clear the store price. | Back to the shared price; ribbon gone. | ☐ | |
| N7 | Set a store price above the shared price. | Shown as the price, nothing struck through, no ribbon. | ☐ | |
| N8 | Store price with an offer % on the same product (admin offer). | The offer takes its percent off the store price; ribbon shows the total saving. | ☐ | |
| N9 | Enter 0 or 3 decimals. | Refused with a message. | ☐ | |

## O. Commission and earnings

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| O1 | Portal as `admin`: Commission. Set default 10%, one store 5%. | Saved; the store shows 5, others "(default 10%)". | ☐ | |
| O2 | Enter 101, -1 or 3 decimals. | Refused. | ☐ | |
| O3 | Place an order at each store (₹200 of items), mark them Delivered in Orders. | Both orders deliver normally. | ☐ | |
| O4 | Reports, Earnings by shop. | Per shop: sales ₹200, commission ₹20 (10%) or ₹10 (5%), "store keeps" includes delivery and handling fees. | ☐ | |
| O5 | Change the percent, look at the same report. | Old orders keep their commission; only new orders use the new percent. | ☐ | |
| O6 | Cancel an order. | It is not in earnings. | ☐ | |
| O7 | Portal as `storemanager`: Reports. | Only their own shop's earnings; no Commission page in the menu. | ☐ | |
| O8 | A product with a store price or offer. | Commission is on the discounted amount the customer paid. | ☐ | |

## P. Notification rule

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| P1 | Place an order. | Bell: "Order placed". | ☐ | |
| P2 | Store accepts, packs, assigns a partner; partner picks up. | No new bell notifications; the order screen timeline moves on each step. | ☐ | |
| P3 | Partner completes delivery with the code. | Bell: "Order delivered". | ☐ | |
| P4 | Store declines or customer cancels an order. | Bell: declined / cancelled. | ☐ | |

## Q. Delivery tracking

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| Q1 | Store gives a Ready order to the partner; partner opens My deliveries and picks it up. | A note "Sharing your distance with the customer while you deliver." shows; the phone asks for location permission once. | ☐ | |
| Q2 | Customer opens that order. | Within about 20 seconds a green line "<Name> is X km away, about N min" shows with the delivery code card. | ☐ | |
| Q3 | Partner moves closer. | The distance and minutes fall on the next refresh. | ☐ | |
| Q4 | Partner turns location off or the phone goes offline for over 5 minutes. | The tracking line disappears; the code card stays. | ☐ | |
| Q5 | Partner delivers with the code. | Tracking line gone; the order shows Delivered. | ☐ | |
| Q6 | Another customer or another partner tries the order. | No access (not found). | ☐ | |

## R. Store changes an order

| ID | Steps | Expected | Pass | Note |
| --- | --- | --- | --- | --- |
| R1 | Place a cash order of 3 of one item; portal as storemanager: Accept it. | The order row shows **Change items**. | ☐ | |
| R2 | Change items: set 1 instead of 3, add a reason, Apply. | Done; new total shown; the row total drops. | ☐ | |
| R3 | Customer app: open the order. | "The store changed your order": the item 3 to 1, the reason; the total is lower; a bell notification arrived. | ☐ | |
| R4 | Try to increase a quantity or remove every item. | Refused with a clear message (remove all: decline or cancel instead). | ☐ | |
| R5 | Order already Ready or out for delivery. | No Change items button; the API refuses. | ☐ | |
| R6 | A paid online order: change it. | Customer sees "Refund of ₹X is on its way", then "done" after a minute or two. Needs Razorpay methods enabled. | ☐ | |
| R7 | Delivery partner delivers a changed cash order. | Cash to collect is the new total. | ☐ | |
| R8 | Reports, Earnings by shop. | Sales and commission use the new product total. | ☐ | |

---

When you are done, tell me the IDs that failed with a short note each. I will fix them and add an automated test for every real bug.
