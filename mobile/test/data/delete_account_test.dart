import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/auth_fakes.dart';

Map<String, Object?> _profile({bool hasPassword = true}) => {
      'success': true,
      'data': {'id': 'u1', 'fullName': 'Asha Patil', 'email': hasPassword ? 'asha@example.test' : '', 'phoneNumber': '+919876543210', 'phoneVerified': true, 'hasPassword': hasPassword},
    };

Future<AuthHarness> _open(WidgetTester tester, {bool hasPassword = true}) async {
  final auth = AuthHarness(storedToken: 'stored')
    ..adapter.replyJson(sessionBody())
    ..adapter.replyJson(_profile(hasPassword: hasPassword));
  SharedPreferences.setMockInitialValues(const {});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 900 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    sessionStoreProvider.overrideWithValue(MemorySessionStore()),
    authRepositoryProvider.overrideWithValue(auth.repo),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid', useSeedData: true)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  return auth;
}

Future<void> _openDeleteScreen(WidgetTester tester) async {
  await tester.tap(find.byTooltip('Profile'));
  await tester.pumpAndSettle();
  await tester.scrollUntilVisible(find.text('Delete account'), 200, scrollable: find.byType(Scrollable).first);
  await tester.pumpAndSettle();
  await tester.tap(find.text('Delete account'));
  await tester.pumpAndSettle();
}

bool _signedIn(WidgetTester tester) => ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))).read(authProvider).isSignedIn;

void main() {
  test('the repository posts the confirmation, then forgets the session on this phone', () async {
    final auth = AuthHarness()
      ..adapter.replyJson(sessionBody(refresh: 'r-1'))
      ..adapter.replyJson({'success': true});
    await auth.repo.login(email: 'asha@example.test', password: 'Sunrise-42x');

    await auth.repo.deleteAccount(password: 'Sunrise-42x');

    expect(auth.paths.last, 'POST /api/customer/profile/delete');
    expect(auth.adapter.requests.last.data, {'confirmation': 'DELETE', 'password': 'Sunrise-42x'});
    expect(await auth.store.readRefreshToken(), isNull);
    expect(await auth.repo.accessToken(), isNull);
  });

  test('a refused deletion keeps the session', () async {
    final auth = AuthHarness()
      ..adapter.replyJson(sessionBody(refresh: 'r-1'))
      ..adapter.replyJson(failureBody('Finish or cancel your open orders first.'), status: 409);
    await auth.repo.login(email: 'asha@example.test', password: 'Sunrise-42x');

    await expectLater(auth.repo.deleteAccount(password: 'Sunrise-42x'), throwsA(isA<ConflictException>()));
    expect(await auth.store.readRefreshToken(), 'r-1');
  });

  test('an account without a password sends no password', () async {
    final auth = AuthHarness()
      ..adapter.replyJson(sessionBody())
      ..adapter.replyJson({'success': true});
    await auth.repo.login(email: 'a@b.test', password: 'x');

    await auth.repo.deleteAccount();

    expect(auth.adapter.requests.last.data, {'confirmation': 'DELETE'});
  });

  testWidgets('the profile offers Delete account only to someone signed in', (tester) async {
    await _open(tester);
    await tester.tap(find.byTooltip('Profile'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Delete account'), 200, scrollable: find.byType(Scrollable).first);
    expect(find.text('Delete account'), findsOneWidget);
  });

  testWidgets('deleting needs the password and the word DELETE, then signs out and goes home', (tester) async {
    final auth = await _open(tester);
    auth.adapter.replyJson({'success': true});
    await _openDeleteScreen(tester);

    await tester.tap(find.text('Delete my account'));
    await tester.pumpAndSettle();
    expect(auth.paths.last, isNot('POST /api/customer/profile/delete'));

    await tester.enterText(find.byType(TextFormField).at(0), 'Sunrise-42x');
    await tester.enterText(find.byType(TextFormField).at(1), 'nope');
    await tester.tap(find.text('Delete my account'));
    await tester.pumpAndSettle();
    expect(auth.paths.last, isNot('POST /api/customer/profile/delete'));

    await tester.enterText(find.byType(TextFormField).at(1), 'delete');
    await tester.tap(find.text('Delete my account'));
    await tester.pumpAndSettle();

    expect(auth.adapter.requests.last.path, '/api/customer/profile/delete');
    expect(auth.adapter.requests.last.data, {'confirmation': 'DELETE', 'password': 'Sunrise-42x'});
    expect(_signedIn(tester), isFalse);
    expect(find.text('Delete my account'), findsNothing);
  });

  testWidgets('a wrong password is explained and the account stays', (tester) async {
    final auth = await _open(tester);
    auth.adapter.replyJson(failureBody('Your password is incorrect.', ['Your password is incorrect.']), status: 400);
    await _openDeleteScreen(tester);
    await tester.enterText(find.byType(TextFormField).at(0), 'wrong-one');
    await tester.enterText(find.byType(TextFormField).at(1), 'DELETE');

    await tester.tap(find.text('Delete my account'));
    await tester.pumpAndSettle();

    expect(find.text('Your password is not correct.'), findsOneWidget);
    expect(_signedIn(tester), isTrue);
  });

  testWidgets('an open order is explained and the account stays', (tester) async {
    final auth = await _open(tester);
    auth.adapter.replyJson(failureBody('Finish or cancel your open orders first.'), status: 409);
    await _openDeleteScreen(tester);
    await tester.enterText(find.byType(TextFormField).at(0), 'Sunrise-42x');
    await tester.enterText(find.byType(TextFormField).at(1), 'DELETE');

    await tester.tap(find.text('Delete my account'));
    await tester.pumpAndSettle();

    expect(find.text('Finish or cancel your open orders first.'), findsOneWidget);
    expect(_signedIn(tester), isTrue);
  });

  testWidgets('an account without a password is not asked for one', (tester) async {
    final auth = await _open(tester, hasPassword: false);
    auth.adapter.replyJson({'success': true});
    await _openDeleteScreen(tester);

    expect(find.byType(TextFormField), findsOneWidget);
    await tester.enterText(find.byType(TextFormField), 'DELETE');
    await tester.tap(find.text('Delete my account'));
    await tester.pumpAndSettle();

    expect(auth.adapter.requests.last.data, {'confirmation': 'DELETE'});
    expect(_signedIn(tester), isFalse);
  });
}
