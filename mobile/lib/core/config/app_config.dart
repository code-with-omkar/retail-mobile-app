import 'package:flutter/foundation.dart';

enum AppEnv { dev, staging, prod }

/// Build-time configuration. Values come from `--dart-define` / `--dart-define-from-file`
/// (see `config/*.json`), never from source code, so no URLs or keys are hardcoded in widgets.
///
///   flutter run --dart-define-from-file=config/dev.json
class AppConfig {
  const AppConfig({
    required this.env,
    required this.apiBaseUrl,
    this.useSeedData = false,
    this.defaultLatitude = _bandraLatitude,
    this.defaultLongitude = _bandraLongitude,
    this.connectTimeout = const Duration(seconds: 10),
    this.receiveTimeout = const Duration(seconds: 20),
  });

  final AppEnv env;
  final String apiBaseUrl;
  final Duration connectTimeout, receiveTimeout;

  /// Offline development: serve the catalog from local seed data instead of the API.
  final bool useSeedData;

  /// INTERIM (until phase P4 brings real addresses and device location): where the app looks for the nearest store.
  /// Defaults to Bandra West, Mumbai. Override with --dart-define=DEFAULT_LAT / DEFAULT_LNG.
  final double defaultLatitude, defaultLongitude;
  static const _bandraLatitude = 19.0596, _bandraLongitude = 72.8295;

  bool get isProd => env == AppEnv.prod;

  /// Android emulators reach the host machine at 10.0.2.2; other targets use localhost.
  static const _emulatorHost = 'http://10.0.2.2:5067';
  static const _localHost = 'http://localhost:5067';

  /// Reads the compile-time defines of the running build.
  static AppConfig fromEnvironment() => resolve(
        envName: const String.fromEnvironment('APP_ENV', defaultValue: 'dev'),
        baseUrl: const String.fromEnvironment('API_BASE_URL'),
        androidDevice: !kIsWeb && defaultTargetPlatform == TargetPlatform.android,
        useSeedData: const bool.fromEnvironment('USE_SEED_DATA'),
        defaultLatitude: double.tryParse(const String.fromEnvironment('DEFAULT_LAT')) ?? _bandraLatitude,
        defaultLongitude: double.tryParse(const String.fromEnvironment('DEFAULT_LNG')) ?? _bandraLongitude,
      );

  /// Pure resolution logic, separated so it can be unit tested.
  static AppConfig resolve({required String envName, required String baseUrl, required bool androidDevice, bool useSeedData = false, double defaultLatitude = _bandraLatitude, double defaultLongitude = _bandraLongitude}) {
    final env = AppEnv.values.firstWhere((e) => e.name == envName, orElse: () => throw ArgumentError.value(envName, 'APP_ENV', 'must be dev, staging or prod'));
    var url = baseUrl.trim();
    if (url.isEmpty) {
      if (env != AppEnv.dev) throw StateError('API_BASE_URL is required for the ${env.name} environment');
      url = androidDevice ? _emulatorHost : _localHost;
    }
    final uri = Uri.tryParse(url);
    if (uri == null || !uri.hasScheme || uri.host.isEmpty) throw ArgumentError.value(url, 'API_BASE_URL', 'must be an absolute http(s) URL');
    if (env == AppEnv.prod && uri.scheme != 'https') throw StateError('The prod environment requires an https API_BASE_URL');
    return AppConfig(env: env, apiBaseUrl: url.endsWith('/') ? url.substring(0, url.length - 1) : url, useSeedData: useSeedData, defaultLatitude: defaultLatitude, defaultLongitude: defaultLongitude);
  }
}
