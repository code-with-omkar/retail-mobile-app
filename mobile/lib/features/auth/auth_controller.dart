import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/dto/auth_dto.dart';
import '../../data/providers.dart';

enum AuthStatus {
  /// Checking the stored session at app start.
  restoring,
  signedOut,
  signedIn,
}

class AuthState {
  const AuthState(this.status, [this.user]);
  final AuthStatus status;
  final CustomerProfile? user;

  bool get isSignedIn => status == AuthStatus.signedIn;
}

/// Holds who is signed in. The work (API calls, token storage) is in `AuthRepository`.
class AuthNotifier extends Notifier<AuthState> {
  @override
  AuthState build() {
    // Not awaited: the app shows as a guest for a moment, then switches to the restored session.
    Future.microtask(_restore);
    return const AuthState(AuthStatus.restoring);
  }

  Future<void> _restore() async {
    if (!ref.mounted) return; // the container was disposed before the check started (tests)

    CustomerProfile? user;
    try {
      user = await ref.read(authRepositoryProvider).restore();
    } catch (_) {
      user = null;
    }
    if (!ref.mounted) return;
    if (state.status == AuthStatus.restoring) state = user == null ? const AuthState(AuthStatus.signedOut) : AuthState(AuthStatus.signedIn, user);
  }

  Future<void> login(String email, String password) async => _set(await ref.read(authRepositoryProvider).login(email: email, password: password));

  Future<void> register({required String fullName, required String email, required String password, String? phoneNumber}) async =>
      _set(await ref.read(authRepositoryProvider).register(fullName: fullName, email: email, password: password, phoneNumber: phoneNumber));

  /// A profile edit made on this device.
  void profileChanged(CustomerProfile user) => state = AuthState(AuthStatus.signedIn, user);

  Future<void> signOut() async {
    // Signed out on screen at once; revoking on the server may take a moment or fail offline.
    state = const AuthState(AuthStatus.signedOut);
    await ref.read(authRepositoryProvider).logout();
  }

  /// The server rejected the stored session.
  void sessionEnded() => state = const AuthState(AuthStatus.signedOut);

  void _set(CustomerProfile user) => state = AuthState(AuthStatus.signedIn, user);
}

final authProvider = NotifierProvider<AuthNotifier, AuthState>(AuthNotifier.new);

/// The first name for greetings, or null for a guest.
String? firstNameOf(AuthState auth) {
  final name = auth.user?.fullName.trim();
  if (name == null || name.isEmpty) return null;
  return name.split(RegExp(r'\s+')).first;
}
