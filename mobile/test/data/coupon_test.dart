import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/widgets.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/dto/order_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/coupon_repository.dart';
import 'package:quickcart_customer/data/repositories/local_shop.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:quickcart_customer/features/cart/coupon_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';
import '../support/location_fakes.dart';

class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;
  @override
  AuthState build() => start;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');

ApiClient _client(FakeAdapter adapter) => ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

Map<String, dynamic> _order({Map<String, dynamic> extra = const {}}) => {
      'id': 'o1',
      'orderNumber': 'ORD-1',
      'storeId': 's1',
      'totalAmount': 51,
      'status': 0,
      'deliveryAddress': '12 Marine Drive',
      'createdAt': '2026-10-09T10:00:00Z',
      'items': [
        {'productId': 'p1', 'productNameSnapshot': 'Tomato', 'unitPrice': 28, 'quantity': 2, 'totalPrice': 56, 'variantId': 'v1', 'variantLabel': '1 kg'}
      ],
      'subtotalAmount': 56,
      'deliveryFee': 25,
      'handlingFee': 5,
      ...extra,
    };

/// Answers by code, the way the server would: SAVE10 takes 10% off the item total (₹28 here, so ₹2.8), MIN500 needs more in the cart.
class FakeCouponRepository implements CouponRepository {
  List<FeaturedCoupon> shown = const [];

  @override
  Future<List<FeaturedCoupon>> featured(String storeId) async => shown;

  final calls = <({String storeId, String code})>[];
  Object? failNext;
  double itemTotal = 28;

  @override
  Future<CouponPreview> preview({required String storeId, required String code}) async {
    calls.add((storeId: storeId, code: code));
    if (failNext case final failure?) {
      failNext = null;
      throw failure;
    }
    const fees = 30.0;
    return switch (code) {
      'SAVE10' => CouponPreview(valid: true, code: code, discount: itemTotal * .1, message: 'Code applied.', total: itemTotal + fees - itemTotal * .1),
      'MIN500' => CouponPreview(valid: false, code: code, discount: 0, message: 'Add items worth ₹472 more to use that code.', total: itemTotal + fees),
      _ => CouponPreview(valid: false, code: code, discount: 0, message: 'That code is not valid.', total: itemTotal + fees),
    };
  }
}

class _Shop {
  _Shop(this.container, this.coupons, this.orders);
  final ProviderContainer container;
  final FakeCouponRepository coupons;
  final FakeOrderRepository orders;
}

Future<_Shop> _open(WidgetTester tester, {String route = '/cart', List<FeaturedCoupon> shown = const []}) async {
  SharedPreferences.setMockInitialValues(const {});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2200 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final cartRepo = FakeCartRepository()..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28);
  final orders = FakeOrderRepository()..onPlaced = cartRepo.lines.clear;
  final coupons = FakeCouponRepository()..shown = shown;
  final addresses = FakeAddressRepository()..seed('Home', isDefault: true, flat: 'Flat 4');
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(const AuthState(AuthStatus.signedIn, _asha))),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cartRepo),
    orderRepositoryProvider.overrideWithValue(orders),
    couponRepositoryProvider.overrideWithValue(coupons),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
  container.read(cartProvider.notifier).add('tomato:1kg', 1);
  await tester.pumpAndSettle();
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push(route);
  await tester.pumpAndSettle();
  return _Shop(container, coupons, orders);
}

Future<void> _type(WidgetTester tester, String code) async {
  await tester.enterText(find.byType(TextField).first, code);
  await tester.tap(find.text('Apply'));
  await tester.pumpAndSettle();
}

