import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/auth_fakes.dart';

Future<void> _pump(WidgetTester tester, {Map<String, Object> prefs = const {}, AuthHarness? auth, List<Override> overrides = const [], Size size = const Size(390, 844)}) async {
  auth ??= AuthHarness();
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = size * 3;
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [sharedPrefsProvider.overrideWithValue(sp), sessionStoreProvider.overrideWithValue(MemorySessionStore()), authRepositoryProvider.overrideWithValue(auth.repo), ...overrides, appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid', useSeedData: true))], child: const QuickCartApp()));
  await tester.pumpAndSettle();
}

/// The stores row pushes the popular items below the first screen, so scroll to them like a customer would.
Future<void> _seePopular(WidgetTester tester) async {
  await tester.scrollUntilVisible(find.text('Popular near you'), 200, scrollable: find.byType(Scrollable).first);
  expect(find.text('Popular near you'), findsOneWidget);
  // Bring the products themselves into view, not just their heading.
  await tester.drag(find.byType(CustomScrollView).first, const Offset(0, -300));
  await tester.pumpAndSettle();
}

Future<void> _fillLogin(WidgetTester tester, {String password = 'Sunrise-42x'}) async {
  await tester.enterText(find.byType(TextFormField).at(0), 'asha@example.test');
  await tester.enterText(find.byType(TextFormField).at(1), password);
  await tester.pump();
  await tester.tap(find.widgetWithText(GestureDetector, 'Log in').first);
  await tester.pumpAndSettle();
}

Future<void> _openProfileSignIn(WidgetTester tester) async {
  await tester.tap(find.byTooltip('Profile'));
  await tester.pumpAndSettle();
  await tester.scrollUntilVisible(find.text('Sign in / Register'), 200, scrollable: find.byType(Scrollable).first);
  await tester.pumpAndSettle();
  await tester.tap(find.text('Sign in / Register'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('home shows the reference layout and search filters products', (tester) async {
    await _pump(tester);
    expect(find.text('DELIVER TO'), findsOneWidget);
    expect(find.text('Shop by category'), findsOneWidget);
    await _seePopular(tester);
    expect(find.textContaining('Tomato', findRichText: true), findsOneWidget);

    // Back to the top, where the search field is.
    await tester.drag(find.byType(CustomScrollView).first, const Offset(0, 1500));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).first, 'onion');
    await tester.pump(const Duration(milliseconds: 400)); // search is debounced
    await tester.pumpAndSettle();
    expect(find.textContaining('Onion', findRichText: true), findsOneWidget);
    expect(find.textContaining('Tomato', findRichText: true), findsNothing);
  });

  testWidgets('add to cart, sign in at checkout and place order', (tester) async {
    final auth = AuthHarness()..adapter.replyJson(sessionBody());
    await _pump(tester, auth: auth);
    await _seePopular(tester);
    await tester.ensureVisible(find.bySemanticsLabel('Add Tomato'));
    await tester.pumpAndSettle();
    await tester.tap(find.bySemanticsLabel('Add Tomato'));
    await tester.pumpAndSettle();
    expect(find.text('1 item'), findsOneWidget);

    await tester.tap(find.byTooltip('Cart'));
    await tester.pumpAndSettle();
    expect(find.text('Your cart'), findsOneWidget);

    await tester.tap(find.textContaining('Checkout'));
    await tester.pumpAndSettle();
    // A guest is asked to sign in first and lands back on checkout afterwards.
    expect(find.text('Your trusted neighbourhood shop, now digital'), findsOneWidget);
    await tester.tap(find.text('Log in'));
    await tester.pumpAndSettle();
    await _fillLogin(tester);
    await tester.tap(find.textContaining('Place order'));
    await tester.pumpAndSettle();
    expect(find.text('Order placed!'), findsOneWidget);

    // Track order, then Back: lands on the orders list rather than doing nothing.
    await tester.tap(find.text('Track order'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Order #'), findsWidgets);
    await tester.tap(find.byTooltip('Back'));
    await tester.pumpAndSettle();
    expect(find.text('Your orders'), findsOneWidget);
  });

  testWidgets('product detail weight selector changes price and adds that pack', (tester) async {
    await _pump(tester);
    await _seePopular(tester);
    await tester.ensureVisible(find.textContaining('Tomato', findRichText: true));
    await tester.pumpAndSettle();
    await tester.tap(find.textContaining('Tomato', findRichText: true));
    await tester.pumpAndSettle();
    expect(find.text('₹28'), findsWidgets);

    await tester.tap(find.text('500 g'));
    await tester.pumpAndSettle();
    expect(find.text('₹15'), findsWidgets);

    await tester.tap(find.textContaining('Add · ₹15'));
    await tester.pumpAndSettle();
    expect(find.text('Added to cart'), findsOneWidget);
  });

  testWidgets('categories tab opens a category', (tester) async {
    await _pump(tester);
    await tester.tap(find.byTooltip('Categories'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dairy'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Farm Milk', findRichText: true), findsOneWidget);
    expect(find.textContaining('Tomato', findRichText: true), findsNothing);
  });

  testWidgets('email login from the profile signs in and greets by name', (tester) async {
    final auth = AuthHarness()..adapter.replyJson(sessionBody());
    await _pump(tester, auth: auth);
    await _openProfileSignIn(tester);
    await tester.tap(find.text('Log in'));
    await tester.pumpAndSettle();
    await _fillLogin(tester);
    expect(find.textContaining('Asha'), findsOneWidget);
    await _seePopular(tester);
    expect(auth.store.token, 'refresh-1');
  });

  testWidgets('a wrong password shows a message and stays on the login screen', (tester) async {
    final auth = AuthHarness()..adapter.replyJson(failureBody('Invalid username or password.'), status: 401);
    await _pump(tester, auth: auth);
    await _openProfileSignIn(tester);
    await tester.tap(find.text('Log in'));
    await tester.pumpAndSettle();
    await _fillLogin(tester, password: 'nope-nope');
    expect(find.text('Wrong email or password.'), findsOneWidget);
    expect(find.text('Welcome back'), findsOneWidget);
  });

  testWidgets('forgot password asks for the emailed code, then returns to log in', (tester) async {
    final auth = AuthHarness()
      ..adapter.replyJson({'success': true, 'data': null})
      ..adapter.replyJson({'success': true, 'data': null});
    await _pump(tester, auth: auth);
    await _openProfileSignIn(tester);
    await tester.tap(find.text('Log in'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Forgot password?'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), 'asha@example.test');
    await tester.tap(find.text('Send reset code'));
    await tester.pumpAndSettle();
    expect(find.text('Enter your code'), findsOneWidget);

    await tester.enterText(find.byType(TextFormField).at(0), 'abcd-2345');
    await tester.enterText(find.byType(TextFormField).at(1), 'Harvest-55z');
    await tester.tap(find.text('Change password'));
    await tester.pumpAndSettle();
    expect(find.text('Welcome back'), findsOneWidget);
    expect(auth.adapter.requests.last.data, {'email': 'asha@example.test', 'code': 'ABCD2345', 'newPassword': 'Harvest-55z'});
  });

  testWidgets('a guest sees a sign-in prompt on the orders tab', (tester) async {
    await _pump(tester);
    await tester.tap(find.byTooltip('Orders'));
    await tester.pumpAndSettle();
    expect(find.text('Sign in to see your orders'), findsOneWidget);
  });

  testWidgets('registration is reachable from welcome', (tester) async {
    await _pump(tester);
    await _openProfileSignIn(tester);
    await tester.ensureVisible(find.text('Create account'));
    await tester.tap(find.text('Create account'));
    await tester.pumpAndSettle();
    expect(find.text('Create your account'), findsOneWidget);
  });

  testWidgets('the delivery addresses screen opens from the home header', (tester) async {
    await _pump(tester);
    await tester.tap(find.text('DELIVER TO'));
    await tester.pumpAndSettle();
    expect(find.text('Delivery addresses'), findsOneWidget);
    expect(find.text('Use my current location'), findsOneWidget);
    expect(find.text('Enter an address'), findsOneWidget);
  });

  testWidgets('language toggle switches the app to Marathi and remembers it', (tester) async {
    await _pump(tester);
    await tester.tap(find.byTooltip('Profile'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('मराठी'));
    await tester.pumpAndSettle();
    expect(find.text('प्रोफाइल'), findsWidgets);
    expect((await SharedPreferences.getInstance()).getString('language'), 'mr');
  });

  testWidgets('dark theme renders from saved preference', (tester) async {
    await _pump(tester, prefs: {'theme_mode': 'dark'});
    await _seePopular(tester);
    final app = tester.widget<MaterialApp>(find.byType(MaterialApp));
    expect(app.themeMode, ThemeMode.dark);
  });


  testWidgets('a long store name never overflows the store bar, even on a small screen with large text', (tester) async {
    tester.platformDispatcher.textScaleFactorTestValue = 1.3;
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    await _pump(tester, size: const Size(320, 640), overrides: [
      selectedStoreProvider.overrideWith((ref) async => const NearestStore(id: 's1', name: 'Harbor Point Dark Store Extension Annexe', distanceKm: 4.7, estimatedMinutes: 25)),
    ]);
    expect(find.text('Harbor Point Dark Store Extension Annexe'), findsOneWidget);
    expect(find.text('Change'), findsNothing, reason: 'on a narrow screen the Change button is an icon, not a label');
    expect(tester.takeException(), isNull);
  });

  testWidgets('form labels stay visible above the field after typing', (tester) async {
    await _pump(tester);
    await _openProfileSignIn(tester);
    await tester.tap(find.text('Create account'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField).at(0), 'Omkar Jagtap');
    await tester.pump();
    expect(find.text('Full name'), findsOneWidget);
    expect(find.text('Omkar Jagtap'), findsOneWidget);
  });
}
