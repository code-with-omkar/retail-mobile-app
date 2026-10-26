import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/dto/order_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/repositories/tracking_repository.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';

class _Auth extends AuthNotifier {
  @override
  AuthState build() => const AuthState(AuthStatus.signedIn, CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test'));
}

class _Tracking implements TrackingRepository {
  _Tracking(this.answer);
  Tracking answer;
  final asked = <String>[];

  @override
  Future<Tracking> get(String orderId) async {
    asked.add(orderId);
    return answer;
  }
}

Future<_Tracking> _open(WidgetTester tester, Order order, Tracking answer) async {
  SharedPreferences.setMockInitialValues({});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2400 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final orders = FakeOrderRepository()..orders.add(order);
  final tracking = _Tracking(answer);
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(_Auth.new),
    cartRepositoryProvider.overrideWithValue(FakeCartRepository()),
    orderRepositoryProvider.overrideWithValue(orders),
    trackingRepositoryProvider.overrideWithValue(tracking),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  await tester.tap(find.byTooltip('Orders'));
  await tester.pumpAndSettle();
  await tester.tap(find.text('#${order.number}'));
  await tester.pumpAndSettle();
  return tracking;
}

void main() {
  test('the repository reads distance, ETA and the partner name', () async {
    final adapter = FakeAdapter()..replyJson({'success': true, 'data': {'available': true, 'distanceKm': 1.2, 'etaMinutes': 4, 'partnerName': 'Rina'}});
    final repo = ApiTrackingRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));

    final t = await repo.get('o1');

    expect(adapter.requests.single.path, '/api/customer/orders/o1/tracking');
    expect((t.available, t.distanceKm, t.etaMinutes, t.partnerName), (true, 1.2, 4, 'Rina'));
  });

  testWidgets('an order out for delivery shows how far the partner is', (tester) async {
    final order = sampleOrder(stage: OrderStage.onTheWay, deliveryPartnerName: 'Rina', deliveryOtp: '4821');
    await _open(tester, order, const Tracking(available: true, distanceKm: 1.2, etaMinutes: 6, partnerName: 'Rina'));

    expect(find.byKey(const Key('tracking-line')), findsOneWidget);
    expect(find.text('Rina is 1.2 km away, about 6 min'), findsOneWidget);
  });

  testWidgets('with no recent position nothing is shown', (tester) async {
    final order = sampleOrder(stage: OrderStage.onTheWay, deliveryPartnerName: 'Rina', deliveryOtp: '4821');
    await _open(tester, order, const Tracking(available: false));

    expect(find.byKey(const Key('tracking-line')), findsNothing);
    expect(find.byKey(const Key('delivery-code')), findsOneWidget); // the code card is unchanged
  });

  testWidgets('a delivered order is never tracked', (tester) async {
    final tracking = await _open(tester, sampleOrder(stage: OrderStage.delivered), const Tracking(available: true, distanceKm: 1, etaMinutes: 3));

    expect(tracking.asked, isEmpty);
    expect(find.byKey(const Key('tracking-line')), findsNothing);
  });

  group('an order the store changed', () {
    Map<String, dynamic> json(String refundStatus, {int to = 1}) => {
          'id': 'o7',
          'orderNumber': 'ORD-7',
          'storeId': 's1',
          'totalAmount': 100,
          'status': 3,
          'deliveryAddress': '12 Marine Drive',
          'createdAt': '2026-10-26T10:00:00Z',
          'items': [
            {'productId': 'p1', 'productNameSnapshot': 'Milk', 'unitPrice': 34, 'quantity': 1, 'totalPrice': 34, 'variantId': 'v1', 'variantLabel': '1 l'}
          ],
          'changes': [
            {
              'at': '2026-10-26T10:30:00Z',
              'lines': [
                {'name': 'Milk', 'label': '1 l', 'from': 3, 'to': to}
              ],
              'refundAmount': 68,
              'refundStatus': refundStatus,
              'reason': 'Out of stock'
            }
          ],
        };

    test('the changes are read from the order', () {
      final order = orderFromJson(json('Pending'));

      final change = order.changes.single;
      expect((change.lines.single.name, change.lines.single.from, change.lines.single.to, change.refund, change.refundStatus, change.reason), ('Milk', 3, 1, 68.0, 'Pending', 'Out of stock'));
      expect(orderFromJson({...json('None'), 'changes': null}).changes, isEmpty);
    });

    testWidgets('the order screen says what changed and where the refund is', (tester) async {
      await _open(tester, orderFromJson(json('Pending')), const Tracking(available: false));

      expect(find.byKey(const Key('order-change')), findsOneWidget);
      expect(find.text('Milk 1 l: 3 to 1'), findsOneWidget);
      expect(find.text('Refund of ₹68 is on its way to your original payment method'), findsOneWidget);
    });

    testWidgets('a removed item and a finished refund read plainly', (tester) async {
      await _open(tester, orderFromJson(json('Processed', to: 0)), const Tracking(available: false));

      expect(find.text('Milk 1 l removed (was 3)'), findsOneWidget);
      expect(find.text('Refund of ₹68 done'), findsOneWidget);
    });
  });
}
