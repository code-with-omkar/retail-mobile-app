import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/auth_fakes.dart';
import '../support/fake_adapter.dart';

/// Uses the app's real provider wiring (no repository override), which is where a dependency cycle between the API
/// client and the auth repository once slipped through the widget tests.
void main() {
  Future<(ProviderContainer, FakeAdapter, MemorySessionStore)> setUpContainer({String? stored}) async {
    SharedPreferences.setMockInitialValues({});
    final adapter = FakeAdapter();
    final store = MemorySessionStore(stored);
    final container = ProviderContainer(retry: noAutomaticRetry, overrides: [
      sharedPrefsProvider.overrideWithValue(await SharedPreferences.getInstance()),
      appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test')),
      httpClientAdapterProvider.overrideWithValue(adapter),
      sessionStoreProvider.overrideWithValue(store),
    ]);
    addTearDown(container.dispose);
    return (container, adapter, store);
  }

  test('registering signs in and later requests carry the new token', () async {
    final (container, adapter, store) = await setUpContainer();
    container.read(authProvider); // starts the (empty) session restore
    await Future<void>.delayed(Duration.zero);
    expect(container.read(authProvider).status, AuthStatus.signedOut);

    adapter.replyJson(sessionBody());
    await container.read(authProvider.notifier).register(fullName: 'Asha Patil', email: 'asha@example.test', password: 'Sunrise-42x');
    expect(container.read(authProvider).user?.fullName, 'Asha Patil');
    expect(store.token, 'refresh-1');

    adapter.replyJson(profileBody());
    await container.read(authRepositoryProvider).profile();
    expect(adapter.requests.last.headers['Authorization'], 'Bearer access-1');
  });

  test('a 401 refreshes through the real wiring and the original request is repeated', () async {
    final (container, adapter, store) = await setUpContainer();
    adapter.replyJson(sessionBody());
    await container.read(authProvider.notifier).login('asha@example.test', 'Sunrise-42x');

    adapter.replyJson(failureBody('expired'), status: 401);
    adapter.replyJson(sessionBody(access: 'access-2', refresh: 'refresh-2'));
    adapter.replyJson(profileBody());
    await container.read(authRepositoryProvider).profile();
    expect(adapter.requests.last.headers['Authorization'], 'Bearer access-2');
    expect(store.token, 'refresh-2');
  });

  test('a stored session is restored at start', () async {
    final (container, adapter, _) = await setUpContainer(stored: 'refresh-1');
    adapter.replyJson(sessionBody(access: 'access-2', refresh: 'refresh-2'));
    adapter.replyJson(profileBody(name: 'Asha Kulkarni'));
    container.listen(authProvider, (_, _) {});
    await Future<void>.delayed(const Duration(milliseconds: 50));
    expect(container.read(authProvider).user?.fullName, 'Asha Kulkarni');
  });

  test('a refresh the server rejects signs the app out', () async {
    final (container, adapter, store) = await setUpContainer();
    adapter.replyJson(sessionBody());
    await container.read(authProvider.notifier).login('asha@example.test', 'Sunrise-42x');

    adapter.replyJson(failureBody('expired'), status: 401);
    adapter.replyJson(failureBody('revoked'), status: 401);
    await expectLater(container.read(authRepositoryProvider).profile(), throwsA(anything));
    expect(container.read(authProvider).status, AuthStatus.signedOut);
    expect(store.token, isNull);
  });
}
