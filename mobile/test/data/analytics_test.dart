import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/analytics.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/widgets.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/coupon_repository.dart';
import 'package:quickcart_customer/data/repositories/delivery_slots_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
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

/// Collects what would be sent, and can be told to fail.
class FakeSink implements AnalyticsSink {
  final batches = <({String session, List<AnalyticsEvent> events})>[];
  Object? failNext;

  List<AnalyticsEvent> get all => [for (final b in batches) ...b.events];
  List<String> get names => [for (final e in all) e.name];

  @override
  Future<void> send(String sessionId, List<AnalyticsEvent> events) async {
    if (failNext case final failure?) {
      failNext = null;
      throw failure;
    }
    batches.add((session: sessionId, events: List.of(events)));
  }
}

class _Coupons implements CouponRepository {
  @override
  Future<CouponPreview> preview({required String storeId, required String code}) async => switch (code) {
        'SAVE10' => CouponPreview(valid: true, code: code, discount: 2.8, message: 'Code applied.', total: 55.2),
        _ => CouponPreview(valid: false, code: code, discount: 0, message: 'Add items worth more.', total: 58, reason: 'BelowMinimum'),
      };
}

class _Slots implements DeliverySlotsRepository {
  @override
  Future<DeliverySlots> list(String storeId) async {
    final start = DateTime.now().toUtc().add(const Duration(days: 1));
    final slot = DeliverySlot(start: start, end: start.add(const Duration(hours: 1)), day: 'd', dayLabel: 'Tomorrow', timeLabel: '9 am - 10 am', available: true, left: 3);
    return DeliverySlots(enabled: true, asapAllowed: true, windows: [slot]);
  }
}

class _Shop {
  _Shop(this.container, this.sink, this.analytics, this.orders);
  final ProviderContainer container;
  final FakeSink sink;
  final Analytics analytics;
  final FakeOrderRepository orders;

  /// What has been recorded so far, flushed.
  Future<List<String>> names() async {
    await analytics.flush();
    return sink.names;
  }
}

Future<_Shop> _open(WidgetTester tester, {String? route}) async {
  SharedPreferences.setMockInitialValues(const {});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2600 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final cartRepo = FakeCartRepository()..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28);
  final orders = FakeOrderRepository()..onPlaced = cartRepo.lines.clear;
  final sink = FakeSink();
  final analytics = Analytics(sink, batchSize: 1000, flushAfter: const Duration(hours: 1));
  final addresses = FakeAddressRepository()..seed('Home', isDefault: true, flat: 'Flat 4');
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(const AuthState(AuthStatus.signedIn, _asha))),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cartRepo),
    orderRepositoryProvider.overrideWithValue(orders),
    couponRepositoryProvider.overrideWithValue(_Coupons()),
    deliverySlotsRepositoryProvider.overrideWithValue(_Slots()),
    analyticsProvider.overrideWithValue(analytics),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
  if (route != null) {
    container.read(cartProvider.notifier).add('tomato:1kg', 1);
    await tester.pumpAndSettle();
    GoRouter.of(tester.element(find.byType(Scaffold).first)).push(route);
    await tester.pumpAndSettle();
  }
  return _Shop(container, sink, analytics, orders);
}

