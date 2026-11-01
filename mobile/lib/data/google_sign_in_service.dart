import 'package:google_sign_in/google_sign_in.dart';

/// Asks the person to pick a Google account and hands back Google's ID token, which the server checks before it signs anyone in.
abstract class GoogleSignInService {
  /// False when this build has no Google client id, so the screens do not offer a button that cannot work.
  bool get isConfigured;

  /// The ID token, or null when the person closed the account picker.
  Future<String?> idToken();
}

class PluginGoogleSignInService implements GoogleSignInService {
  PluginGoogleSignInService(this._serverClientId);
  final String _serverClientId;
  Future<void>? _ready;

  @override
  bool get isConfigured => _serverClientId.isNotEmpty;

  @override
  Future<String?> idToken() async {
    _ready ??= GoogleSignIn.instance.initialize(serverClientId: _serverClientId);
    await _ready;
    try {
      final account = await GoogleSignIn.instance.authenticate();
      return account.authentication.idToken;
    } on GoogleSignInException catch (e) {
      if (e.code == GoogleSignInExceptionCode.canceled || e.code == GoogleSignInExceptionCode.interrupted) return null;
      rethrow;
    }
  }
}

class DisabledGoogleSignInService implements GoogleSignInService {
  const DisabledGoogleSignInService();
  @override
  bool get isConfigured => false;
  @override
  Future<String?> idToken() async => null;
}
