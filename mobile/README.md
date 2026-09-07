# QuickCart Customer Mobile App

Flutter customer app scaffolded with Flutter 3.47.2. The first usable slice includes a branded home screen, delivery location header, search, category rail, product cards, responsive cart quantity controls, and bottom navigation.

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
