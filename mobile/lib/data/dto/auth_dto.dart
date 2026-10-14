/// Hand-written DTOs mirroring `QuickCommerce.Application/DTOs/AuthenticationDtos.cs` and `AccountDtos.cs`.
library;

import 'catalog_dto.dart';

/// The signed-in customer.
class CustomerProfile {
  const CustomerProfile({required this.id, required this.fullName, required this.email, this.phoneNumber, this.phoneVerified = false, this.hasPassword = true});
  final String id, fullName, email;
  final String? phoneNumber;

  /// True once the number was proved with a text-message code, so it can be used to sign in.
  final bool phoneVerified;

  /// False for an account made with a phone number or Google, which has no password to ask for.
  final bool hasPassword;

  /// `GET/PUT /api/customer/profile`.
  factory CustomerProfile.fromJson(Map<String, dynamic> j) => CustomerProfile(
        id: _string(j, 'id'),
        fullName: _string(j, 'fullName'),
        email: (j['email'] as String?) ?? '',
        phoneNumber: j['phoneNumber'] as String?,
        phoneVerified: j['phoneVerified'] == true,
        hasPassword: j['hasPassword'] != false,
      );
}

/// Answer to "send me a code": when to allow asking again, and how long the code works.
class OtpSent {
  const OtpSent({required this.resendAfterSeconds, required this.expiresInSeconds});
  final int resendAfterSeconds, expiresInSeconds;

  factory OtpSent.fromJson(Map<String, dynamic> j) => OtpSent(
        resendAfterSeconds: (j['resendAfterSeconds'] as num?)?.toInt() ?? 30,
        expiresInSeconds: (j['expiresInSeconds'] as num?)?.toInt() ?? 300,
      );
}

/// Response of login, register and refresh.
class AuthSessionDto {
  const AuthSessionDto({required this.accessToken, required this.refreshToken, required this.expiresIn, required this.userId, required this.displayName, required this.email, required this.roles});
  final String accessToken, refreshToken, userId, displayName, email;
  final int expiresIn;
  final List<String> roles;

  factory AuthSessionDto.fromJson(Map<String, dynamic> j) {
    final user = asObject(j['user']);
    final expires = j['expiresIn'];
    if (expires is! num) throw const FormatException('Field "expiresIn" expected a number');
    return AuthSessionDto(
      accessToken: _string(j, 'accessToken'),
      refreshToken: _string(j, 'refreshToken'),
      expiresIn: expires.toInt(),
      userId: _string(user, 'id'),
      displayName: _string(user, 'displayName'),
      email: (user['email'] as String?) ?? '',
      roles: [for (final r in (user['roles'] as List? ?? const [])) '$r'],
    );
  }

  CustomerProfile get profile => CustomerProfile(id: userId, fullName: displayName, email: email);
}

String _string(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is String && v.isNotEmpty) return v;
  throw FormatException('Field "$key" expected a non-empty string but was ${v.runtimeType}');
}
