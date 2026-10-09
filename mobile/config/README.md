# Build configuration

The app reads its environment at build time (no URLs or keys in source code).

| File | Use |
| --- | --- |
| `dev.json` | Android emulator -> API on the host machine (`10.0.2.2:5067`). Committed; contains no secrets. |
| `staging.example.json`, `prod.example.json` | Templates. Copy to `staging.local.json` / `prod.local.json` and set the real URL. `*.local.json` is git-ignored. |

```powershell
# Android emulator
flutter run --dart-define-from-file=config/dev.json

# Physical device on the same Wi-Fi: copy dev.json to dev.local.json and use your PC's LAN address
flutter run --dart-define-from-file=config/dev.local.json

# Release build against staging
flutter build appbundle --release --dart-define-from-file=config/staging.local.json
```

Rules enforced in `lib/core/config/app_config.dart`: `staging` and `prod` need `API_BASE_URL`; `prod` must be `https`.
Plain `http` works only in debug builds (see `android/app/src/debug/AndroidManifest.xml`).
Do not put secrets (API keys, signing keys) in these files.
