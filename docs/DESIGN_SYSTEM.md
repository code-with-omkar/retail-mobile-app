# QuickCart Customer App — Final Design System: "Electric Yellow"

Status: **FINAL** (approved 2026-10-06) · Applies to: Flutter customer app (`mobile/`) · Themes: **Light** and **Dark**
Source of truth for visuals: Design canvas "QuickCart App Design Plan" → page *Client options* → boards **FINAL — Electric Yellow (light)** and **FINAL — Electric Yellow (dark)**.
Brand name and logo: **not decided** — keep `[BRAND NAME]` / `[LOGO]` placeholders.

> This file supersedes the earlier "Neon Market" theme (lime `#8DFF00` primary) and the MolBhav palette. The current `mobile/lib/core/theme/*.dart` files still hold Neon Market values and must be updated to the tokens below.

---

## 1. Principles

1. **Electric gradient ground, solid tinted cards.** Page background is always a gradient; cards/tiles are always **solid opaque** colours. No translucent "glass" cards, no muddy overlays.
2. **No plain white surfaces.** Cards use light tints of the palette (lavender, light yellow, light pink, light green) in light theme and deep indigo in dark theme.
3. **Yellow = action.** Every primary action (buttons, Add, cart pill, active nav, selected chip, stickers) is yellow `#FFD400` with ink text `#141118`. Never white text on yellow.
4. **Pink = deals/alerts. Green = in stock / done / savings.**
5. **Black nav anchors the screen** in both themes.
6. Text contrast ≥ 4.5:1 (≥ 3:1 for ≥ 24 px). Touch targets ≥ 44 px. Colour never the only signal (badges carry text).

---

## 2. Colour tokens

### 2.1 Core palette (both themes)

| Token | Hex | Use | Text on it |
|---|---|---|---|
| `action` | `#FFD400` | Primary buttons, Add, cart pill, active nav, selected chip, discount sticker, mic/pin button | `#141118` |
| `actionGradient` | `linear 135° #FFE14D 0% → #FFD400 50% → #FFB800 100%` | Hero/ETA tile only | `#141118` (muted `#3A3640`) |
| `deal` | `#FB1A8E` | "Today's deals" tile, notification dot, alerts | `#141118` |
| `success` | `#02F34C` | In-stock dot, savings text, completed steps | — (use as text only on dark chips) |
| `ink` | `#141118` | Text on yellow/pink/green/tints; nav bar (light) | — |

### 2.2 Light theme

| Token | Value | Notes |
|---|---|---|
| `pageGradient` | `linear 165° #2A1BFF 0% → #6A11E8 45% → #B00699 100%` | Every screen background (Welcome: stops 0/50/100) |
| `onPage` | `#FFFFFF` | Greeting, section titles, address |
| `onPageMuted` | `#E9E4FF` | "DELIVER TO", meta, Marathi sub-labels |
| `onPageLink` | `#FFD400` | "See all" |
| `card` | `#EAE4FF` (lavender) | Search bar, bell button, product cards, pack-size picker, "Fresh today" chip, quantity box |
| `cardBorder` | `#EAE4FF` | Same as card (no visible hairline) |
| `onCard` | `#141118` | |
| `onCardMuted` | `#5E5A66` | Units ("1 kg"), placeholder text |
| `onCardAccent` | `#B0008A` | Heart / favourite icon on cards |
| `tileBuyAgain` | `#FFF1A8` (light yellow) | text `#141118`, meta `#5E5A66` |
| `tileCategory1..4` | `#B9FFCF` · `#FFC6E2` · `#FFF1A8` · `#EAE4FF` | Veggies · Fruits · Dairy · Grocery — text/icons `#141118` |
| `productImageBg1..3` | `#FFC6E2` · `#FFF1A8` · `#B9FFCF` | Behind product photos |
| `stepper` | bg `#141118`, text `#FFFFFF`, minus `#3A3640`, plus `#FFD400` / `#141118` | On product cards |
| `navBar` | `#141118`, icons `#FFFFFF`, active pill `#FFD400` / `#141118` | Floating capsule |
| `chipSelected` | `#FFD400` / `#141118` | Pack size, filters |
| `statusChip` | bg `#141118`, text + dot `#02F34C` | "In stock" |
| `savingsText` | `#02F34C` on page gradient | "You save ₹8" |
| `input` | bg `#141118`, text `#FFFFFF`, border `#FFD400` 2 px | Phone number field |
| `welcomeTiles` | `#FFD400` · `#B9FFCF` · `#FFC6E2` | text `#141118` |
| `notificationDot` | `#FB1A8E` | |

### 2.3 Dark theme

