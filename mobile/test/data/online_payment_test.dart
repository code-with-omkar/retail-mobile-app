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
import 'package:quickcart_customer/data/payment_launcher.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';
import 'package:quickcart_customer/data/repositories/payment_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:quickcart_customer/features/orders/orders_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';
import '../support/location_fakes.dart';
import '../support/payment_fakes.dart';

class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;
  @override
  AuthState build() => start;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');

Map<String, dynamic> _order({Object status = 10, Object? paymentStatus = 1, String method = 'Online', String? expires = '2026-10-09T10:15:00Z'}) => {
      'id': 'o1',
      'orderNumber': 'ORD-1',
      'storeId': 's1',
      'totalAmount': 86,
      'status': status,
      'deliveryAddress': '12 Marine Drive',
      'createdAt': '2026-10-09T10:00:00Z',
      'items': [
        {'productId': 'p1', 'productNameSnapshot': 'Tomato', 'unitPrice': 28, 'quantity': 2, 'totalPrice': 56, 'variantId': 'v1', 'variantLabel': '1 kg'}
      ],
      'subtotalAmount': 56,
      'deliveryFee': 25,
      'handlingFee': 5,
      'paymentMethod': method,
      'paymentStatus': paymentStatus,
      'paymentExpiresAt': expires,
    };

ApiClient _client(FakeAdapter adapter) => ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

class _Shop {
  _Shop(this.container, this.cartRepo, this.orders, this.payments, this.launcher);
  final ProviderContainer container;
  final FakeCartRepository cartRepo;
  final FakeOrderRepository orders;
  final FakePaymentRepository payments;
  final FakePaymentLauncher launcher;
}

Future<_Shop> _open(WidgetTester tester, {bool online = true}) async {
  SharedPreferences.setMockInitialValues(const {});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2200 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final cartRepo = FakeCartRepository()..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28);
  final orders = FakeOrderRepository()..onPlaced = cartRepo.lines.clear;
  final payments = FakePaymentRepository(orders, online: online);
  final launcher = FakePaymentLauncher();
  final addresses = FakeAddressRepository()..seed('Home', isDefault: true, flat: 'Flat 4');
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(const AuthState(AuthStatus.signedIn, _asha))),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cartRepo),
    orderRepositoryProvider.overrideWithValue(orders),
    paymentRepositoryProvider.overrideWithValue(payments),
    paymentLauncherProvider.overrideWithValue(launcher),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
  container.read(cartProvider.notifier).add('tomato:1kg', 1);
  await tester.pumpAndSettle();
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/checkout');
  await tester.pumpAndSettle();
  return _Shop(container, cartRepo, orders, payments, launcher);
}

