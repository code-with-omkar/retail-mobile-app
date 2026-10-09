import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/dto/order_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/local_shop.dart';
import 'package:quickcart_customer/data/repositories/notification_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/notifications/notifications_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';
import '../support/location_fakes.dart';

class _Auth extends AuthNotifier {
  @override
  AuthState build() => const AuthState(AuthStatus.signedIn, CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test'));
}

Map<String, dynamic> _json({String type = 'OrderAccepted', String? category = 'Order', String title = 'Order accepted'}) => {
      'id': 'n1',
      'orderId': 'o1',
      'type': type,
      'title': title,
      'message': 'The store accepted your order KHG-261009-0001.',
      'isRead': false,
      'createdAt': '2026-10-09T10:05:00Z',
      'category': ?category,
    };

AppNotification _note(String type, String category, {String title = 't'}) => AppNotification(id: type, type: type, title: title, message: 'm', isRead: false, createdAt: DateTime(2026, 10, 9), category: category);

ApiClient _client(FakeAdapter adapter) => ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

Future<(ProviderContainer, FakeNotificationRepository)> _open(WidgetTester tester, {List<AppNotification> notifications = const [], String language = 'en', bool offersOn = true}) async {
  SharedPreferences.setMockInitialValues({'language': language});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 1800 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final notes = FakeNotificationRepository()
    ..items.addAll(notifications)
    ..offers = offersOn;
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(_Auth.new),
    addressRepositoryProvider.overrideWithValue(FakeAddressRepository()..seed('Home', isDefault: true)),
    cartRepositoryProvider.overrideWithValue(FakeCartRepository()),
    orderRepositoryProvider.overrideWithValue(FakeOrderRepository()),
    notificationRepositoryProvider.overrideWithValue(notes),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/notifications');
  await tester.pumpAndSettle();
  return (ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))), notes);
}

void main() {
  group('reading notifications from the server', () {
    test('the category comes with each notification, and an older answer without one is an order notification', () {
      expect(notificationFromJson(_json(type: 'Offer', category: 'Offer')).category, 'Offer');
      expect(notificationFromJson(_json(category: null)).category, 'Order');
    });

    test('the list asks for the customer language, and sends none when none is given', () async {
      final adapter = FakeAdapter()
        ..replyJson({'success': true, 'data': [_json()]})
        ..replyJson({'success': true, 'data': <dynamic>[]});
      final repo = ApiNotificationRepository(_client(adapter));

      await repo.list(language: 'mr');
      await repo.list();

      expect(adapter.requests[0].queryParameters, {'lang': 'mr'});
      expect(adapter.requests[1].queryParameters, isEmpty);
    });

    test('the offers choice is read and saved through the preferences route', () async {
      final adapter = FakeAdapter()
        ..replyJson({'success': true, 'data': {'offers': false}})
        ..replyJson({'success': true, 'data': {'offers': true}});
      final repo = ApiNotificationRepository(_client(adapter));

      final before = await repo.offersEnabled();
      await repo.setOffers(true);

      expect(before, isFalse);
      expect(adapter.requests.map((r) => '${r.method} ${r.path}'), ['GET /api/customer/notifications/preferences', 'PUT /api/customer/notifications/preferences']);
      expect(adapter.requests[1].data, {'offers': true});
    });

    test('without a server the choice is kept in memory and defaults to on', () async {
      final repo = LocalNotificationRepository(LocalShop());

      expect(await repo.offersEnabled(), isTrue);
      await repo.setOffers(false);
      expect(await repo.offersEnabled(), isFalse);
    });
  });

  group('icons', () {
    test('each kind of notification has its own icon', () {
      expect(notificationIcon(_note('Offer', 'Offer')), Icons.local_offer_outlined);
      expect(notificationIcon(_note('OrderAccepted', 'Order')), Icons.storefront_outlined);
      expect(notificationIcon(_note('OrderPacking', 'Order')), Icons.inventory_2_outlined);
      expect(notificationIcon(_note('OutForDelivery', 'Order')), Icons.delivery_dining_outlined);
      expect(notificationIcon(_note('OrderDelivered', 'Order')), Icons.check_circle_outline);
      expect(notificationIcon(_note('OrderRejected', 'Order')), Icons.block);
      expect(notificationIcon(_note('OrderCancelled', 'Order')), Icons.cancel_outlined);
      expect(notificationIcon(_note('PaymentReceived', 'Payment')), Icons.account_balance_wallet_outlined);
      expect(notificationIcon(_note('OrderStatusChanged', 'Order', title: 'Order cancelled')), Icons.cancel_outlined);
      expect(notificationIcon(_note('Anything', 'System')), Icons.receipt_long_outlined);
    });
  });

  group('the notifications screen', () {
    testWidgets('shows offers with an Offer label and the offers switch above the list', (tester) async {
      await _open(tester, notifications: [_note('Offer', 'Offer', title: 'Festival sale'), _note('OrderAccepted', 'Order', title: 'Order accepted')]);

      expect(find.text('Offers and announcements'), findsOneWidget);
      expect(find.text('Order and payment updates are always sent.'), findsOneWidget);
      expect(find.text('Festival sale'), findsOneWidget);
      expect(find.text('Offer'), findsOneWidget);
      expect(find.byIcon(Icons.local_offer_outlined), findsOneWidget);
    });

    testWidgets('the list is asked for in the customer language, and again in the new one after a switch', (tester) async {
      final (container, notes) = await _open(tester, notifications: [_note('OrderAccepted', 'Order')]);
      expect(notes.lastLanguage, 'en');

      container.read(localeProvider.notifier).set(const Locale('mr'));
      await tester.pumpAndSettle();

      expect(notes.lastLanguage, 'mr');
    });

    testWidgets('switching offers off is saved, and a refusal puts the switch back with a message', (tester) async {
      final (_, notes) = await _open(tester, notifications: [_note('OrderAccepted', 'Order')]);
      expect(tester.widget<Switch>(find.byType(Switch)).value, isTrue);

      await tester.tap(find.byType(Switch));
      await tester.pumpAndSettle();
      expect(notes.offers, isFalse);
      expect(tester.widget<Switch>(find.byType(Switch)).value, isFalse);

      notes.failNext = const NetworkException('down');
      await tester.tap(find.byType(Switch));
      await tester.pumpAndSettle();
      expect(tester.widget<Switch>(find.byType(Switch)).value, isFalse, reason: 'still off: the save failed');
      expect(find.text('Could not save your choice. Please try again.'), findsOneWidget);
    });

    testWidgets('a returning customer sees their saved choice', (tester) async {
      await _open(tester, notifications: [_note('OrderAccepted', 'Order')], offersOn: false);

      expect(tester.widget<Switch>(find.byType(Switch)).value, isFalse);
    });
  });
}