| Token | Value | Notes |
|---|---|---|
| `pageGradient` | `linear 165° #0E0A3A 0% → #24105E 45% → #4A0A4A 100%` | Every screen background |
| `onPage` | `#FFFFFF` | |
| `onPageMuted` | `#C9C2F0` | |
| `onPageLink` | `#FFD400` | |
| `card` | `#1E1846` (deep indigo) | Search, bell, product cards, picker, chips, quantity box |
| `cardBorder` | `#2E2766` | |
| `onCard` | `#FFFFFF` | |
| `onCardMuted` | `#C9C2F0` | |
| `onCardAccent` | `#FB1A8E` | Heart icon |
| `tileBuyAgain` | `#2E2766` | text `#FFFFFF`, meta `#C9C2F0` |
| `tileCategory1..4` | `#02F34C` · `#FB1A8E` · `#FFD400` · `#B9A8FF` | Bright solids — text/icons `#141118` |
| `productImageBg1..3` | `#FFC6E2` · `#FFF1A8` · `#B9FFCF` | Same as light |
| `stepper` | bg `#EAE4FF`, text `#141118`, minus `#C9C2F0`, plus `#FFD400` / `#141118` | |
| `navBar` | `#0A0720`, icons `#FFFFFF`, active `#FFD400` / `#141118` | |
| `chipSelected` | `#FFD400` / `#141118` | |
| `statusChip` | bg `#0A0720`, text + dot `#02F34C` | |
| `input` | bg `#1E1846`, text `#FFFFFF`, border `#FFD400` 2 px | |
| `welcomeTiles` | `#FFD400` · `#02F34C` · `#FB1A8E` | text `#141118` |
| `notificationDot` | `#FB1A8E` | |

Theme switch: follow system (`ThemeMode.system`) with a manual override in Account.

---

## 3. Typography

| Role | Font | Size / line | Weight | Notes |
|---|---|---|---|---|
| Display (ETA, "Order placed") | Bricolage Grotesque | 52/48 · 40/44 | 800 | letter-spacing −3 % to −4 % |
| H1 screen title | Bricolage Grotesque | 28–30 / 32–34 | 800 | −3 % |
| H2 section | Bricolage Grotesque | 20/26 | 700 | |
| Card title | Plus Jakarta Sans | 14–16 / 18–22 | 800 | |
| Body | Plus Jakarta Sans | 14/20 | 500 | |
| Caption / meta | Plus Jakarta Sans | 11–12 / 14–16 | 600–700 | "DELIVER TO" uses +8 % tracking, caps |
| Price | Bricolage Grotesque | 18 (cards) · 40 (detail) | 800 | tabular figures, `₹` + Indian grouping |
| Marathi / Devanagari | Mukta | +2 px vs Latin | 500–700 | e.g. `टोमॅटो`, `भाज्या` |

Flutter: `google_fonts` (Bricolage Grotesque, Plus Jakarta Sans, Mukta); `fontFamilyFallback: ['Mukta']`.

---

## 4. Shape, spacing, depth

- Radius: sticker 8 · photo areas 18 · cards/tiles 24 · hero tile 32 · detail photo bottom 40 · buttons/chips/nav **pill (999)**.
- Spacing: screen gutter **20**, block gap **14**, grid gap **10**, card padding **8–18**.
- Depth: cards flat (no shadow). Floating elements only (nav capsule, cart pill, Add button): `0 12px 32px rgba(20,17,24,.18)`.
- Discount sticker: rotated −5° (card) / −8° (detail).

---

## 5. Components (as drawn)

- **Address header:** yellow pin circle 40 · "DELIVER TO" muted caps · address 15/800 · bell 44 on `card` with pink dot.
- **Search bar:** 52 high pill on `card`, muted placeholder, yellow mic button 40.
- **Home bento:** left tall hero tile (`actionGradient`, ETA in ink 52 px) · right top pink "Today's deals" · right bottom "Buy again".
- **Category grid:** 4 tiles, 88 high, icon 26 + label 12/800.
- **Product card:** 158 wide on `card`, image 96 high on tinted bg, sticker top-left, name + muted unit, price + stepper or yellow `+` (34–36).
- **Bottom bar:** floating black nav capsule (Home · Browse · Orders · Account, 52 px items, active = yellow) + yellow cart pill (items count + total + black cart icon).
- **Product detail:** 360 tall image area (tint, rounded bottom 40), back + heart buttons on `card`, status chip, name + Marathi name, price 40 + struck MRP + "You save", pack-size segmented picker, sticky bottom: quantity box + yellow "Add · ₹28".
- **Welcome:** gradient bg, `[LOGO]` slot, EN / मराठी switch, 3-tile bento, headline "Your local shop, now in your hand." + Marathi line, phone input, yellow "Get OTP".

Screen inventory and flows (OTP, Search, Listing, Cart, Checkout COD, Order placed, Tracking, Notifications, Account, empty/error states) live on the *Working files* page of the canvas; restyle them with these tokens during build.

---

