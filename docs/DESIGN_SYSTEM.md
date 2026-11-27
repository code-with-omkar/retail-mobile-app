# QuickCart Customer App — Final Design System: "Electric Yellow" v2

Status: **FINAL v2** (revised 2026-10-09) · Applies to: Flutter customer app (`mobile/`) · Themes: **Light** and **Dark**
Source of truth for visuals: Design canvas "QuickCart App Design Plan" → page *Client options* → boards **FINAL — Electric Yellow (light)** and **FINAL — Electric Yellow (dark)** (both drawn from the part `FinalScreens.dc.html`).
Brand name and logo: **not decided** — keep `[BRAND NAME]` / `[LOGO]` placeholders.

> v2 changes from v1 (2026-10-06): smaller cards, fixed 12 px gaps between all cards, every card a single solid colour (no inner tinted image panels, no gradient hero tile), **orange `#FF7A1A` replaces hot pink** for deals/alerts, page gradient ends in violet instead of magenta.
> Supersedes the earlier "Neon Market" theme (lime `#8DFF00`) and the MolBhav palette. The current `mobile/lib/core/theme/*.dart` files still hold Neon Market values and must be updated to the tokens below.

---

## 1. Principles

1. **Small cards, real spacing.** Cards are compact (see §5 sizes). Every gap between cards is **12 px**; sections are separated by **24 px**. Never butt cards against each other.
2. **One card = one solid colour.** No split backgrounds inside a card (no tinted image panel inside a differently coloured card, no gradient tiles). Photos sit directly on the card colour; placeholders are a dashed outline only.
3. **Gradient only on the page.** The electric gradient is the page background; nothing else uses a gradient.
4. **No plain white surfaces.** Light theme cards use light tints (lavender, light yellow, light orange, light green); dark theme cards use one deep indigo.
5. **Yellow = action.** Buttons, cart pill, active nav, selected chip, mic/pin buttons, ETA card: `#FFD400` with ink `#141118`. Never white text on yellow.
6. **Orange = deals/alerts. Green = in stock / savings.** Pink is no longer used.
7. **Black nav anchors the screen** in both themes.
8. Text contrast ≥ 4.5:1 (≥ 3:1 for ≥ 24 px). Touch targets ≥ 44 px (in-card +/− 32 px with 44 px hit area). Colour never the only signal.

---

## 2. Colour tokens

### 2.1 Core palette (both themes)

| Token | Hex | Use | Text on it |
|---|---|---|---|
| `action` | `#FFD400` | Primary buttons, Add, cart pill, active nav, selected pack size, pin + mic buttons, ETA card, Welcome tile 1 | `#141118` |
| `deal` | `#FF7A1A` | "Today's deals" card, notification dot, heart icon, alerts | `#141118` |
| `success` | `#02F34C` | In-stock chip text/dot (on black chip), "You save" text on page | — |
| `ink` | `#141118` | Text on yellow/orange/green/tints; nav bar (light); stickers (light) | — |

### 2.2 Light theme

| Token | Value | Notes |
|---|---|---|
| `pageGradient` | `linear 165° #2A1BFF 0% → #5B16EE 55% → #8A10D8 100%` | Every screen background |
| `onPage` / `onPageMuted` | `#FFFFFF` / `#E9E4FF` | Titles, address / meta, Marathi lines |
| `onPageLink` | `#FFD400` | "See all" |
| `card` | `#EAE4FF` (lavender) | Search bar, bell, pack-size chips (unselected), "Fresh today" chip, quantity box |
| `onCard` / `onCardMuted` | `#141118` / `#5E5A66` | |
| `etaCard` | `#FFD400`, text `#141118`, meta `#3A3640` | Home: "From [STORE] · [ETA] min" |
| `dealCard` | `#FF7A1A`, text `#141118` | Home: "Today's deals" |
| `categoryTile1..4` | `#B9FFCF` · `#FFD3B0` · `#FFF1A8` · `#EAE4FF` | Veggies · Fruits · Dairy · Grocery; icon `#141118`; label below tile in `onPage` |
| `productCard1..3` | `#FFD3B0` · `#FFF1A8` · `#B9FFCF` | Whole card one colour; text `#141118`, unit `#5E5A66`; photo placeholder dashed `rgba(20,17,24,.22)` |
| `productDetailPanel` | `#FFD3B0` | Inset photo card on detail |
| `sticker` | bg `#141118`, text `#FFD400` | Discount badge "−22%" |
| `addButton` / `stepper` | bg `#141118`, icons/text `#FFD400` | On product cards |
| `navBar` | `#141118`, icons `#FFFFFF`, active `#FFD400` / `#141118` | Floating capsule; also back/heart buttons on detail |
| `statusChip` | bg `#141118`, text + dot `#02F34C` | "In stock" |
| `input` | bg `#141118`, text `#FFFFFF`, placeholder `#B9B4C2`, border `#FFD400` 2 px | Phone number |
| `welcomeTiles` | `#FFD400` · `#B9FFCF` · `#FFD3B0` | text `#141118` |
| `notificationDot` | `#FF7A1A` | |

