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
import 'package:quickcart_customer/features/orders/orders_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/location_fakes.dart';

class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;
  @override
  AuthState build() => start;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');

class _Shop {
  _Shop(this.container, this.cartRepo, this.orderRepo, this.addresses);
  final ProviderContainer container;
  final FakeCartRepository cartRepo;
  final FakeOrderRepository orderRepo;
  final FakeAddressRepository addresses;
}

Future<_Shop> _open(
  WidgetTester tester, {
  bool withSavedAddress = true,
  void Function(FakeAddressRepository addresses)? seedAddresses,
  Map<String, Object> prefs = const {},
  void Function(FakeCartRepository cart)? serverCart,
  int tomatoes = 1,
}) async {
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2200 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final cartRepo = FakeCartRepository()
    ..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28)
    ..know('onion:1kg', productId: 'onion', variantId: 'onion:1kg', name: 'Onion', price: 26);
  final orderRepo = FakeOrderRepository()..onPlaced = cartRepo.lines.clear;
  final addresses = FakeAddressRepository();
  if (withSavedAddress) addresses.seed('Home', isDefault: true, flat: 'Flat 4');
  seedAddresses?.call(addresses);
  serverCart?.call(cartRepo);
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(const AuthState(AuthStatus.signedIn, _asha))),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cartRepo),
    orderRepositoryProvider.overrideWithValue(orderRepo),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
  if (serverCart == null) container.read(cartProvider.notifier).add('tomato:1kg', tomatoes);
  await tester.pumpAndSettle();
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/checkout');
  await tester.pumpAndSettle();
  return _Shop(container, cartRepo, orderRepo, addresses);
}

PillButton _placeOrder(WidgetTester tester) => tester.widget<PillButton>(find.byType(PillButton).last);

Future<void> _tapPlaceOrder(WidgetTester tester) async {
  await tester.tap(find.byType(PillButton).last);
  await tester.pumpAndSettle();
}

Future<void> _tapText(WidgetTester tester, String text) async {
  await tester.ensureVisible(find.text(text));
  await tester.pumpAndSettle();
  await tester.tap(find.text(text));
  await tester.pumpAndSettle();
}

