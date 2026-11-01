import 'dart:async';

import '../api/api_client.dart';
import '../api/api_exception.dart';
import '../dto/auth_dto.dart';
import '../dto/catalog_dto.dart';
import '../session_store.dart';

/// Customer accounts: sign up, sign in, password reset and the session itself.
///
/// The access token lives in memory only; the refresh token is kept in [SessionStore] (Android Keystore).
/// The API rotates refresh tokens, so every successful refresh stores the new one before anything else.
class AuthRepository {
  AuthRepository(this._api, this._store, {this.onSessionEnded});

  final ApiClient _api;
  final SessionStore _store;

  /// Called when the server no longer accepts the stored session (refresh rejected), so the UI can sign out.
  final void Function()? onSessionEnded;

  String? _accessToken;
  Future<bool>? _refreshing;

  /// Used by `ApiClient` for the `Authorization` header.
  Future<String?> accessToken() async => _accessToken;

  static const _customerRole = 'Customer';

  Future<CustomerProfile> login({required String email, required String password}) async {
    final session = await _api.post('/api/auth/login', body: {'username': email.trim(), 'password': password}, parse: (d) => AuthSessionDto.fromJson(asObject(d)));
    return _begin(session);
  }

  /// Texts a code to the number (10 digits). The answer is the same whether or not the number has an account.
  Future<OtpSent> requestOtp(String phone) => _api.post('/api/auth/otp/request', body: {'phone': phone}, parse: (d) => OtpSent.fromJson(asObject(d)));

  /// Signs in with the code: to the account that owns the verified number, or to a new one.
  Future<CustomerProfile> verifyOtp({required String phone, required String code}) async {
    final session = await _api.post('/api/auth/otp/verify', body: {'phone': phone, 'code': code}, parse: (d) => AuthSessionDto.fromJson(asObject(d)));
    return _beginAndFill(session);
  }

  /// Signs in with Google's ID token, which the server checks itself.
  Future<CustomerProfile> googleSignIn(String idToken) async {
    final session = await _api.post('/api/auth/google', body: {'idToken': idToken}, parse: (d) => AuthSessionDto.fromJson(asObject(d)));
    return _beginAndFill(session);
  }

  /// For a signed-in customer: texts a code to a number they want to verify for their own account.
  Future<OtpSent> requestPhoneVerification(String phone) => _api.post('/api/account/phone/otp/request', body: {'phone': phone}, parse: (d) => OtpSent.fromJson(asObject(d)));

  Future<void> verifyPhone({required String phone, required String code}) => _api.post('/api/account/phone/otp/verify', body: {'phone': phone, 'code': code}, parse: (_) {});

  Future<CustomerProfile> register({required String fullName, required String email, required String password, String? phoneNumber}) async {
    final session = await _api.post(
      '/api/auth/register',
      body: {'fullName': fullName.trim(), 'email': email.trim(), 'password': password, if (phoneNumber != null && phoneNumber.trim().isNotEmpty) 'phoneNumber': phoneNumber.trim()},
      parse: (d) => AuthSessionDto.fromJson(asObject(d)),
    );
    return _begin(session);
  }

  /// Always succeeds for a well-formed email, whether or not an account exists.
  Future<void> forgotPassword(String email) => _api.post('/api/auth/forgot-password', body: {'email': email.trim()}, parse: (_) {});

  Future<void> resetPassword({required String email, required String code, required String newPassword}) =>
      _api.post('/api/auth/reset-password', body: {'email': email.trim(), 'code': code.trim(), 'newPassword': newPassword}, parse: (_) {});

  Future<void> changePassword({required String currentPassword, required String newPassword}) async {
    await _api.post(
      '/api/auth/change-password',
      body: {'currentPassword': currentPassword, 'newPassword': newPassword, 'refreshToken': await _store.readRefreshToken()},
      parse: (_) {},
    );
  }

  /// Deletes the account for good (the server erases the personal data and revokes every session), then forgets the session here.
  Future<void> deleteAccount({String? password}) async {
    await _api.post('/api/customer/profile/delete', body: {'confirmation': 'DELETE', if (password != null && password.isNotEmpty) 'password': password}, parse: (_) {});
    _accessToken = null;
    await _store.clear();
  }

  Future<CustomerProfile> profile() => _api.get('/api/customer/profile', parse: (d) => CustomerProfile.fromJson(asObject(d)));

  Future<CustomerProfile> updateProfile({required String fullName, String? phoneNumber}) =>
      _api.put('/api/customer/profile', body: {'fullName': fullName.trim(), 'phoneNumber': phoneNumber?.trim()}, parse: (d) => CustomerProfile.fromJson(asObject(d)));

  /// App start: uses the stored refresh token to get a fresh session. Null means signed out.
  /// A network failure keeps the stored token, so a customer who opens the app offline is not logged out for good.
  Future<CustomerProfile?> restore() async {
    if (await _store.readRefreshToken() == null) return null;
    if (!await refreshSession()) return null;
    try {
      return await profile();
    } on UnauthorizedException {
      await _endSession();
      return null;
    } on ApiException {
      return null;
    }
  }

  /// Renews the access token with the stored refresh token. Several callers at once share one request, because a
  /// rotated refresh token can only be used once. Returns false when no new token was obtained.
  Future<bool> refreshSession() => _refreshing ??= _refresh().whenComplete(() => _refreshing = null);

  Future<bool> _refresh() async {
    final stored = await _store.readRefreshToken();
    if (stored == null) return false;
    try {
      final session = await _api.post('/api/auth/refresh', body: {'refreshToken': stored}, parse: (d) => AuthSessionDto.fromJson(asObject(d)));
      await _store.writeRefreshToken(session.refreshToken);
      _accessToken = session.accessToken;
      return true;
    } on UnauthorizedException {
      await _endSession();
      return false;
    } on ApiException {
      return false;
    }
  }

  /// Revokes the session on the server (best effort) and forgets it on this device either way.
  Future<void> logout() async {
    final stored = await _store.readRefreshToken();
    try {
      if (stored != null && _accessToken != null) {
        await _api.post('/api/auth/logout', body: {'refreshToken': stored}, parse: (_) {});
      }
    } on ApiException {
      // Offline or already expired: the local sign-out below is what matters to this device.
    } finally {
      _accessToken = null;
      await _store.clear();
    }
  }

  /// Starts the session, then reads the full profile (the session itself carries no phone number). If that read fails the session still stands.
  Future<CustomerProfile> _beginAndFill(AuthSessionDto session) async {
    final basic = await _begin(session);
    try {
      return await profile();
    } on ApiException {
      return basic;
    }
  }

  Future<CustomerProfile> _begin(AuthSessionDto session) async {
    if (!session.roles.contains(_customerRole)) {
      // Staff accounts share the login route. They have no place in the customer app, so the session is revoked again.
      _accessToken = session.accessToken;
      try {
        await _api.post('/api/auth/logout', body: {'refreshToken': session.refreshToken}, parse: (_) {});
      } on ApiException {
        // Best effort.
      } finally {
        _accessToken = null;
      }
      throw const ForbiddenException('Not a customer account', statusCode: 403);
    }
    await _store.writeRefreshToken(session.refreshToken);
    _accessToken = session.accessToken;
    return session.profile;
  }

  Future<void> _endSession() async {
    _accessToken = null;
    await _store.clear();
    onSessionEnded?.call();
  }
}
