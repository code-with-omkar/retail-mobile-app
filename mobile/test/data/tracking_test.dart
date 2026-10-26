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
}
