import 'package:dio/dio.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/repositories/auth_repository.dart';
import 'package:quickcart_customer/data/session_store.dart';

import 'fake_adapter.dart';

/// The body the API returns from login, register and refresh.
Map<String, Object?> sessionBody({String access = 'access-1', String refresh = 'refresh-1', List<String> roles = const ['Customer']}) => {
      'success': true,
      'data': {
        'accessToken': access,
        'refreshToken': refresh,
        'expiresIn': 900,
        'tokenType': 'Bearer',
        'user': {'id': 'u1', 'displayName': 'Asha Patil', 'email': 'asha@example.test', 'organizationId': 'o1', 'roles': roles, 'permissions': <String>[]},
      },
    };

Map<String, Object?> profileBody({String name = 'Asha Patil', String? phone}) => {
      'success': true,
      'data': {'id': 'u1', 'fullName': name, 'email': 'asha@example.test', 'phoneNumber': phone},
    };

Map<String, Object?> failureBody(String message, [List<String> errors = const []]) => {'success': false, 'message': message, 'errors': errors};

/// An [AuthRepository] wired to an [ApiClient] the same way the app's providers do, over a [FakeAdapter].
class AuthHarness {
  AuthHarness({String? storedToken}) : store = MemorySessionStore(storedToken) {
    final dio = Dio()..httpClientAdapter = adapter;
    api = ApiClient(
      config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'),
      dio: dio,
      tokenProvider: () => repo.accessToken(),
      onUnauthorized: () => repo.refreshSession(),
    );
    repo = AuthRepository(api, store, onSessionEnded: () => sessionEnded++);
  }

  final adapter = FakeAdapter();
  final MemorySessionStore store;
  late final ApiClient api;
  late final AuthRepository repo;
  int sessionEnded = 0;

  List<String> get paths => adapter.requests.map((r) => '${r.method} ${r.path}').toList();
  String? authorizationOf(int request) => adapter.requests[request].headers['Authorization'] as String?;
}