### 2.3 Dark theme

| Token | Value | Notes |
|---|---|---|
| `pageGradient` | `linear 165° #0E0A3A 0% → #1E0F55 55% → #2E0B5E 100%` | Every screen background |
| `onPage` / `onPageMuted` | `#FFFFFF` / `#C9C2F0` | |
| `onPageLink` | `#FFD400` | |
| `card` | `#1E1846` (deep indigo) | All cards: search, bell, chips, product cards, detail photo panel, quantity box |
| `onCard` / `onCardMuted` | `#FFFFFF` / `#C9C2F0` | |
| `etaCard` / `dealCard` | `#FFD400` / `#FF7A1A`, text `#141118` | Same as light |
| `categoryTile1..4` | `#02F34C` · `#FF7A1A` · `#FFD400` · `#B9A8FF` | Bright solids; icon `#141118`; label `onPage` |
| `productCard` | `#1E1846` | Single indigo for all; photo placeholder dashed `rgba(255,255,255,.28)` |
| `sticker` | bg `#FFD400`, text `#141118` | |
| `addButton` / `stepper` | bg `#FFD400`, icons/text `#141118` | |
| `navBar` | `#0A0720`, icons `#FFFFFF`, active `#FFD400` / `#141118` | Also back/heart buttons |
| `statusChip` | bg `#0A0720`, text + dot `#02F34C` | |
| `input` | bg `#1E1846`, text `#FFFFFF`, placeholder `#C9C2F0`, border `#FFD400` 2 px | |
| `welcomeTiles` | `#FFD400` · `#02F34C` · `#FF7A1A` | text `#141118` |
| `notificationDot` / heart | `#FF7A1A` | |

Theme switch: follow system (`ThemeMode.system`) with a manual override in Account.

---

## 3. Typography

| Role | Font | Size / line | Weight | Notes |
|---|---|---|---|---|
| Welcome headline | Bricolage Grotesque | 36/38 | 800 | −3 % |
| Product name (detail) | Bricolage Grotesque | 30/32 | 800 | Marathi name beside it in Mukta 22 |
| Price (detail) | Bricolage Grotesque | 34 | 800 | tabular, `₹` + Indian grouping |
| ETA value | Bricolage Grotesque | 28/28 | 800 | −3 % |
| Greeting | Bricolage Grotesque | 26/30 | 800 | |
| Section title | Bricolage Grotesque | 18 | 700 | |
| Price (card) | Bricolage Grotesque | 16 | 800 | tabular |
| Card title | Plus Jakarta Sans | 13–14 / 16–17 | 800 | |
| Body / search | Plus Jakarta Sans | 14 | 500 | |
| Meta / unit / label | Plus Jakarta Sans | 11–12 | 600–700 | "DELIVER TO" caps +8 % tracking |
| Marathi / Devanagari | Mukta | +2 px vs Latin | 500–700 | |

Flutter: `google_fonts` (Bricolage Grotesque, Plus Jakarta Sans, Mukta); `fontFamilyFallback: ['Mukta']`.

---

## 4. Shape, spacing, depth

- **Spacing scale:** 4 · 8 · 12 · 16 · 20 · 24.
  - Screen gutter **20** · gap between cards (grid/row) **12** · between blocks in a section **16** · between sections **24** · card padding **12–14**.
- **Radius:** sticker 6–10 · pack-size chip 14 · photo placeholder 14 · product card 20 · category tile 20 · ETA/deal/welcome cards 22 · detail photo panel 28 · buttons/search/nav **pill**.
- **Depth:** cards flat (no shadow). Floating only (nav capsule, cart pill, Add button): `0 12px 32px rgba(20,17,24,.22)`.
- Stickers are not rotated in v2.

---

## 5. Components (v2 sizes)

- **Address header:** yellow pin 40 · "DELIVER TO" + address · bell 44 on `card` with orange dot.
- **Search:** 48 high pill on `card`, yellow mic 38.
- **Promo row (Home):** 2 cards, 84 high, 12 gap — ETA (yellow, 1.25 fr) + Today's deals (orange, 1 fr).
- **Category row:** 4 tiles **64×64**, 12 gap, icon 24, label below the tile (not inside).
- **Product card:** **136 wide**, padding 12, gap 8; dashed photo slot 64 high; name 13/800, unit 11; price 16 + add (32) or stepper (32 high). Horizontal scroll, 12 gap.
- **Bottom bar:** black nav capsule (48 px items) + yellow cart pill 60 high, 10 gap.
- **Product detail:** inset photo card 300 high (16 top, 20 sides), black back/heart buttons 42, sticker bottom-right; status chips (8 gap); name; price row; **3 separate pack-size chips** 44 high with 10 gap (selected = yellow); sticky bottom: quantity box 58 + yellow "Add · ₹28".
- **Welcome:** `[LOGO]` 48 + EN/मराठी switch on black; **3 small cards in a row** 112 high, 12 gap; headline + Marathi line; phone input 54; "Get OTP" 54.

Screen inventory and flows (OTP, Search, Listing, Cart, Checkout COD, Order placed, Tracking, Notifications, Account, empty/error states) live on the *Working files* page of the canvas; restyle them with these tokens and sizes during build.