void main() {
  group('reading and sending coupons', () {
    test('an order carries its coupon code and discount; older answers have none', () {
      final used = orderFromJson(_order(extra: {'couponCode': 'SAVE10', 'couponDiscount': 5.6, 'totalAmount': 50.4}));
      expect((used.couponCode, used.couponDiscount), ('SAVE10', 5.6));
      expect(used.withStage(OrderStage.cancelled).couponCode, 'SAVE10'); // copies keep it
      expect(used.withPayment(stage: OrderStage.placed, state: PaymentState.paid).couponDiscount, 5.6);

      final plain = orderFromJson(_order());
      expect((plain.couponCode, plain.couponDiscount), (null, 0));
    });

    test('the preview posts the code to the store and reads what the server decided', () async {
      final adapter = FakeAdapter()
        ..replyJson({
          'success': true,
          'data': {'valid': true, 'code': 'SAVE10', 'discount': 2.8, 'message': 'Code applied.', 'total': 55.2}
        })
        ..replyJson({
          'success': true,
          'data': {'valid': false, 'code': 'OLD', 'discount': 0, 'message': 'That code has expired.', 'total': 58}
        });
      final repo = ApiCouponRepository(_client(adapter));

      final good = await repo.preview(storeId: 's1', code: 'SAVE10');
      final bad = await repo.preview(storeId: 's1', code: 'OLD');

      expect(adapter.requests[0].path, '/api/carts/s1/coupon');
      expect(adapter.requests[0].data, {'code': 'SAVE10'});
      expect((good.valid, good.discount, good.total), (true, 2.8, 55.2));
      expect((bad.valid, bad.discount, bad.message), (false, 0, 'That code has expired.'));
    });

    test('the order sends the code only when there is one', () async {
      final adapter = FakeAdapter()
        ..replyJson({'success': true, 'data': _order()}, status: 201)
        ..replyJson({'success': true, 'data': _order()}, status: 201);
      final repo = ApiOrderRepository(_client(adapter));

      await repo.place(storeId: 's1', idempotencyKey: 'k1', addressId: 'a1', couponCode: 'SAVE10');
      await repo.place(storeId: 's1', idempotencyKey: 'k2', addressId: 'a1');

      expect(adapter.requests[0].data, {'addressId': 'a1', 'couponCode': 'SAVE10', 'outOfStockChoice': 'CallMe'});
      expect(adapter.requests[1].data, {'addressId': 'a1', 'outOfStockChoice': 'CallMe'});
    });

    test('offline demo data refuses a code rather than ignoring it', () async {
      final preview = await LocalCouponRepository().preview(storeId: 's1', code: 'SAVE10');
      expect(preview.valid, isFalse);
      await expectLater(LocalOrderRepository(LocalShop()).place(storeId: 's1', idempotencyKey: 'k', addressId: 'a', couponCode: 'SAVE10'), throwsA(isA<ConflictException>()));
    });
  });

  group('the coupon box in the cart', () {
    testWidgets('a coupon marked as a pop-up appears once, and its card in the cart applies it', (tester) async {
      const listed = FeaturedCoupon(id: 'c1', code: 'SAVE10', kind: 'Percent', value: 10, showAsPopup: true);
      final shop = await _open(tester, shown: const [listed]);
      // Closed if Home showed it before the cart was opened.
      final close = find.byTooltip('Close');
      if (close.evaluate().isNotEmpty) {
        await tester.tap(close);
        await tester.pumpAndSettle();
      }
      expect(find.text('Coupons for you'), findsOneWidget);

      await tester.tap(find.text('Apply').first);
      await tester.pumpAndSettle();

      expect(shop.coupons.calls.single.code, 'SAVE10');
      expect(find.text('Applied'), findsOneWidget);
      final prefs = shop.container.read(sharedPrefsProvider);
      expect(prefs.getStringList('coupon_popups_seen_u1'), ['c1']);
    });

    testWidgets('a good code shows as applied, takes its discount off To pay and the Checkout button', (tester) async {
      final shop = await _open(tester);

      await _type(tester, 'save10');

      expect(shop.coupons.calls.single.code, 'SAVE10'); // typed in lower case, sent upper-case
      expect(find.text('SAVE10 applied'), findsOneWidget);
      expect(find.text('Coupon (SAVE10)'), findsOneWidget);
      expect(find.text('−₹2.80'), findsOneWidget);
      expect(shop.container.read(couponProvider).orderCode, 'SAVE10');
      expect(find.textContaining('Checkout · ₹55'), findsOneWidget); // 28 + 25 + 5 - 2.80 rounds to ₹55.2
    });

    testWidgets('a refused code shows why and takes nothing off', (tester) async {
      final shop = await _open(tester);

      await _type(tester, 'MIN500');

      expect(find.text('Add items worth ₹472 more to use that code.'), findsOneWidget);
      expect(find.textContaining('applied'), findsNothing);
      expect(shop.container.read(couponProvider).orderCode, isNull);
      expect(find.byType(TextField), findsOneWidget); // the box stays for another try
    });

    testWidgets('Remove clears the code and the discount', (tester) async {
      final shop = await _open(tester);
      await _type(tester, 'SAVE10');

      await tester.tap(find.text('Remove'));
      await tester.pumpAndSettle();

      expect(shop.container.read(couponProvider).code, isNull);
      expect(find.text('Coupon (SAVE10)'), findsNothing);
      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('when the check cannot reach the server it says so and the order is not sent with the code', (tester) async {
      final shop = await _open(tester);
      shop.coupons.failNext = const NetworkException('offline');

      await _type(tester, 'SAVE10');

      expect(find.text('Could not check the code. Try again.'), findsOneWidget);
      expect(shop.container.read(couponProvider).orderCode, isNull);
    });

    testWidgets('emptying the cart drops the code', (tester) async {
      final shop = await _open(tester);
      await _type(tester, 'SAVE10');

      shop.container.read(cartProvider.notifier).delete('tomato:1kg');
      await tester.pumpAndSettle();

      expect(shop.container.read(couponProvider).code, isNull);
    });

    testWidgets('a change of the item total checks the code again', (tester) async {
      final shop = await _open(tester);
      await _type(tester, 'SAVE10');
      expect(shop.coupons.calls, hasLength(1));

      shop.coupons.itemTotal = 56;
      shop.container.read(cartProvider.notifier).add('tomato:1kg', 1);
      await tester.pumpAndSettle();

      expect(shop.coupons.calls.length, greaterThan(1));
      expect(shop.container.read(couponProvider).discount, closeTo(5.6, .001));
    });
  });

  group('checkout with a coupon', () {
    testWidgets('the order is placed with the applied code', (tester) async {
      final shop = await _open(tester);
      await _type(tester, 'SAVE10');
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/checkout');
      await tester.pumpAndSettle();
      expect(find.text('Coupon (SAVE10)'), findsOneWidget);

      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();

      expect(shop.orders.coupons, ['SAVE10']);
    });

    testWidgets('without a code the order is placed without one', (tester) async {
      final shop = await _open(tester, route: '/checkout');

      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();

      expect(shop.orders.coupons, [null]);
    });

    testWidgets('a code the server turns down at the last moment is dropped and the customer is told', (tester) async {
      final shop = await _open(tester);
      await _type(tester, 'SAVE10');
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/checkout');
      await tester.pumpAndSettle();
      shop.orders.failures.add(const ConflictException('That code has been used up.', statusCode: 409, reason: CheckoutReasons.couponInvalid));

      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();

      expect(find.textContaining('That coupon could not be used'), findsOneWidget);
      expect(shop.container.read(couponProvider).code, isNull);
      expect(find.text('Coupon (SAVE10)'), findsNothing);
    });
  });
}

