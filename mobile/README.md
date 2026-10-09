# QuickCart Customer Mobile App

Flutter customer app using the "Electric Yellow" design system (references in `docs/design`, light and dark). Screens: Welcome (phone + OTP, Google/Apple, email), Home, Categories, Category, Product detail (pack sizes), Cart, Checkout, Order success/tracking, Orders, Profile (language + theme), Address picker. English and Marathi (`lib/core/strings_mr.dart`); theme follows the system with a manual override. Design tokens: `lib/core/theme.dart`; shared widgets: `lib/core/widgets.dart`. Data is local seed data; auth, OTP, Maps and payments are UI-only.

Run from this directory:

```powershell
flutter pub get
flutter run -d chrome
```

Available local targets are Chrome, Edge, and Windows. Android requires Android Studio and the Android SDK; iOS builds require macOS/Xcode.

Planned feature modules:

- `auth`: register, login, forgot-password placeholder
- `catalog`: categories, search, product detail
- `location`: address and latitude/longitude selection
- `cart`: store-aware cart and quantity changes
- `checkout`: nearest store, inventory recheck, payment placeholder
- `orders`: history, detail, status timeline
- `profile`: account and logout

The app currently uses local seed data for the first UI slice. The next increment should add Riverpod for state, Dio for REST, go_router for navigation, and freezed/json_serializable for DTOs. Keep DTOs separate from widgets and preserve the API envelope shape documented in the root README.