---

## 6. Product decisions captured with the design

- Sign-in: **phone + OTP** (API endpoints still to add).
- Payment v1: **Cash on delivery only**; UPI/Card shown disabled "Coming soon".
- Languages: **English + Marathi**.
- Nav: Home · Browse · Orders · Account + floating cart pill.

---

## 7. Flutter mapping

```dart
// lib/core/theme/app_palette.dart — raw values (Electric Yellow v2)
abstract final class AppPalette {
  static const Color action = Color(0xFFFFD400);
  static const Color deal = Color(0xFFFF7A1A);
  static const Color success = Color(0xFF02F34C);
  static const Color ink = Color(0xFF141118);
  static const Color etaMuted = Color(0xFF3A3640);

  // Light
  static const List<Color> pageLight = [Color(0xFF2A1BFF), Color(0xFF5B16EE), Color(0xFF8A10D8)];
  static const Color lavender = Color(0xFFEAE4FF);
  static const Color lightYellow = Color(0xFFFFF1A8);
  static const Color lightOrange = Color(0xFFFFD3B0);
  static const Color lightGreen = Color(0xFFB9FFCF);
  static const Color onPageMutedLight = Color(0xFFE9E4FF);
  static const Color onCardMutedLight = Color(0xFF5E5A66);

  // Dark
  static const List<Color> pageDark = [Color(0xFF0E0A3A), Color(0xFF1E0F55), Color(0xFF2E0B5E)];
  static const Color indigoCard = Color(0xFF1E1846);
  static const Color lilac = Color(0xFFB9A8FF);
  static const Color onMutedDark = Color(0xFFC9C2F0);
  static const Color navDark = Color(0xFF0A0720);
}

// lib/core/theme/app_dimens.dart
abstract final class AppSpacing {
  static const double xs = 4, sm = 8, md = 12, lg = 16, gutter = 20, section = 24;
  static const double cardGap = md;   // between any two cards
}
abstract final class AppRadius {
  static const double chip = 14, productCard = 20, tile = 20, promoCard = 22, panel = 28, pill = 999;
}
abstract final class AppSizes {
  static const double categoryTile = 64, productCardWidth = 136, promoCardHeight = 84,
      searchHeight = 48, addButton = 32, packChipHeight = 44, ctaHeight = 58;
}
```

| `AppColors` (ThemeExtension) field | Light | Dark |
|---|---|---|
| `pageGradient` (LinearGradient ≈165°, stops 0/.55/1) | `pageLight` | `pageDark` |
| `onPage` / `onPageMuted` | `#FFFFFF` / `#E9E4FF` | `#FFFFFF` / `#C9C2F0` |
| `card` | `#EAE4FF` | `#1E1846` |
| `onCard` / `onCardMuted` | `#141118` / `#5E5A66` | `#FFFFFF` / `#C9C2F0` |
| `action` / `onAction` | `#FFD400` / `#141118` | same |
| `deal` / `onDeal` | `#FF7A1A` / `#141118` | same |
| `success` | `#02F34C` | same |
| `categoryTiles` | `[#B9FFCF, #FFD3B0, #FFF1A8, #EAE4FF]` | `[#02F34C, #FF7A1A, #FFD400, #B9A8FF]` |
| `productCards` | `[#FFD3B0, #FFF1A8, #B9FFCF]` (cycle) | `[#1E1846]` |
| `addButtonBg` / `addButtonFg` | `#141118` / `#FFD400` | `#FFD400` / `#141118` |
| `stickerBg` / `stickerFg` | `#141118` / `#FFD400` | `#FFD400` / `#141118` |
| `nav` | `#141118` | `#0A0720` |
| `statusChipBg` | `#141118` | `#0A0720` |

`ColorScheme`: `primary = action`, `onPrimary = ink`, `secondary = deal`, `tertiary = success`, `surface = card`, `onSurface = onCard`. Scaffold background transparent; wrap each screen body in a `DecoratedBox` with `pageGradient`.

---

## 8. Reference images

Rendered from the final v2 canvas boards (2× PNG), in `docs/design-reference/` and bundled in `Electric-Yellow-Design-Reference.pdf`.

| File | Shows |
|---|---|
| `01-final-electric-yellow-light-board.png` | Light board: palette + Welcome, Home, Product detail |
| `02-final-electric-yellow-dark-board.png` | Dark board: palette + Welcome, Home, Product detail |
| `03-light-welcome.png` · `04-light-home.png` · `05-light-product-detail.png` | Light screens, 390×844 @2× |
| `06-dark-welcome.png` · `07-dark-home.png` · `08-dark-product-detail.png` | Dark screens, 390×844 @2× |

When images and this doc disagree, the token tables in §2 and sizes in §4–5 win.

## 9. Known gaps

- Brand name, logo and app icon still open.
- Existing theme code in `mobile/lib/core/theme/` uses the superseded Neon Market palette — update to v2 before building screens.
- Other screens on the *Working files* page still show the older large-card layout; restyle during build using §4–5.
