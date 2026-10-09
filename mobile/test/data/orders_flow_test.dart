import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/widgets.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/models.dart';
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
  _App(this.container, this.orders, this.cart, this.auth);
  final ProviderContainer container;
  final FakeOrderRepository orders;
  final FakeCartRepository cart;
  final _Auth auth;
}

Future<_App> _open(WidgetTester tester, {List<Order> orders = const [], bool signedIn = true, Duration delay = Duration.zero, Object? failRead}) async {
  SharedPreferences.setMockInitialValues({});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 1800 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final repo = FakeOrderRepository()
    ..orders.addAll(orders)
    ..delay = delay
    ..failRead = failRead;
  final cart = FakeCartRepository();
  final addresses = FakeAddressRepository()..seed('Home', isDefault: true);
  final auth = _Auth(signedIn ? const AuthState(AuthStatus.signedIn, _asha) : const AuthState(AuthStatus.signedOut));
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => auth),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cart),
    orderRepositoryProvider.overrideWithValue(repo),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  return _App(ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))), repo, cart, auth);
}

Future<void> _goOrders(WidgetTester tester) async {
  await tester.tap(find.byTooltip('Orders'));
  await tester.pumpAndSettle();
}

void main() {
  group('the orders list', () {
    testWidgets('shows each real order with its number, status, item count and total', (tester) async {
      await _open(tester, orders: [sampleOrder(), sampleOrder(number: 'ORD-1002', stage: OrderStage.packed)]);

      await _goOrders(tester);

      expect(find.text('#ORD-1001'), findsOneWidget);
      expect(find.text('#ORD-1002'), findsOneWidget);
      expect(find.text('Delivered'), findsOneWidget);
      expect(find.text('Packed'), findsOneWidget);
      expect(find.text('3 items · ₹162'), findsNWidgets(2), reason: '2 x 34 + 89 is 157, plus handling 5');
    });

    testWidgets('a declined order says so', (tester) async {
      await _open(tester, orders: [sampleOrder(stage: OrderStage.rejected)]);

      await _goOrders(tester);

      expect(find.text('Declined'), findsOneWidget);
    });

    testWidgets('while the orders load, placeholders are shown, not an empty list', (tester) async {
      await _open(tester, orders: [sampleOrder()], delay: const Duration(milliseconds: 400));
      await tester.tap(find.byTooltip('Orders'));
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.byType(SkeletonBox), findsWidgets);
      expect(find.text('No orders yet'), findsNothing);
      await tester.pumpAndSettle();
      expect(find.text('#ORD-1001'), findsOneWidget);
    });

    testWidgets('with no orders it invites the customer to shop', (tester) async {
      await _open(tester);

      await _goOrders(tester);

      expect(find.text('No orders yet'), findsOneWidget);
    });

    testWidgets('a failed read says so and Retry loads them', (tester) async {
      await _open(tester, orders: [sampleOrder()], failRead: const NetworkException('offline'));

      await _goOrders(tester);
      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
      await tester.tap(find.text('Retry'));
      await tester.pumpAndSettle();

      expect(find.text('#ORD-1001'), findsOneWidget);
    });

    testWidgets('pulling down reads the orders again', (tester) async {
      final app = await _open(tester, orders: [sampleOrder()]);
      await _goOrders(tester);
      final before = app.orders.listCalls;
      app.orders.orders.insert(0, sampleOrder(number: 'ORD-1003', stage: OrderStage.placed));

      await tester.drag(find.byType(ListView), const Offset(0, 1400));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 300));
      await tester.pump(const Duration(seconds: 1));
      await tester.pumpAndSettle();

      expect(app.orders.listCalls, greaterThan(before));
      expect(find.text('#ORD-1003'), findsOneWidget);
    });

    testWidgets('a guest is asked to sign in and no orders are read', (tester) async {
      final app = await _open(tester, signedIn: false, orders: [sampleOrder()]);

      await _goOrders(tester);

      expect(find.text('Sign in to see your orders'), findsOneWidget);
      expect(app.orders.listCalls, 0);
    });

    testWidgets('signing out hides the orders', (tester) async {
      final app = await _open(tester, orders: [sampleOrder()]);
      await _goOrders(tester);
      expect(find.text('#ORD-1001'), findsOneWidget);

      app.auth.become(const AuthState(AuthStatus.signedOut));
      await tester.pumpAndSettle();

      expect(find.text('#ORD-1001'), findsNothing);
      expect(find.text('Sign in to see your orders'), findsOneWidget);
    });
  });

  group('one order', () {
    testWidgets('shows the lines as they were ordered, the fee breakdown, the way to pay and who receives it', (tester) async {
      await _open(tester, orders: [sampleOrder(deliveryFee: 25, handlingFee: 5)]);
      await _goOrders(tester);

      await tester.tap(find.text('#ORD-1001'));
      await tester.pumpAndSettle();

      expect(find.text('Order #ORD-1001'), findsOneWidget);
      expect(find.textContaining('Farm Milk'), findsOneWidget);
      expect(find.textContaining('Basmati Rice'), findsOneWidget);
      expect(find.text('₹68'), findsOneWidget, reason: '2 x 34');
      expect(find.text('Item total'), findsOneWidget);
      expect(find.text('₹157'), findsOneWidget);
      expect(find.text('₹25'), findsOneWidget);
      expect(find.text('₹5'), findsOneWidget);
      expect(find.text('₹187'), findsOneWidget);
      expect(find.text('Cash on delivery'), findsOneWidget);
      expect(find.text('Flat 4, 12 Marine Drive, Mumbai'), findsOneWidget);
      expect(find.text('Asha Patil · 9876543210'), findsOneWidget);
    });

    testWidgets('free delivery reads FREE and an order without a receiver shows only the address', (tester) async {
      await _open(tester, orders: [sampleOrder(deliveryFee: 0, receiverName: null)]);
      await _goOrders(tester);

      await tester.tap(find.text('#ORD-1001'));
      await tester.pumpAndSettle();

      expect(find.text('FREE'), findsOneWidget);
      expect(find.textContaining('9876543210'), findsNothing);
    });

    testWidgets('a declined order stops the timeline after "Order placed"', (tester) async {
      await _open(tester, orders: [sampleOrder(stage: OrderStage.rejected)]);
      await _goOrders(tester);

      await tester.tap(find.text('#ORD-1001'));
      await tester.pumpAndSettle();

      expect(find.text('Order placed'), findsOneWidget);
      expect(find.text('Declined'), findsWidgets);
      expect(find.text('Out for delivery'), findsNothing);
      expect(find.text('Packed'), findsNothing);
    });

    testWidgets('a delivered order shows every step reached', (tester) async {
      await _open(tester, orders: [sampleOrder()]);
      await _goOrders(tester);

      await tester.tap(find.text('#ORD-1001'));
      await tester.pumpAndSettle();

      for (final step in ['Order placed', 'Packed', 'Out for delivery', 'Delivered']) {
        expect(find.text(step), findsWidgets);
      }
      expect(find.byIcon(Icons.check), findsNWidgets(4));
    });

    testWidgets('an order that cannot be read shows a retry, not a blank screen', (tester) async {
      final app = await _open(tester);
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/order/id-ORD-9');
      app.orders.orders.add(sampleOrder(number: 'ORD-9'));
      app.orders.failRead = const NetworkException('offline');
      await tester.pumpAndSettle();

      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
      await tester.tap(find.text('Retry'));
      await tester.pumpAndSettle();

      expect(find.text('Order #ORD-9'), findsOneWidget);
    });

    testWidgets('Reorder adds what can be bought, leaves out what cannot, and says so', (tester) async {
      final app = await _open(tester, orders: [
        sampleOrder(lines: const [
          OrderLine(productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', label: '1 kg', unitPrice: 28, quantity: 2),
          OrderLine(productId: 'ghost', variantId: 'ghost:1', name: 'Ghost Item', label: '1', unitPrice: 10, quantity: 1),
        ])
      ]);
      await _goOrders(tester);
      await tester.tap(find.text('#ORD-1001'));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.text('Reorder'));
      await tester.tap(find.text('Reorder'));
      await tester.pumpAndSettle();

      expect(app.container.read(cartProvider), {'tomato:1kg': 2});
      expect(find.text('Your cart'), findsOneWidget);
    });
  });

  group('elsewhere in the app', () {
    testWidgets('Home offers Buy again with the latest real order', (tester) async {
      await _open(tester, orders: [sampleOrder(number: 'ORD-1005', deliveryFee: 0)]);

      expect(find.text('Buy again'), findsOneWidget);
      expect(find.text('3 items · ₹162'), findsOneWidget);
      await tester.tap(find.text('Buy again'));
      await tester.pumpAndSettle();

      expect(find.text('Order #ORD-1005'), findsOneWidget);
    });

    testWidgets('Home without orders sends Buy again to the categories', (tester) async {
      await _open(tester);

      await tester.tap(find.text('Buy again'));
      await tester.pumpAndSettle();

      expect(find.text('Dairy'), findsWidgets);
    });

    testWidgets('the profile counts the real orders', (tester) async {
      await _open(tester, orders: [sampleOrder(), sampleOrder(number: 'ORD-1002')]);

      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();

      expect(find.textContaining('2'), findsWidgets);
    });
  });
}
