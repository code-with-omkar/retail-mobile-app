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
import 'package:quickcart_customer/data/repositories/delivery_slots_repository.dart';
import 'package:quickcart_customer/data/repositories/local_shop.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:quickcart_customer/features/checkout/delivery_time.dart';
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
      'totalAmount': 58,
      'status': 0,
      'deliveryAddress': '12 Marine Drive',
      'createdAt': '2026-10-09T10:00:00Z',
      'items': [
        {'productId': 'p1', 'productNameSnapshot': 'Tomato', 'unitPrice': 28, 'quantity': 1, 'totalPrice': 28, 'variantId': 'v1', 'variantLabel': '1 kg'}
      ],
      'subtotalAmount': 28,
      'deliveryFee': 25,
      'handlingFee': 5,
      ...extra,
    };

/// Three windows tomorrow at 9, 10 and 11 am India time (the 10 am one full), always in the future whatever day the test runs.
DeliverySlots _slots({bool enabled = true, bool asap = true}) {
  final now = DateTime.now().toUtc();
  final indiaNow = now.add(const Duration(hours: 5, minutes: 30));
  final midnightUtc = DateTime.utc(indiaNow.year, indiaNow.month, indiaNow.day).subtract(const Duration(hours: 5, minutes: 30)).add(const Duration(days: 1));
  DeliverySlot at(int hour, {bool available = true}) {
    final start = midnightUtc.add(Duration(hours: hour));
    final end = start.add(const Duration(hours: 1));
    final t = slotText(start, end, now);
    return DeliverySlot(start: start, end: end, day: '${start.millisecondsSinceEpoch ~/ 86400000}', dayLabel: t.day, timeLabel: t.time, available: available, left: available ? 3 : 0);
  }

  return DeliverySlots(enabled: enabled, asapAllowed: asap, windows: enabled ? [at(9), at(10, available: false), at(11)] : const []);
}

class FakeSlotsRepository implements DeliverySlotsRepository {
  DeliverySlots slots = _slots();
  var calls = 0;

  @override
  Future<DeliverySlots> list(String storeId) async {
    calls++;
    return slots;
  }
}

class _Shop {
  _Shop(this.container, this.slots, this.orders);
  final ProviderContainer container;
  final FakeSlotsRepository slots;
  final FakeOrderRepository orders;
}

Future<_Shop> _open(WidgetTester tester, {DeliverySlots? slots}) async {
  SharedPreferences.setMockInitialValues(const {});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2600 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final cartRepo = FakeCartRepository()..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28);
  final orders = FakeOrderRepository()..onPlaced = cartRepo.lines.clear;
  final slotsRepo = FakeSlotsRepository();
  if (slots != null) slotsRepo.slots = slots;
  final addresses = FakeAddressRepository()..seed('Home', isDefault: true, flat: 'Flat 4');
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(const AuthState(AuthStatus.signedIn, _asha))),
    addressRepositoryProvider.overrideWithValue(addresses),
    cartRepositoryProvider.overrideWithValue(cartRepo),
    orderRepositoryProvider.overrideWithValue(orders),
    deliverySlotsRepositoryProvider.overrideWithValue(slotsRepo),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
  container.read(cartProvider.notifier).add('tomato:1kg', 1);
  await tester.pumpAndSettle();
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/checkout');
  await tester.pumpAndSettle();
  return _Shop(container, slotsRepo, orders);
}

Future<void> _place(WidgetTester tester) async {
  await tester.tap(find.byType(PillButton).last);
  await tester.pumpAndSettle();
}