Future<void> _tapPrimary(WidgetTester tester) async {
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
  group('reading online orders and payments from the server', () {
    test('an order waiting for payment is read with its payment state and the time it is held until', () {
      final order = orderFromJson(_order());

      expect((order.stage, order.paymentState, order.isOnline), (OrderStage.awaitingPayment, PaymentState.created, true));
      expect(order.paymentExpiresAt, DateTime.utc(2026, 10, 9, 10, 15).toLocal());
      expect(order.stage.isActive, isTrue);
    });

    test('the payment state is read from its number or its name, and a missing one means cash on delivery', () {
      expect([0, 1, 2, 3, 4, 5, 6].map(paymentStateFrom), PaymentState.values);
      expect(paymentStateFrom('Paid'), PaymentState.paid);
      expect(paymentStateFrom('RefundFailed'), PaymentState.refundFailed);
      expect(paymentStateFrom(null), PaymentState.notRequired);
      final cash = orderFromJson(_order(status: 0, paymentStatus: null, method: 'CashOnDelivery', expires: null));
      expect((cash.stage, cash.paymentState, cash.paymentExpiresAt, cash.isOnline), (OrderStage.placed, PaymentState.notRequired, null, false));
    });

    test('choosing online sends the payment method, and cash on delivery sends none', () async {
      final adapter = FakeAdapter()
        ..replyJson({'success': true, 'data': _order()}, status: 201)
        ..replyJson({'success': true, 'data': _order(status: 0, method: 'CashOnDelivery', paymentStatus: 0, expires: null)}, status: 201);
      final repo = ApiOrderRepository(_client(adapter));

      final online = await repo.place(storeId: 's1', idempotencyKey: 'k1', addressId: 'a1', online: true);
      await repo.place(storeId: 's1', idempotencyKey: 'k2', addressId: 'a1');

      expect(online.stage, OrderStage.awaitingPayment);
      expect(adapter.requests[0].data, {'addressId': 'a1', 'paymentMethod': 'Online'});
      expect(adapter.requests[1].data, {'addressId': 'a1'});
    });

    test('the options, the start and the confirm use the right routes and read the answers', () async {
      final adapter = FakeAdapter()
        ..replyJson({
          'success': true,
          'data': {'cashOnDelivery': true, 'online': true, 'holdMinutes': 15}
        })
        ..replyJson({
          'success': true,
          'data': {'provider': 'Razorpay', 'keyId': 'rzp_test_x', 'providerOrderId': 'order_1', 'amountPaise': 8600, 'currency': 'INR', 'expiresAt': '2026-10-09T10:15:00Z', 'orderNumber': 'ORD-1'}
        })
        ..replyJson({'success': true, 'data': _order(status: 0, paymentStatus: 3)});
      final repo = ApiPaymentRepository(_client(adapter));

      final options = await repo.options();
      final session = await repo.start('o1');
      final paid = await repo.confirm('o1', const PaymentProof(providerOrderId: 'order_1', providerPaymentId: 'pay_1', signature: 'sig'));

      expect((options.online, options.holdMinutes), (true, 15));
      expect((session.keyId, session.providerOrderId, session.amountPaise, session.orderNumber), ('rzp_test_x', 'order_1', 8600, 'ORD-1'));
      expect((paid.stage, paid.paymentState), (OrderStage.placed, PaymentState.paid));
      expect(adapter.requests.map((r) => '${r.method} ${r.path}'), ['GET /api/payments/options', 'POST /api/customer/orders/o1/payment', 'POST /api/customer/orders/o1/payment/confirm']);
      expect(adapter.requests[2].data, {'providerOrderId': 'order_1', 'providerPaymentId': 'pay_1', 'signature': 'sig'});
    });

    test('a refused payment is a conflict with its reason', () async {
      final adapter = FakeAdapter()..replyJson({'success': false, 'message': 'Too late', 'reason': 'PaymentHoldExpired'}, status: 409);
      final repo = ApiPaymentRepository(_client(adapter));

      await expectLater(repo.start('o1'), throwsA(isA<ConflictException>().having((e) => e.reason, 'reason', PaymentReasons.holdExpired)));
    });

    test('without a server there is no online payment', () async {
      final repo = LocalPaymentRepository();

      expect((await repo.options()).online, isFalse);
      await expectLater(repo.start('o1'), throwsA(isA<ConflictException>()));
    });
  });

  group('choosing how to pay', () {
    testWidgets('both ways are offered when the server offers online payment, and cash on delivery is chosen to begin with', (tester) async {
      await _open(tester);

      expect(find.text('Cash on delivery'), findsOneWidget);
      expect(find.text('Pay online'), findsOneWidget);
      expect(find.text('Place order · ₹58'), findsOneWidget);
    });

    testWidgets('only cash on delivery is offered while online payment is off', (tester) async {
      await _open(tester, online: false);

      expect(find.text('Pay online'), findsNothing);
      expect(find.text('Cash on delivery'), findsOneWidget);
    });

    testWidgets('a cash order is placed as before, without any payment', (tester) async {
      final shop = await _open(tester);

      await _tapPrimary(tester);

      expect(shop.orders.attempts.single.online, isFalse);
      expect(find.text('Order placed!'), findsOneWidget);
      expect(shop.payments.calls.where((c) => c.startsWith('start')), isEmpty);
    });
  });

  group('paying online', () {
    testWidgets('choosing online places an order waiting for payment, opens the provider screen at once and ends on the order placed screen once paid', (tester) async {
      final shop = await _open(tester);
      await _tapText(tester, 'Pay online');
      expect(find.text('Pay online · ₹58'), findsOneWidget);

      await _tapPrimary(tester);

      expect(shop.orders.attempts.single.online, isTrue);
      expect(shop.payments.calls, containsAllInOrder(['start id-ORD-2000', 'confirm id-ORD-2000']));
      expect(shop.launcher.sessions.single.amountPaise, 5800);
      expect(shop.launcher.lastPrefill, (name: 'Asha Patil', phone: '9876543210', email: 'asha@example.test'));
      expect(find.text('Order placed!'), findsOneWidget);
      expect(find.text('Paid ₹58 online.'), findsOneWidget);
      expect(find.text('Pay ₹58 in cash on delivery.'), findsNothing);
      final order = shop.container.read(ordersProvider).value!.single;
      expect((order.stage, order.paymentState), (OrderStage.placed, PaymentState.paid));
      expect(shop.container.read(cartProvider), isEmpty);
    });

    testWidgets('closing the provider screen leaves the order waiting, says so and lets the customer try again', (tester) async {
      final shop = await _open(tester);
      shop.launcher.outcomes.add(const PaymentDismissed());
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      expect(find.text('Payment was not completed. You can try again.'), findsOneWidget);
      expect(find.text('Try again · ₹58'), findsOneWidget);
      expect(find.textContaining('Your items are held for'), findsOneWidget);
      expect(shop.payments.confirmed, isEmpty);

      await tester.tap(find.text('Try again · ₹58'));
      await tester.pumpAndSettle();

      expect(find.text('Order placed!'), findsOneWidget);
      expect(shop.launcher.sessions, hasLength(2));
      expect(shop.payments.confirmed, hasLength(1));
    });

    testWidgets('a failed payment says no money is lost and offers another try', (tester) async {
      final shop = await _open(tester);
      shop.launcher.outcomes.add(const PaymentFailed());
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      expect(find.textContaining('Your payment did not go through'), findsOneWidget);
      expect(find.text('Try again · ₹58'), findsOneWidget);
    });

    testWidgets('when the server cannot confirm just now the customer is told it is being confirmed, cannot pay twice, and the order turns paid on its own', (tester) async {
      final shop = await _open(tester);
      shop.payments.failConfirm = const ConflictException('later', statusCode: 409, reason: PaymentReasons.providerUnavailable);
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      expect(find.textContaining('We received your payment and are confirming it'), findsOneWidget);
      expect(tester.widget<PillButton>(find.byType(PillButton).first).onPressed, isNull, reason: 'no second payment while the first is being confirmed');
      expect(shop.launcher.sessions, hasLength(1));

      // The provider tells the server itself; the screen notices on its next check.
      shop.payments.markPaid('id-ORD-2000');
      await tester.pump(const Duration(seconds: 6));
      await tester.pumpAndSettle();

      expect(find.text('Order placed!'), findsOneWidget);
    });

    testWidgets('a network failure while confirming is treated the same: nothing is lost', (tester) async {
      final shop = await _open(tester);
      shop.payments.failConfirm = const NetworkException('down');
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      expect(find.textContaining('We received your payment and are confirming it'), findsOneWidget);
    });

    testWidgets('an order whose time ran out is shown as cancelled with the refund promise', (tester) async {
      final shop = await _open(tester);
      shop.launcher.outcomes.add(const PaymentDismissed());
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      shop.payments.expire('id-ORD-2000');
      await tester.pump(const Duration(seconds: 6));
      await tester.pumpAndSettle();

      expect(find.textContaining('cancelled because it was not paid in time'), findsOneWidget);
      expect(find.text('Pay now'), findsNothing);
      expect(find.text('View your orders'), findsOneWidget);
    });

    testWidgets('cancelling an unpaid order from the payment screen asks first and cancels without a charge', (tester) async {
      final shop = await _open(tester);
      shop.launcher.outcomes.add(const PaymentDismissed());
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      await _tapText(tester, 'Cancel order');
      expect(find.text('You have not paid yet, so nothing is charged. The items go back to the store.'), findsOneWidget);
      await tester.tap(find.text('Cancel order').last);
      await tester.pumpAndSettle();

      expect(shop.orders.cancelled, ['id-ORD-2000']);
      expect(find.textContaining('This order was cancelled'), findsOneWidget);
    });

    testWidgets('online payment being switched off after the choice falls back to cash on delivery with a message', (tester) async {
      final shop = await _open(tester);
      shop.orders.failures.add(const ConflictException('off', statusCode: 409, reason: PaymentReasons.unavailable));
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);

      expect(find.textContaining('Online payment is not available right now. You can pay cash on delivery instead.'), findsOneWidget);
      expect(find.text('Place order · ₹58'), findsOneWidget);
    });

    testWidgets('the order list and the order page show an unpaid order as awaiting payment with a way to pay', (tester) async {
      final shop = await _open(tester);
      shop.launcher.outcomes.add(const PaymentDismissed());
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);
      GoRouter.of(tester.element(find.byType(Scaffold).first)).go('/orders');
      await tester.pumpAndSettle();

      expect(find.text('Awaiting payment'), findsWidgets);

      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/order/id-ORD-2000');
      await tester.pumpAndSettle();

      expect(find.textContaining('Payment pending'), findsOneWidget);
      expect(find.text('Online payment'), findsOneWidget);
      await tester.tap(find.textContaining('Pay now'));
      await tester.pumpAndSettle();
      expect(find.text('Payment'), findsWidgets);
    });

    testWidgets('a paid order that was cancelled shows that the refund is on its way', (tester) async {
      final shop = await _open(tester);
      await _tapText(tester, 'Pay online');
      await _tapPrimary(tester);
      final at = shop.orders.orders.indexWhere((o) => o.id == 'id-ORD-2000');
      shop.orders.orders[at] = shop.orders.orders[at].withPayment(stage: OrderStage.cancelled, state: PaymentState.refunding);
      await shop.container.read(ordersProvider.notifier).refresh();
      GoRouter.of(tester.element(find.byType(Scaffold).first)).go('/orders');
      await tester.pumpAndSettle();
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/order/id-ORD-2000');
      await tester.pumpAndSettle();

      expect(find.text('Your refund is on its way to your original payment method.'), findsOneWidget);
    });
  });
}
