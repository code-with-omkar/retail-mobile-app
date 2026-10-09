import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where the refresh token lives between app launches. The access token is never stored: it stays in memory.
abstract class SessionStore {
  Future<String?> readRefreshToken();
  Future<void> writeRefreshToken(String token);
  Future<void> clear();
}

/// Android Keystore backed storage. A failure to read (for example data restored from a backup onto another device,
/// which cannot be decrypted) is treated as "no session" and the unusable entry is removed. Tokens are never logged.
class SecureSessionStore implements SessionStore {
  const SecureSessionStore([this._storage = const FlutterSecureStorage()]);
  final FlutterSecureStorage _storage;
  static const _key = 'refresh_token';

  @override
  Future<String?> readRefreshToken() async {
    try {
      return await _storage.read(key: _key);
    } catch (e) {
      if (kDebugMode) debugPrint('[session] could not read stored session: ${e.runtimeType}');
      await clear();
      return null;
    }
  }

  @override
  Future<void> writeRefreshToken(String token) async {
    try {
      await _storage.write(key: _key, value: token);
    } catch (e) {
      // The session still works until the app closes; the user just has to sign in again next launch.
      if (kDebugMode) debugPrint('[session] could not store session: ${e.runtimeType}');
    }
  }

  @override
  Future<void> clear() async {
    try {
      await _storage.delete(key: _key);
    } catch (_) {}
  }
}

/// For tests and previews.
class MemorySessionStore implements SessionStore {
  MemorySessionStore([this.token]);
  String? token;

  @override
  Future<String?> readRefreshToken() async => token;
  @override
  Future<void> writeRefreshToken(String value) async => token = value;
  @override
  Future<void> clear() async => token = null;
}
