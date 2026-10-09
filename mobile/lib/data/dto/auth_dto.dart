/// Hand-written DTOs mirroring `QuickCommerce.Application/DTOs/AuthenticationDtos.cs` and `AccountDtos.cs`.
library;

import 'catalog_dto.dart';

/// The signed-in customer.
class CustomerProfile {
  const CustomerProfile({required this.id, required this.fullName, required this.email, this.phoneNumber});
  final String id, fullName, email;
  final String? phoneNumber;

  /// `GET/PUT /api/customer/profile`.
  factory CustomerProfile.fromJson(Map<String, dynamic> j) => CustomerProfile(
        id: _string(j, 'id'),
        fullName: _string(j, 'fullName'),
        email: _string(j, 'email'),
        phoneNumber: j['phoneNumber'] as String?,
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