## 6. Product decisions captured with the design

- Sign-in: **phone + OTP** (API endpoints still to add).
- Payment v1: **Cash on delivery only**; UPI/Card shown disabled "Coming soon".
- Languages: **English + Marathi**.
- Nav: Home · Browse · Orders · Account + floating cart pill.

---

## 7. Flutter mapping

```dart
// lib/core/theme/app_palette.dart — raw values (Electric Yellow)
abstract final class AppPalette {
  static const Color action = Color(0xFFFFD400);
  static const Color actionLight = Color(0xFFFFE14D);
  static const Color actionDeep = Color(0xFFFFB800);
  static const Color deal = Color(0xFFFB1A8E);
  static const Color success = Color(0xFF02F34C);
  static const Color ink = Color(0xFF141118);
  static const Color heroMuted = Color(0xFF3A3640);
  static const Color magentaText = Color(0xFFB0008A);

  // Light
  static const List<Color> pageLight = [Color(0xFF2A1BFF), Color(0xFF6A11E8), Color(0xFFB00699)];
  static const Color lavender = Color(0xFFEAE4FF);
  static const Color lightYellow = Color(0xFFFFF1A8);
  static const Color lightPink = Color(0xFFFFC6E2);
  static const Color lightGreen = Color(0xFFB9FFCF);
  static const Color onPageMutedLight = Color(0xFFE9E4FF);
  static const Color onCardMutedLight = Color(0xFF5E5A66);
  static const Color stepperMinusLight = Color(0xFF3A3640);

  // Dark
  static const List<Color> pageDark = [Color(0xFF0E0A3A), Color(0xFF24105E), Color(0xFF4A0A4A)];
  static const Color indigoCard = Color(0xFF1E1846);
  static const Color indigoLine = Color(0xFF2E2766);
  static const Color lilac = Color(0xFFB9A8FF);
  static const Color onMutedDark = Color(0xFFC9C2F0);
  static const Color navDark = Color(0xFF0A0720);
}
```

| `AppColors` (ThemeExtension) field | Light | Dark |
|---|---|---|
| `pageGradient` (LinearGradient, begin topLeft→bottomRight ≈165°) | `pageLight` stops 0/.45/1 | `pageDark` stops 0/.45/1 |
| `onPage` / `onPageMuted` | `#FFFFFF` / `#E9E4FF` | `#FFFFFF` / `#C9C2F0` |
| `card` / `cardBorder` | `#EAE4FF` / `#EAE4FF` | `#1E1846` / `#2E2766` |
| `onCard` / `onCardMuted` / `onCardAccent` | `#141118` / `#5E5A66` / `#B0008A` | `#FFFFFF` / `#C9C2F0` / `#FB1A8E` |
| `action` / `onAction` | `#FFD400` / `#141118` | same |
| `deal` / `onDeal` | `#FB1A8E` / `#141118` | same |
| `success` | `#02F34C` | same |
| `tileBuyAgain` | `#FFF1A8` | `#2E2766` |
| `categoryTiles` | `[#B9FFCF, #FFC6E2, #FFF1A8, #EAE4FF]` | `[#02F34C, #FB1A8E, #FFD400, #B9A8FF]` |
| `nav` | `#141118` | `#0A0720` |
| `stepperBg` / `onStepper` / `stepperMinus` | `#141118` / `#FFFFFF` / `#3A3640` | `#EAE4FF` / `#141118` / `#C9C2F0` |
| `statusChipBg` | `#141118` | `#0A0720` |

`ColorScheme`: `primary = action`, `onPrimary = ink`, `secondary = deal`, `tertiary = success`, `surface = card`, `onSurface = onCard`. Scaffold background is transparent; wrap each screen body in a `DecoratedBox` with `pageGradient`.

---

## 8. Reference images

Rendered from the final canvas boards (2× PNG). Stored with this doc as project file uploads / in `docs/design-reference/`; all eight are also bundled in `Electric-Yellow-Design-Reference.pdf`.

| File | Shows |
|---|---|
| `01-final-electric-yellow-light-board.png` | Light board: palette swatches + Welcome, Home, Product detail |
| `02-final-electric-yellow-dark-board.png` | Dark board: palette swatches + Welcome, Home, Product detail |
| `03-light-welcome.png` · `04-light-home.png` · `05-light-product-detail.png` | Light screens, 390×844 @2× |
| `06-dark-welcome.png` · `07-dark-home.png` · `08-dark-product-detail.png` | Dark screens, 390×844 @2× |

When images and this doc disagree, the token tables in §2 win.

## 9. Known gaps

- `[PRODUCT PHOTO]` placeholder label is low contrast on tinted image areas (placeholder only; real photos replace it).
- Brand name, logo and app icon still open.
- Existing theme code in `mobile/lib/core/theme/` uses the superseded Neon Market palette — update before building screens.