void main() {
  group('the tracker', () {
    test('steps are queued and sent together, with one session id for all of them', () async {
      final sink = FakeSink();
      final a = Analytics(sink, flushAfter: const Duration(hours: 1));

      a.track(Track.appOpen);
      a.track(Track.search, detail: 'milk');
      expect(sink.batches, isEmpty); // nothing is sent at the moment of the step
      await a.flush();

      expect(sink.batches, hasLength(1));
      expect(sink.names, ['app_open', 'search']);
      expect(sink.all[1].detail, 'milk');
      expect(sink.batches.single.session, a.sessionId);
      expect(RegExp(r'^[a-f0-9]{24}$').hasMatch(a.sessionId), isTrue);
      expect(Analytics(sink).sessionId, isNot(a.sessionId)); // a new app start is a new session
    });

    test('a full batch is sent on its own, and nothing is lost or sent twice', () async {
      final sink = FakeSink();
      final a = Analytics(sink, batchSize: 3, flushAfter: const Duration(hours: 1));

      for (var i = 0; i < 7; i++) {
        a.track(Track.addToCart);
      }
      await Future<void>.delayed(const Duration(milliseconds: 20));
      await a.flush();

      expect(sink.all, hasLength(7));
    });

    test('a short wait sends what is queued without anyone asking', () async {
      final sink = FakeSink();
      final a = Analytics(sink, flushAfter: const Duration(milliseconds: 30));

      a.track(Track.cartOpen);
      await Future<void>.delayed(const Duration(milliseconds: 120));

      expect(sink.names, ['cart_open']);
    });

    test('a batch never has more than 25 steps', () async {
      final sink = FakeSink();
      final a = Analytics(sink, batchSize: 1000, queueLimit: 1000, flushAfter: const Duration(hours: 1));

      for (var i = 0; i < 60; i++) {
        a.track(Track.addToCart);
      }
      await a.flush();

      expect(sink.batches.map((b) => b.events.length), [25, 25, 10]);
    });

    test('a failed send loses that batch but never throws, and the next batch still goes', () async {
      final sink = FakeSink()..failNext = const NetworkFailure();
      final a = Analytics(sink, flushAfter: const Duration(hours: 1));

      a.track(Track.appOpen);
      await a.flush();
      a.track(Track.cartOpen);
      await a.flush();

      expect(sink.names, ['cart_open']);
    });

    test('the queue is capped, dropping the oldest, so an offline phone does not grow without limit', () async {
      final sink = FakeSink();
      final a = Analytics(sink, batchSize: 1000, queueLimit: 5, flushAfter: const Duration(hours: 1));

      for (var i = 0; i < 8; i++) {
        a.track(Track.search, detail: 'term $i');
      }
      await a.flush();

      expect(sink.all.map((e) => e.detail), ['term 3', 'term 4', 'term 5', 'term 6', 'term 7']);
    });

    test('the current store is added to a step unless the step names one', () async {
      final sink = FakeSink();
      final a = Analytics(sink, storeId: () => 'store-1', flushAfter: const Duration(hours: 1));

      a.track(Track.productView);
      a.track(Track.storeChosen, store: 'store-2');
      await a.flush();

      expect(sink.all.map((e) => e.storeId), ['store-1', 'store-2']);
    });

    test('a step is sent as the small JSON the server expects, with no empty fields', () {
      expect(const AnalyticsEvent('app_open').toJson(), {'name': 'app_open'});
      expect(const AnalyticsEvent('search', detail: 'milk', storeId: 's1').toJson(), {'name': 'search', 'detail': 'milk', 'storeId': 's1'});
    });

    test('flushing with nothing queued sends nothing', () async {
      final sink = FakeSink();
      await Analytics(sink).flush();
      expect(sink.batches, isEmpty);
    });

    test('the step names are the ones the server accepts', () {
      const serverNames = {'app_open', 'search', 'product_view', 'add_to_cart', 'cart_open', 'checkout_start', 'coupon_applied', 'coupon_refused', 'slot_chosen', 'order_placed', 'store_chosen', 'out_of_service_area'};
      const ours = [Track.appOpen, Track.search, Track.productView, Track.addToCart, Track.cartOpen, Track.checkoutStart, Track.couponApplied, Track.couponRefused, Track.slotChosen, Track.orderPlaced, Track.storeChosen, Track.outOfServiceArea];
      expect(ours.toSet(), serverNames);
    });

    test('a search term is lower case, short, and never anything that looks like an email or a phone number', () {
      expect(Analytics.safeTerm('  Fresh   MILK '), 'fresh milk');
      expect(Analytics.safeTerm('a'), isNull);
      expect(Analytics.safeTerm(''), isNull);
      expect(Analytics.safeTerm(null), isNull);
      expect(Analytics.safeTerm('asha@example.com'), isNull);
      expect(Analytics.safeTerm('98765 43210'), isNull);
      expect(Analytics.safeTerm('9 8 7 6 5 4 3'), isNull);
      expect(Analytics.safeTerm('atta 5 kg'), 'atta 5 kg');
      expect(Analytics.safeTerm('x' * 60)!.length, 40);
    });

    test('the API sink posts the session and the steps to the anonymous route', () async {
      final adapter = FakeAdapter()..replyJson({'success': true, 'data': 2}, status: 202);
      final client = ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

      await ApiAnalyticsSink(client).send('abcdef0123456789abcdef01', const [AnalyticsEvent('app_open'), AnalyticsEvent('search', detail: 'milk')]);

      expect(adapter.requests.single.path, '/api/analytics/events');
      expect(adapter.requests.single.data, {
        'sessionId': 'abcdef0123456789abcdef01',
        'events': [
          {'name': 'app_open'},
          {'name': 'search', 'detail': 'milk'}
        ]
      });
    });

    test('offline demo data records nothing', () async {
      await NoAnalyticsSink().send('x', const [AnalyticsEvent('app_open')]);
    });
  });

  group('what the screens record', () {
    testWidgets('opening the app is the first step', (tester) async {
      final shop = await _open(tester);
      expect(await shop.names(), ['app_open']);
    });

    testWidgets('adding to the cart, opening it, opening checkout and placing the order are recorded in order, with how the order was placed', (tester) async {
      final shop = await _open(tester, route: '/cart');
      expect(await shop.names(), ['app_open', 'add_to_cart', 'cart_open']);

      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/checkout');
      await tester.pumpAndSettle();
      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();

      final names = await shop.names();
      expect(names.sublist(3), ['checkout_start', 'order_placed']);
      expect(shop.sink.all.last.detail, 'cash');
    });

    testWidgets('a good code and a refused one are counted, a refusal by its kind and never by the code typed', (tester) async {
      final shop = await _open(tester, route: '/cart');

      await tester.enterText(find.byType(TextField).first, 'save10');
      await tester.tap(find.text('Apply'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Remove'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField).first, 'whatever');
      await tester.tap(find.text('Apply'));
      await tester.pumpAndSettle();

      await shop.analytics.flush();
      final coupon = shop.sink.all.where((e) => e.name.startsWith('coupon')).toList();
      expect(coupon.map((e) => e.name), ['coupon_applied', 'coupon_refused']);
      expect(coupon[1].detail, 'belowminimum');
      expect(shop.sink.all.map((e) => e.detail ?? ''), isNot(contains(anyOf('SAVE10', 'save10', 'WHATEVER', 'whatever'))));
    });

    testWidgets('choosing a delivery time is recorded as a window or as soon as possible, with the order saying it was scheduled', (tester) async {
      final shop = await _open(tester, route: '/checkout');

      await tester.tap(find.text('9 am - 10 am'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('As soon as possible'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('9 am - 10 am'));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();

      await shop.analytics.flush();
      final steps = shop.sink.all;
      expect(steps.where((e) => e.name == 'slot_chosen').map((e) => e.detail), ['window', 'asap', 'window']);
      expect(steps.last.name, 'order_placed');
      expect(steps.last.detail, 'cash+slot');
    });

    testWidgets('an order refused because the address is outside the service area is counted', (tester) async {
      final shop = await _open(tester, route: '/checkout');
      shop.orders.failures.add(const ConflictException('This store does not deliver to the delivery address.', statusCode: 409, reason: 'OutsideServiceArea'));

      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();

      expect(await shop.names(), contains('out_of_service_area'));
      expect(shop.sink.names, isNot(contains('order_placed')));
    });

    testWidgets('nothing recorded carries more than a few safe words', (tester) async {
      final shop = await _open(tester, route: '/checkout');
      await tester.tap(find.byType(PillButton).last);
      await tester.pumpAndSettle();
      await shop.analytics.flush();

      for (final e in shop.sink.all) {
        expect(e.detail == null || RegExp(r'^[a-z0-9 +_.-]{1,40}$').hasMatch(e.detail!), isTrue, reason: '${e.name}: ${e.detail}');
      }
    });
  });
}

/// A failure the sink can throw without any network.
class NetworkFailure implements Exception {
  const NetworkFailure();
}
