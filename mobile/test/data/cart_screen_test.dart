import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/location_fakes.dart';

class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;
  @override
  AuthState build() => start;
  void become(AuthState next) => state = next;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');

class _App {
  _App(this.container, this.cartRepo, this.auth);
  final ProviderContainer container;
  final FakeCartRepository cartRepo;
  final _Auth auth;
}

Future<_App> _open(WidgetTester tester, {bool signedIn = true, bool failPricing = false, Map<String, Object> prefs = const {}, void Function(FakeCartRepository cart)? server}) async {
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2000 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final cartRepo = FakeCartRepository()
    ..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28)
    ..know('apple:1kg', productId: 'apple', variantId: 'apple:1kg', name: 'Royal Apples', price: 149, stock: 2);
  cartRepo.failPricing = failPricing;
  server?.call(cartRepo);
  final auth = _Auth(signedIn ? const AuthState(AuthStatus.signedIn, _asha) : const AuthState(AuthStatus.signedOut));
  final addresses = FakeAddressRepository()..seed('Home', isDefault: true);
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => auth),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cartRepo),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  return _App(ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))), cartRepo, auth);
}

Future<void> _openCart(WidgetTester tester) async {
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/cart');
  await tester.pumpAndSettle();
}

void main() {
  group('the bill', () {
    testWidgets('a signed-in cart shows the item total, the fees and what to pay, from the server settings', (tester) async {
      final app = await _open(tester);
      app.container.read(cartProvider.notifier).add('tomato:1kg', 2);
      await tester.pumpAndSettle();

      await _openCart(tester);

      expect(find.text('Your cart'), findsOneWidget);
      expect(find.text('Item total'), findsOneWidget);
      expect(find.text('₹56'), findsOneWidget);
      expect(find.text('₹25'), findsOneWidget);
      expect(find.text('₹5'), findsOneWidget);
      expect(find.text('₹86'), findsWidgets);
      expect(find.text('Add ₹143 more for free delivery'), findsOneWidget);
    });

    testWidgets('delivery turns free once the threshold is reached', (tester) async {
      final app = await _open(tester);
      app.container.read(cartProvider.notifier).add('apple:1kg', 2);
      await tester.pumpAndSettle();

      await _openCart(tester);

      expect(find.text('FREE'), findsOneWidget);
      expect(find.text('You unlocked free delivery!'), findsOneWidget);
      expect(find.text('₹303'), findsWidgets);
    });

    testWidgets('a guest sees the same fees without signing in', (tester) async {
      final app = await _open(tester, signedIn: false, prefs: deviceLocationPrefs);
      app.container.read(cartProvider.notifier).add('tomato:1kg');
      await tester.pumpAndSettle();

      await _openCart(tester);

      expect(find.text('₹25'), findsOneWidget);
      expect(find.text('₹58'), findsWidgets);
      expect(app.cartRepo.calls.where((c) => c != 'pricing'), isEmpty, reason: 'only the fee settings are read for a guest');
    });

    testWidgets('if the fee settings cannot be read, no fees are shown rather than wrong ones', (tester) async {
      final app = await _open(tester, signedIn: false, failPricing: true, prefs: deviceLocationPrefs);
      app.container.read(cartProvider.notifier).add('tomato:1kg');
      await tester.pumpAndSettle();

      await _openCart(tester);

      expect(find.text('Item total'), findsOneWidget);
      expect(find.text('Delivery fee'), findsNothing);
      expect(find.textContaining('more for free delivery'), findsNothing);
    });
  });

  group('what the cart tells the customer', () {
    testWidgets('a change the server refused is explained and can be dismissed', (tester) async {
      final app = await _open(tester);
      app.cartRepo.failNext = const ConflictException('too many', statusCode: 409);
      app.container.read(cartProvider.notifier).add('tomato:1kg');
      await tester.pumpAndSettle();
      await _openCart(tester);

      expect(find.text('This changed while you were shopping. Please review and try again.'), findsWidgets, reason: 'on the cart and as a message');
      expect(find.text('Your cart is empty'), findsOneWidget, reason: 'the refused item is not in the cart');
      await tester.tap(find.byTooltip('Dismiss'));
      await tester.pumpAndSettle();

      expect(find.byTooltip('Dismiss'), findsNothing);
    });

    testWidgets('after signing in, what could not be added from the phone cart is listed', (tester) async {
      final app = await _open(tester, signedIn: false, prefs: deviceLocationPrefs);
      app.container.read(cartProvider.notifier).add('apple:1kg', 5);
      app.container.read(cartProvider.notifier).add('tomato:1kg');
      await tester.pumpAndSettle();

      app.auth.become(const AuthState(AuthStatus.signedIn, _asha));
      await tester.pumpAndSettle();
      await _openCart(tester);

      expect(find.text('Some items from your cart could not be added:'), findsWidgets);
      expect(find.text('Only 2 of Royal Apples (1 kg) could be added'), findsOneWidget);
    });

    testWidgets('lines checkout would refuse are marked with the way to fix them', (tester) async {
      await _open(tester, server: (repo) {
        repo.storeId = 'seed-store';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 2, 28, current: 31));
      });
      await _openCart(tester);

      expect(find.text('Some items need your attention'), findsOneWidget);
      expect(find.text('Tomato (1 kg): price changed from ₹28 to ₹31'), findsOneWidget);
      await tester.tap(find.text('Update prices'));
      await tester.pumpAndSettle();

      expect(find.text('Some items need your attention'), findsNothing);
      expect(find.text('₹62'), findsWidgets);
    });

    testWidgets('a cart that is changed on another phone shows the change when the cart opens', (tester) async {
      final app = await _open(tester, server: (repo) {
        repo.storeId = 'seed-store';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 1, 28));
      });
      app.cartRepo.lines.single.quantity = 4;

      await _openCart(tester);

      expect(app.container.read(cartProvider), {'tomato:1kg': 4});
    });
  });

  group('two carts', () {
    testWidgets('signing in with a cart on the phone and a different saved one asks which to keep', (tester) async {
      final app = await _open(tester, signedIn: false, prefs: deviceLocationPrefs, server: (repo) {
        repo.storeId = 'some-other-store';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 3, 28));
      });
      app.container.read(cartProvider.notifier).add('apple:1kg');
      await tester.pumpAndSettle();

      app.auth.become(const AuthState(AuthStatus.signedIn, _asha));
      await tester.pumpAndSettle();

      expect(find.text('You have two carts'), findsOneWidget);
      await tester.tap(find.text('Keep my saved cart'));
      await tester.pumpAndSettle();

      expect(find.text('You have two carts'), findsNothing);
      expect(app.container.read(cartProvider), {'tomato:1kg': 3});
    });

    testWidgets('the other answer keeps the phone cart', (tester) async {
      final app = await _open(tester, signedIn: false, prefs: deviceLocationPrefs, server: (repo) {
        repo.storeId = 'some-other-store';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 3, 28));
      });
      app.container.read(cartProvider.notifier).add('apple:1kg');
      await tester.pumpAndSettle();
      app.auth.become(const AuthState(AuthStatus.signedIn, _asha));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Use the cart from this phone'));
      await tester.pumpAndSettle();

      expect(app.container.read(cartProvider), {'apple:1kg': 1});
      expect(app.cartRepo.lines.map((l) => l.key), ['apple:1kg']);
    });
  });
}