void main() {
  group('placing the order', () {
    testWidgets('the order is placed for the saved address with a fresh key, the cart is emptied and the real order number is shown', (tester) async {
      final shop = await _open(tester);

      await _tapPlaceOrder(tester);

      final attempt = shop.orderRepo.attempts.single;
      expect((attempt.storeId, attempt.addressId, attempt.place), ('seed-store', 'a1', null));
      expect(attempt.key, matches(RegExp(r'^[0-9a-f]{32}$')));
      expect(find.text('Order placed!'), findsOneWidget);
      expect(find.textContaining('ORD-2000'), findsOneWidget);
      expect(find.text('Pay ₹58 in cash on delivery.'), findsOneWidget);
      expect(shop.container.read(cartProvider), isEmpty);
      expect(shop.container.read(ordersProvider).value!.single.number, 'ORD-2000');
    });

    testWidgets('Track order opens the real order, with the fee breakdown and the receiver', (tester) async {
      await _open(tester);
      await _tapPlaceOrder(tester);

      await _tapText(tester, 'Track order');

      expect(find.text('Order #ORD-2000'), findsOneWidget);
      expect(find.text('Item total'), findsOneWidget);
      expect(find.text('Delivery fee'), findsOneWidget);
      expect(find.text('Handling fee'), findsOneWidget);
      expect(find.text('Cash on delivery'), findsOneWidget);
      expect(find.text('Asha Patil · 9876543210'), findsOneWidget);
    });

    testWidgets('only cash on delivery is offered', (tester) async {
      await _open(tester);

      expect(find.text('Cash on delivery'), findsOneWidget);
      expect(find.text('Pay with cash when your order arrives.'), findsOneWidget);
      expect(find.text('UPI'), findsNothing);
      expect(find.text('Card'), findsNothing);
    });

    testWidgets('the summary shows the server fees', (tester) async {
      await _open(tester);

      expect(find.text('Delivery fee'), findsOneWidget);
      expect(find.text('₹25'), findsOneWidget);
      expect(find.text('₹5'), findsOneWidget);
      expect(find.text('₹58'), findsWidgets);
    });

    testWidgets('a place on this phone (no saved address) is sent as text and a point', (tester) async {
      final shop = await _open(tester, withSavedAddress: false, prefs: deviceLocationPrefs);

      await _tapPlaceOrder(tester);

      final attempt = shop.orderRepo.attempts.single;
      expect(attempt.addressId, isNull);
      expect(attempt.place!.line, contains('Sea Breeze'));
      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('a double tap makes one attempt', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.delay = const Duration(milliseconds: 300);

      await tester.tap(find.byType(PillButton).last);
      await tester.pump(const Duration(milliseconds: 50));
      expect(_placeOrder(tester).onPressed, isNull, reason: 'busy: the button is off while the order is going');
      expect(find.text('Placing your order…'), findsOneWidget);
      await tester.pumpAndSettle();

      expect(shop.orderRepo.attempts, hasLength(1));
    });
  });

  group('when the cart changed', () {
    testWidgets('a changed price stops the order, shows the lines and the way to fix them, and the customer decides', (tester) async {
      final shop = await _open(tester, tomatoes: 2);
      shop.cartRepo.lines.single.current = 31;
      shop.orderRepo.failures.add(const ConflictException('price', statusCode: 409, reason: CheckoutReasons.priceChanged));

      await _tapPlaceOrder(tester);

      expect(find.textContaining('Your cart changed while we were placing the order'), findsOneWidget);
      expect(find.text('Tomato (1 kg): price changed from ₹28 to ₹31'), findsOneWidget);
      expect(_placeOrder(tester).onPressed, isNull, reason: 'the customer must accept the new price first');
      expect(find.text('Order placed!'), findsNothing);

      await _tapText(tester, 'Update prices');

      expect(find.textContaining('price changed from'), findsNothing);
      expect(_placeOrder(tester).onPressed, isNotNull);
      expect(find.text('₹92'), findsWidgets, reason: 'the new total: 2 x 31 plus delivery 25 and handling 5');
    });

    testWidgets('after accepting the new price the next attempt is a new one with a new key', (tester) async {
      final shop = await _open(tester);
      shop.cartRepo.lines.single.current = 31;
      shop.orderRepo.failures.add(const ConflictException('price', statusCode: 409, reason: CheckoutReasons.priceChanged));
      await _tapPlaceOrder(tester);
      await _tapText(tester, 'Update prices');

      await _tapPlaceOrder(tester);

      expect(shop.orderRepo.attempts, hasLength(2));
      expect(shop.orderRepo.attempts[0].key, isNot(shop.orderRepo.attempts[1].key));
      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('an item that is gone is named and can be removed, and the rest is ordered', (tester) async {
      final shop = await _open(tester, serverCart: (repo) {
        repo.storeId = 'seed-store';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 1, 28));
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 1, 26, unavailable: true));
      });

      expect(find.text('Onion (1 kg) is no longer available'), findsOneWidget, reason: 'shown before trying');
      expect(_placeOrder(tester).onPressed, isNull);
      await _tapText(tester, 'Remove unavailable items');

      expect(find.textContaining('no longer available'), findsNothing);
      expect(shop.cartRepo.lines.map((l) => l.key), ['tomato:1kg']);
      await _tapPlaceOrder(tester);
      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('missing stock says how many are left and the quantity can be lowered to that', (tester) async {
      final shop = await _open(tester, tomatoes: 3);
      shop.cartRepo.lines.single.available = 1;

      await shop.container.read(cartProvider.notifier).reload();
      await tester.pumpAndSettle();

      expect(find.text('Tomato (1 kg): only 1 left'), findsOneWidget);
      await _tapText(tester, 'Use available quantity');
      expect(shop.cartRepo.lines.single.quantity, 1);
      await _tapPlaceOrder(tester);
      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('a conflict found by the server is explained even if the app had not seen it coming', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const ConflictException('stock', statusCode: 409, reason: CheckoutReasons.inventoryConflict));
      shop.cartRepo.lines.single.available = 0;

      await _tapPlaceOrder(tester);

      expect(find.textContaining('Your cart changed while we were placing the order'), findsOneWidget);
      expect(find.text('Tomato (1 kg): only 0 left'), findsOneWidget);
    });
  });

  group('when the order did not go through', () {
    testWidgets('an empty cart says so and points to the orders in case it was already placed', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const ConflictException('empty', statusCode: 409, reason: CheckoutReasons.cartEmpty));

      await _tapPlaceOrder(tester);

      expect(find.text('Your cart is empty. If you already placed this order, you will find it in Your orders.'), findsOneWidget);
      expect(shop.orderRepo.listCalls, greaterThan(0), reason: 'the orders are read again, so a placed order shows');
      await _tapText(tester, 'View your orders');
      expect(find.text('Your orders'), findsOneWidget);
    });

    testWidgets('an address nobody delivers to is explained with a way to choose another', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const ConflictException('area', statusCode: 409, reason: ServiceabilityReasons.outsideServiceArea));

      await _tapPlaceOrder(tester);

      expect(find.text('We do not deliver to this address yet. Choose another address to place your order.'), findsWidgets);
      await _tapText(tester, 'Choose another address');
      expect(find.text('Delivery addresses'), findsOneWidget);
    });

    testWidgets('no answer: the customer is told the order may exist, and trying again uses the same key so it cannot be doubled', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const NetworkException('offline'));

      await _tapPlaceOrder(tester);

      expect(find.textContaining('We could not confirm your order'), findsOneWidget);
      expect(_placeOrder(tester).onPressed, isNotNull, reason: 'the customer can try again');
      await _tapPlaceOrder(tester);

      expect(shop.orderRepo.attempts, hasLength(2));
      expect(shop.orderRepo.attempts[0].key, shop.orderRepo.attempts[1].key);
      expect(find.text('Order placed!'), findsOneWidget);
      expect(shop.orderRepo.orders, hasLength(1));
    });

    testWidgets('a timeout and a server error are treated the same way: same key on the retry', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const TimeoutApiException('slow'));
      shop.orderRepo.failures.add(const ServerException('boom', statusCode: 500));

      await _tapPlaceOrder(tester);
      await _tapPlaceOrder(tester);
      await _tapPlaceOrder(tester);

      expect(shop.orderRepo.attempts.map((a) => a.key).toSet(), hasLength(1));
      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('View your orders from the unconfirmed message opens the list', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const NetworkException('offline'));
      await _tapPlaceOrder(tester);

      await _tapText(tester, 'View your orders');

      expect(find.text('Your orders'), findsOneWidget);
    });

    testWidgets('a refusal the app does not know gets a plain message and the next attempt is a new one', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const ConflictException('who knows', statusCode: 409));

      await _tapPlaceOrder(tester);
      expect(find.text('We could not place your order. Please try again.'), findsOneWidget);
      await _tapPlaceOrder(tester);

      expect(shop.orderRepo.attempts[0].key, isNot(shop.orderRepo.attempts[1].key));
      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('a rejected request (validation) is not retried with the same key', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const ValidationException('bad', statusCode: 400, errors: ['Delivery address is required']));

      await _tapPlaceOrder(tester);
      await _tapPlaceOrder(tester);

      expect(find.text('We could not place your order. Please try again.'), findsNothing);
      expect(shop.orderRepo.attempts[0].key, isNot(shop.orderRepo.attempts[1].key));
    });

    testWidgets('the button works again after a failure and the technical text never reaches the screen', (tester) async {
      final shop = await _open(tester);
      shop.orderRepo.failures.add(const ServerException('NullReferenceException at Foo.Bar', statusCode: 500));

      await _tapPlaceOrder(tester);

      expect(find.textContaining('NullReference'), findsNothing);
      expect(_placeOrder(tester).onPressed, isNotNull);
    });
  });

}