void main() {
  group('words for a window', () {
    // 10:00 India time on Mon 2 Nov 2026 is 04:30 UTC.
    final now = DateTime.utc(2026, 11, 2, 4, 30);
    DateTime india(int day, int hour, [int minute = 0]) => DateTime.utc(2026, 11, day, hour, minute).subtract(const Duration(hours: 5, minutes: 30));

    test('today, tomorrow and a later date, in India time', () {
      expect(slotText(india(2, 14), india(2, 15), now), (day: 'Today', time: '2 pm - 3 pm'));
      expect(slotText(india(3, 9), india(3, 10, 30), now), (day: 'Tomorrow', time: '9 am - 10:30 am'));
      expect(slotText(india(4, 12), india(4, 13), now), (day: 'Wed 4 Nov', time: '12 pm - 1 pm'));
      expect(slotText(india(2, 0), india(2, 1), now).time, '12 am - 1 am');
    });

    test('just after midnight in India is already the next day even though it is still the evening in UTC', () {
      final late = DateTime.utc(2026, 11, 2, 19, 0); // 00:30 on the 3rd in India
      expect(slotText(india(3, 9), india(3, 10), late).day, 'Today');
    });
  });

  group('reading and sending delivery times', () {
    test('the list of windows is read with its day groups, and a full window is not available', () {
      final slots = DeliverySlots.fromJson({
        'enabled': true,
        'asapAllowed': false,
        'windows': [
          {'startUtc': '2026-11-02T08:30:00Z', 'endUtc': '2026-11-02T09:30:00Z', 'day': '2026-11-02', 'dayLabel': 'Today', 'timeLabel': '2 pm - 3 pm', 'available': true, 'left': 2},
          {'startUtc': '2026-11-03T03:30:00Z', 'endUtc': '2026-11-03T04:30:00Z', 'day': '2026-11-03', 'dayLabel': 'Tomorrow', 'timeLabel': '9 am - 10 am', 'available': false, 'left': 0},
        ],
      });

      expect((slots.enabled, slots.asapAllowed), (true, false));
      expect(slots.byDay.keys, ['2026-11-02', '2026-11-03']);
      expect(slots.windows[0].start, DateTime.utc(2026, 11, 2, 8, 30));
      expect(slots.windows[1].available, isFalse);
      expect(DeliverySlots.fromJson({'enabled': false}).windows, isEmpty);
    });

    test('the windows are read from the store\'s route', () async {
      final adapter = FakeAdapter()..replyJson({'success': true, 'data': {'enabled': true, 'asapAllowed': true, 'windows': []}});

      final slots = await ApiDeliverySlotsRepository(_client(adapter)).list('s1');

      expect(adapter.requests.single.path, '/api/stores/s1/delivery-slots');
      expect(slots.enabled, isTrue);
    });

    test('an order carries its window; an older answer has none', () {
      final scheduled = orderFromJson(_order(extra: {'deliverySlotStart': '2026-11-02T08:30:00Z', 'deliverySlotEnd': '2026-11-02T09:30:00Z'}));
      expect(scheduled.isScheduled, isTrue);
      expect(scheduled.deliverySlotStart, DateTime.utc(2026, 11, 2, 8, 30));
      expect(scheduled.withStage(OrderStage.cancelled).deliverySlotEnd, DateTime.utc(2026, 11, 2, 9, 30)); // copies keep it
      expect(orderFromJson(_order()).isScheduled, isFalse);
    });

    test('the order sends the chosen start in UTC, and nothing for as soon as possible', () async {
      final adapter = FakeAdapter()
        ..replyJson({'success': true, 'data': _order()}, status: 201)
        ..replyJson({'success': true, 'data': _order()}, status: 201);
      final repo = ApiOrderRepository(_client(adapter));

      await repo.place(storeId: 's1', idempotencyKey: 'k1', addressId: 'a1', deliverySlotStart: DateTime.utc(2026, 11, 3, 3, 30));
      await repo.place(storeId: 's1', idempotencyKey: 'k2', addressId: 'a1');

      expect(adapter.requests[0].data, {'addressId': 'a1', 'deliverySlotStart': '2026-11-03T03:30:00.000Z', 'outOfStockChoice': 'CallMe'});
      expect(adapter.requests[1].data, {'addressId': 'a1', 'outOfStockChoice': 'CallMe'});
    });

    test('offline demo data has no scheduled delivery and refuses a chosen time', () async {
      expect((await LocalDeliverySlotsRepository().list('s1')).enabled, isFalse);
      await expectLater(LocalOrderRepository(LocalShop()).place(storeId: 's1', idempotencyKey: 'k', addressId: 'a', deliverySlotStart: DateTime.utc(2026, 11, 3)), throwsA(isA<ConflictException>()));
    });
  });

  group('choosing a delivery time at checkout', () {
    testWidgets('a store that does not schedule shows no choice and the order is placed for now', (tester) async {
      final shop = await _open(tester, slots: DeliverySlots.off);

      expect(find.text('Delivery time'), findsNothing);
      await _place(tester);

      expect(shop.orders.slots, [null]);
    });

    testWidgets('the windows show by day, a full one is disabled, and as soon as possible is the default', (tester) async {
      final shop = await _open(tester);
      final windows = shop.slots.slots.windows;

      expect(find.text('Delivery time'), findsOneWidget);
      expect(find.text('As soon as possible'), findsOneWidget);
      expect(find.text('Tomorrow'), findsOneWidget);
      expect(find.text(windows[0].timeLabel), findsOneWidget);
      expect(find.text('${windows[1].timeLabel} · Full'), findsOneWidget);
      expect(shop.container.read(chosenSlotProvider), isNull);

      await tester.tap(find.text('${windows[1].timeLabel} · Full'));
      await tester.pumpAndSettle();
      expect(shop.container.read(chosenSlotProvider), isNull); // a full window cannot be chosen
    });

    testWidgets('choosing a window sends its start with the order and shows it on the green card', (tester) async {
      final shop = await _open(tester);
      final w = shop.slots.slots.windows[2];

      await tester.tap(find.text(w.timeLabel));
      await tester.pumpAndSettle();

      expect(shop.container.read(chosenSlotProvider), w.start);
      expect(find.textContaining('Delivery: Tomorrow, ${w.timeLabel}'), findsOneWidget);

      await _place(tester);

      expect(shop.orders.slots, [w.start]);
      expect(shop.container.read(chosenSlotProvider), isNull); // cleared for the next order
    });

    testWidgets('going back to as soon as possible sends no window', (tester) async {
      final shop = await _open(tester);
      await tester.tap(find.text(shop.slots.slots.windows[0].timeLabel));
      await tester.pumpAndSettle();

      await tester.tap(find.text('As soon as possible'));
      await tester.pumpAndSettle();
      await _place(tester);

      expect(shop.orders.slots, [null]);
    });

    testWidgets('when as soon as possible is off, the order cannot be placed until a time is chosen', (tester) async {
      final shop = await _open(tester, slots: _slots(asap: false));

      expect(find.text('As soon as possible'), findsNothing);
      expect(find.text('Choose a delivery time to continue.'), findsOneWidget);
      await _place(tester);
      expect(shop.orders.attempts, isEmpty);

      await tester.tap(find.text(shop.slots.slots.windows[0].timeLabel));
      await tester.pumpAndSettle();
      await _place(tester);
      expect(shop.orders.attempts, hasLength(1));
    });

    testWidgets('a window the server turns down is dropped, the offers are read again and the customer is told', (tester) async {
      final shop = await _open(tester);
      final w = shop.slots.slots.windows[0];
      await tester.tap(find.text(w.timeLabel));
      await tester.pumpAndSettle();
      final readsBefore = shop.slots.calls;
      shop.orders.failures.add(const ConflictException('That delivery time is full. Choose another.', statusCode: 409, reason: CheckoutReasons.slotUnavailable));

      await _place(tester);

      expect(find.textContaining('That delivery time is no longer available'), findsOneWidget);
      expect(shop.container.read(chosenSlotProvider), isNull);
      expect(shop.slots.calls, greaterThan(readsBefore));
    });

    testWidgets('a chosen window that fills up while the customer looks is dropped from the choice', (tester) async {
      final shop = await _open(tester);
      final w = shop.slots.slots.windows[0];
      await tester.tap(find.text(w.timeLabel));
      await tester.pumpAndSettle();
      expect(shop.container.read(chosenSlotProvider), w.start);

      final full = shop.slots.slots;
      shop.slots.slots = DeliverySlots(enabled: true, asapAllowed: true, windows: [for (final x in full.windows) DeliverySlot(start: x.start, end: x.end, day: x.day, dayLabel: x.dayLabel, timeLabel: x.timeLabel, available: x.start != w.start && x.available, left: 0)]);
      shop.container.invalidate(deliverySlotsProvider);
      await tester.pumpAndSettle();

      expect(shop.container.read(chosenSlotProvider), isNull);
    });
  });
}
