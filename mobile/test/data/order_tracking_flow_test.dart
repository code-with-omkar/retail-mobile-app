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
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/phone_launcher.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/notifications/notifications_controller.dart';
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

class _App {
  _App(this.container, this.orders, this.notes, this.phone, this.clock);
  final ProviderContainer container;
  final FakeOrderRepository orders;
  final FakeNotificationRepository notes;
  final FakePhoneLauncher phone;
  final _Clock clock;
}

class _Clock {
  DateTime now = DateTime(2026, 10, 9, 18, 30);
  DateTime call() => now;
}

Future<_App> _open(
  WidgetTester tester, {
  List<Order> orders = const [],
  List<AppNotification> notifications = const [],
  bool signedIn = true,
  Map<String, Object> prefs = const {},
}) async {
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 1800 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final orderRepo = FakeOrderRepository()..orders.addAll(orders);
  final noteRepo = FakeNotificationRepository()..items.addAll(notifications);
  final phone = FakePhoneLauncher();
  final clock = _Clock();
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(signedIn ? const AuthState(AuthStatus.signedIn, _asha) : const AuthState(AuthStatus.signedOut))),
    addressRepositoryProvider.overrideWithValue(FakeAddressRepository()..seed('Home', isDefault: true)),
    cartRepositoryProvider.overrideWithValue(FakeCartRepository()),
    orderRepositoryProvider.overrideWithValue(orderRepo),
    notificationRepositoryProvider.overrideWithValue(noteRepo),
    phoneLauncherProvider.overrideWithValue(phone),
    orderClockProvider.overrideWithValue(clock.call),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  return _App(ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))), orderRepo, noteRepo, phone, clock);
}

/// Time passes (the polling timers run) and what they started finishes.
Future<void> _elapse(WidgetTester tester, int seconds) async {
  await tester.pump(Duration(seconds: seconds));
  await tester.pump(const Duration(milliseconds: 50));
}

Future<void> _goOrders(WidgetTester tester) async {
  await tester.tap(find.byTooltip('Orders'));
  await tester.pumpAndSettle();
}

Future<void> _openOrder(WidgetTester tester, String number) async {
  await tester.tap(find.text('#$number'));
  await tester.pumpAndSettle();
}

Order _active(String number, {OrderStage stage = OrderStage.placed, int? estimate, String? phone = '02249000001', DateTime? placedAt}) =>
    sampleOrder(number: number, stage: stage, estimatedMinutes: estimate, storePhone: phone, placedAt: placedAt ?? DateTime(2026, 10, 9, 18, 27));

void _setStage(_App app, String number, OrderStage stage) {
  final at = app.orders.orders.indexWhere((o) => o.number == number);
  app.orders.orders[at] = app.orders.orders[at].withStage(stage);
}

void main() {
  group('the orders list follows the shop', () {
    testWidgets('an order the shop accepts changes on the list by itself, without pulling', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      expect(find.text('Order placed'), findsOneWidget);

      _setStage(app, 'ORD-1', OrderStage.packed);
      await _elapse(tester, 30);

      expect(find.text('Packed'), findsOneWidget);
      expect(find.text('Order placed'), findsNothing);
    });

    testWidgets('active orders come first, finished ones after', (tester) async {
      await _open(tester, orders: [
        sampleOrder(number: 'DONE-1', stage: OrderStage.delivered, placedAt: DateTime(2026, 10, 9, 17, 0)),
        _active('LIVE-1'),
        sampleOrder(number: 'GONE-1', stage: OrderStage.cancelled, placedAt: DateTime(2026, 10, 9, 18, 0)),
      ]);

      await _goOrders(tester);

      double y(String n) => tester.getTopLeft(find.text('#$n')).dy;
      expect(y('LIVE-1'), lessThan(y('GONE-1')));
      expect(y('GONE-1'), lessThan(y('DONE-1')), reason: 'newest first within the finished ones');
    });

    testWidgets('when nothing is left to wait for, it stops asking', (tester) async {
      final app = await _open(tester, orders: [sampleOrder(number: 'DONE-1', stage: OrderStage.delivered), sampleOrder(number: 'GONE-1', stage: OrderStage.cancelled)]);
      await _goOrders(tester);
      final before = app.orders.listCalls;

      await _elapse(tester, 300);

      expect(app.orders.listCalls, before);
    });

    testWidgets('on another tab it does not ask, and it asks again when the customer comes back', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await tester.tap(find.byTooltip('Home'));
      await tester.pumpAndSettle();
      final before = app.orders.listCalls;

      await _elapse(tester, 120);
      expect(app.orders.listCalls, before, reason: 'Home is in front');

      _setStage(app, 'ORD-1', OrderStage.onTheWay);
      await _goOrders(tester);
      await _elapse(tester, 30);
      expect(find.text('Out for delivery'), findsOneWidget);
    });

    testWidgets('when the check fails the list stays and says it could not refresh; when it works again the note goes', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);

      app.orders.failRead = const NetworkException('offline');
      await _elapse(tester, 30);

      expect(find.text('#ORD-1'), findsOneWidget);
      expect(find.text('Could not refresh. Showing the last update.'), findsOneWidget);

      await _elapse(tester, 60); // backed off to 60 s
      expect(find.text('Could not refresh. Showing the last update.'), findsNothing);
    });

    testWidgets('after a failure it waits longer before asking again', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      final start = app.orders.listCalls;

      app.orders.failRead = const NetworkException('offline');
      await _elapse(tester, 30);
      expect(app.orders.listCalls, start + 1);
      await _elapse(tester, 30);
      expect(app.orders.listCalls, start + 1, reason: 'still waiting: the wait doubled');
      await _elapse(tester, 30);
      expect(app.orders.listCalls, start + 2);
    });
  });

  group('one order follows the shop', () {
    testWidgets('the timeline moves when the shop accepts, and it stops checking once the order is delivered', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      expect(find.byIcon(Icons.check), findsOneWidget);

      _setStage(app, 'ORD-1', OrderStage.packed);
      await _elapse(tester, 15);
      expect(find.byIcon(Icons.check), findsNWidgets(2));

      _setStage(app, 'ORD-1', OrderStage.delivered);
      await _elapse(tester, 15);
      expect(find.byIcon(Icons.check), findsNWidgets(4));
      final calls = app.orders.listCalls;
      await _elapse(tester, 120);
      expect(app.orders.listCalls, calls);
    });

    testWidgets('a declined order ends the timeline and says so', (tester) async {
      await _open(tester, orders: [_active('ORD-1', stage: OrderStage.rejected)]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');

      expect(find.text('The store declined this order.'), findsOneWidget);
      expect(find.text('Cancel order'), findsNothing);
    });

    testWidgets('a cancelled order ends the timeline and says so', (tester) async {
      await _open(tester, orders: [_active('ORD-1', stage: OrderStage.cancelled)]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');

      expect(find.text('This order was cancelled.'), findsOneWidget);
      expect(find.text('Cancelled'), findsWidgets);
      expect(find.text('Packed'), findsNothing);
      expect(find.text('Cancel order'), findsNothing);
    });
  });

  group('the store and the arrival', () {
    testWidgets('the store is named and a tap on Call store dials its number', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');

      expect(find.text('Harbor Point Dark Store'), findsOneWidget);
      await tester.tap(find.text('Call store'));
      await tester.pumpAndSettle();

      expect(app.phone.dialled, ['02249000001']);
    });

    testWidgets('if the phone app cannot open, the customer is told', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      app.phone.opens = false;
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');

      await tester.tap(find.text('Call store'));
      await tester.pumpAndSettle();

      expect(find.text('Could not open the phone app.'), findsOneWidget);
    });

    testWidgets('a store without a phone shows its name and no Call button', (tester) async {
      await _open(tester, orders: [_active('ORD-1', phone: null)]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');

      expect(find.text('Harbor Point Dark Store'), findsOneWidget);
      expect(find.text('Call store'), findsNothing);
    });

    testWidgets('an order without store details (placed before they were kept) still opens', (tester) async {
      await _open(tester, orders: [sampleOrder(number: 'OLD-1', stage: OrderStage.delivered, storeName: null, storePhone: null)]);
      await _goOrders(tester);
      await _openOrder(tester, 'OLD-1');

      expect(find.text('Order #OLD-1'), findsOneWidget);
      expect(find.text('Sold by'), findsNothing);
    });

    testWidgets('while it is on its way: arrival counted from when it was placed, then "any moment now"', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1', stage: OrderStage.packed, estimate: 15)]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      expect(find.text('Arriving in about 12 minutes'), findsOneWidget, reason: 'placed 3 minutes ago, estimate 15');

      app.clock.now = DateTime(2026, 10, 9, 18, 50);
      await _elapse(tester, 15);

      expect(find.text('Arriving any moment now'), findsOneWidget);
    });

    testWidgets('no arrival line for a finished order or one without an estimate', (tester) async {
      await _open(tester, orders: [_active('ORD-1', estimate: null), sampleOrder(number: 'DONE-1', stage: OrderStage.delivered, estimatedMinutes: 20)]);
      await _goOrders(tester);

      await _openOrder(tester, 'ORD-1');
      expect(find.textContaining('Arriving'), findsNothing);
      await tester.pageBack();
      await tester.pumpAndSettle();
      await _openOrder(tester, 'DONE-1');
      expect(find.textContaining('Arriving'), findsNothing);
    });
  });

  group('cancelling', () {
    testWidgets('only an order the shop has not accepted can be cancelled', (tester) async {
      await _open(tester, orders: [_active('LIVE-1'), _active('PACKED-1', stage: OrderStage.packed)]);
      await _goOrders(tester);

      await _openOrder(tester, 'PACKED-1');
      expect(find.text('Cancel order'), findsNothing);
      await tester.pageBack();
      await tester.pumpAndSettle();
      await _openOrder(tester, 'LIVE-1');
      await tester.ensureVisible(find.text('Cancel order'));
      expect(find.text('Cancel order'), findsOneWidget);
    });

    testWidgets('asking first: keeping the order changes nothing', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      await tester.ensureVisible(find.text('Cancel order'));
      await tester.tap(find.text('Cancel order'));
      await tester.pumpAndSettle();
      expect(find.text('Cancel this order?'), findsOneWidget);

      await tester.tap(find.text('Keep order'));
      await tester.pumpAndSettle();

      expect(app.orders.cancelled, isEmpty);
      expect(find.text('Cancel this order?'), findsNothing);
      expect(find.text('Order placed'), findsWidgets);
    });

    testWidgets('confirming cancels it, the screen says so, the timeline ends, and the bell shows the notification', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      app.notes.items.add(sampleNotification(title: 'Order cancelled', message: 'Your order was cancelled.', orderId: 'id-ORD-1'));
      await tester.ensureVisible(find.text('Cancel order'));
      await tester.tap(find.text('Cancel order'));
      await tester.pumpAndSettle();

      await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Cancel order')));
      await tester.pumpAndSettle();

      expect(app.orders.cancelled, ['id-ORD-1']);
      expect(find.text('Your order was cancelled.'), findsOneWidget, reason: 'the message');
      expect(find.text('This order was cancelled.'), findsOneWidget);
      expect(find.text('Cancel order'), findsNothing);
      expect(app.container.read(unreadCountProvider).value, 1);
    });

    testWidgets('too late: the customer is told, sees where the order is now, and is pointed to the store', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      _setStage(app, 'ORD-1', OrderStage.packed);
      app.orders.failCancel = const ConflictException('too late', statusCode: 409, reason: 'OrderNotCancellable');
      await tester.ensureVisible(find.text('Cancel order'));
      await tester.tap(find.text('Cancel order'));
      await tester.pumpAndSettle();

      await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Cancel order')));
      await tester.pumpAndSettle();

      expect(find.text('The store has already accepted your order, so it can no longer be cancelled here. Please call the store.'), findsOneWidget);
      expect(find.text('Cancel order'), findsNothing, reason: 'the order moved on, so the button is gone');
      expect(find.text('Call store'), findsOneWidget);
    });

    testWidgets('a connection problem is explained and the order stays cancellable', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      app.orders.failCancel = const NetworkException('offline');
      await tester.ensureVisible(find.text('Cancel order'));
      await tester.tap(find.text('Cancel order'));
      await tester.pumpAndSettle();

      await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Cancel order')));
      await tester.pumpAndSettle();

      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
      expect(find.text('Cancel order'), findsOneWidget);
    });

    testWidgets('a double tap cancels once', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1')]);
      app.orders.delay = const Duration(milliseconds: 300);
      await _goOrders(tester);
      await _openOrder(tester, 'ORD-1');
      await tester.ensureVisible(find.text('Cancel order'));
      await tester.tap(find.text('Cancel order'));
      await tester.pumpAndSettle();

      await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Cancel order')));
      await tester.pump(const Duration(milliseconds: 50));
      expect(find.text('Cancelling…'), findsOneWidget);
      await tester.tap(find.text('Cancelling…'), warnIfMissed: false);
      await tester.pumpAndSettle();

      expect(app.orders.cancelled, hasLength(1));
    });
  });

  group('the success screen', () {
    testWidgets('shows the estimate the order was given, not the store estimate of the moment', (tester) async {
      final app = await _open(tester);
      final order = _active('ORD-9', estimate: 23);
      app.container.read(lastPlacedOrderProvider.notifier).set(order);
      GoRouter.of(tester.element(find.byType(Scaffold).first)).go('/order-success/${order.id}');
      await tester.pumpAndSettle();

      expect(find.textContaining('Expected in about 23 minutes'), findsOneWidget);
    });
  });

  group('the bell', () {
    testWidgets('shows how many notifications are unread, and nothing when there are none', (tester) async {
      await _open(tester, notifications: [sampleNotification(id: 'a'), sampleNotification(id: 'b'), sampleNotification(id: 'c', read: true)]);

      expect(find.text('2'), findsOneWidget);
    });

    testWidgets('no number when everything is read, and "9+" for a lot', (tester) async {
      await _open(tester, notifications: [sampleNotification(read: true)]);
      expect(find.text('0'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      await _open(tester, notifications: [for (var i = 0; i < 12; i++) sampleNotification(id: 'n$i')]);
      expect(find.text('9+'), findsOneWidget);
    });

    testWidgets('a guest has no number and no check is made', (tester) async {
      final app = await _open(tester, signedIn: false, notifications: [sampleNotification()]);

      await _elapse(tester, 180);

      expect(find.text('1'), findsNothing);
      expect(app.notes.calls, isEmpty);
    });

    testWidgets('a new notification shows on the bell within a minute, without any tap', (tester) async {
      final app = await _open(tester);
      expect(find.text('1'), findsNothing);

      app.notes.items.add(sampleNotification());
      await _elapse(tester, 60);

      expect(find.text('1'), findsOneWidget);
    });

    testWidgets('only while Home is in front: another tab does not ask', (tester) async {
      final app = await _open(tester);
      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();
      final before = app.notes.calls.length;

      await _elapse(tester, 180);

      expect(app.notes.calls.length, before);
    });

    testWidgets('a failed check keeps the number', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification()]);
      app.notes.failNext = const NetworkException('offline');

      await _elapse(tester, 60);

      expect(find.text('1'), findsOneWidget);
    });
  });

  group('the notifications screen', () {
    Future<void> openBell(WidgetTester tester) async {
      await tester.tap(find.byIcon(Icons.notifications_none_rounded).first);
      await tester.pumpAndSettle();
    }

    testWidgets('lists them newest first in the customer\'s language, with how long ago and which are unread', (tester) async {
      await _open(tester, notifications: [
        sampleNotification(id: 'old', message: 'Your order is now Preparing.', at: DateTime.now().subtract(const Duration(hours: 5)), read: true),
        sampleNotification(id: 'new', message: 'Your order is now Accepted.', at: DateTime.now().subtract(const Duration(minutes: 5))),
      ]);

      await openBell(tester);

      expect(find.text('Your order is now accepted by the store.'), findsOneWidget);
      expect(find.text('Your order is now being prepared.'), findsOneWidget);
      expect(find.text('5 min ago'), findsOneWidget);
      expect(find.text('5 h ago'), findsOneWidget);
      double y(String t) => tester.getTopLeft(find.text(t)).dy;
      expect(y('Your order is now accepted by the store.'), lessThan(y('Your order is now being prepared.')));
    });

    testWidgets('in Marathi the known messages are translated', (tester) async {
      await _open(tester, prefs: {'language': 'mr'}, notifications: [sampleNotification(message: 'Your order is now Accepted.'), sampleNotification(id: 'c', title: 'Order cancelled', message: 'Your order was cancelled.')]);

      await openBell(tester);

      expect(find.text('तुमची ऑर्डर आता दुकानाने स्वीकारली आहे.'), findsOneWidget);
      expect(find.text('तुमची ऑर्डर रद्द करण्यात आली.'), findsOneWidget);
    });

    testWidgets('a message the app does not know is shown as the server wrote it', (tester) async {
      await _open(tester, notifications: [sampleNotification(title: 'Something new', message: 'A message we have no translation for.')]);

      await openBell(tester);

      expect(find.text('A message we have no translation for.'), findsOneWidget);
    });

    testWidgets('tapping one marks it read, lowers the number on the bell, and opens its order', (tester) async {
      final app = await _open(tester, orders: [_active('ORD-1001')], notifications: [sampleNotification(id: 'n1', orderId: 'id-ORD-1001')]);
      await openBell(tester);

      await tester.tap(find.text('Your order is now accepted by the store.'));
      await tester.pumpAndSettle();

      expect(app.notes.calls, contains('read n1'));
      expect(app.container.read(unreadCountProvider).value, 0);
      expect(find.text('Order #ORD-1001'), findsOneWidget);
    });

    testWidgets('a notification without an order is only marked read', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification(id: 'n1', orderId: null)]);
      await openBell(tester);

      await tester.tap(find.text('Your order is now accepted by the store.'));
      await tester.pumpAndSettle();

      expect(app.notes.calls, contains('read n1'));
      expect(find.text('Notifications'), findsWidgets);
    });

    testWidgets('Mark all as read clears every unread one and the button goes', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification(id: 'a'), sampleNotification(id: 'b')]);
      await openBell(tester);

      await tester.tap(find.text('Mark all as read'));
      await tester.pumpAndSettle();

      expect(app.notes.calls, contains('read-all'));
      expect(find.text('Mark all as read'), findsNothing);
      expect(app.container.read(unreadCountProvider).value, 0);
    });

    testWidgets('no button when everything is already read', (tester) async {
      await _open(tester, notifications: [sampleNotification(read: true)]);

      await openBell(tester);

      expect(find.text('Mark all as read'), findsNothing);
    });

    testWidgets('if the server refuses to mark it read the list shows the truth again', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification(id: 'n1', orderId: null)]);
      await openBell(tester);
      app.notes.failNext = const NetworkException('offline');

      await tester.tap(find.text('Your order is now accepted by the store.'));
      await tester.pumpAndSettle();

      expect(app.container.read(notificationsProvider).value!.single.isRead, isFalse);
    });

    testWidgets('none yet, a failed read with Retry, and a guest asked to sign in', (tester) async {
      var app = await _open(tester);
      await openBell(tester);
      expect(find.text('No notifications yet'), findsOneWidget);

      await tester.pumpWidget(const SizedBox());
      app = await _open(tester, notifications: [sampleNotification()]);
      app.notes.failNext = const NetworkException('offline');
      app.notes.failTimes = 2; // the first read and the quiet check right after it
      await tester.tap(find.byTooltip('Notifications'));
      await tester.pumpAndSettle();
      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
      await tester.tap(find.text('Retry'));
      await tester.pumpAndSettle();
      expect(find.text('Your order is now accepted by the store.'), findsOneWidget);

      await tester.pumpWidget(const SizedBox());
      await _open(tester, signedIn: false);
      await tester.tap(find.byTooltip('Notifications'));
      await tester.pumpAndSettle();
      expect(find.text('Sign in to see your notifications'), findsOneWidget);
    });

    testWidgets('a list seen empty earlier is read again when the screen is opened, so what arrived since shows', (tester) async {
      final app = await _open(tester);
      await openBell(tester);
      expect(find.text('No notifications yet'), findsOneWidget);
      await tester.pageBack();
      await tester.pumpAndSettle();

      app.notes.items.add(sampleNotification());
      await openBell(tester);

      expect(find.text('Your order is now accepted by the store.'), findsOneWidget);
      expect(find.text('No notifications yet'), findsNothing);
    });

    testWidgets('one that arrives while the screen is open shows within half a minute, without pulling', (tester) async {
      final app = await _open(tester);
      await openBell(tester);
      expect(find.text('No notifications yet'), findsOneWidget);

      app.notes.items.add(sampleNotification(message: 'Your order is now Ready.'));
      await _elapse(tester, 30);

      expect(find.text('Your order is now ready for delivery.'), findsOneWidget);
    });

    testWidgets('a failed check keeps the list and the next one brings the news', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification(id: 'a')]);
      await openBell(tester);
      app.notes.failNext = const NetworkException('offline');
      await _elapse(tester, 30);
      expect(find.text('Your order is now accepted by the store.'), findsOneWidget);

      app.notes.items.add(sampleNotification(id: 'b', message: 'Your order is now Ready.'));
      await _elapse(tester, 60);

      expect(find.text('Your order is now ready for delivery.'), findsOneWidget);
    });

    testWidgets('it does not ask when the screen is not in front', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification()]);
      await openBell(tester);
      await tester.pageBack();
      await tester.pumpAndSettle();
      final before = app.notes.calls.where((c) => c == 'list').length;

      await _elapse(tester, 120);

      expect(app.notes.calls.where((c) => c == 'list').length, before);
    });

    testWidgets('pulling down reads them again', (tester) async {
      final app = await _open(tester, notifications: [sampleNotification(id: 'a')]);
      await openBell(tester);
      app.notes.items.insert(0, sampleNotification(id: 'b', message: 'Your order is now Ready.'));

      await tester.drag(find.byType(ListView), const Offset(0, 1400));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));
      await tester.pumpAndSettle();

      expect(find.text('Your order is now ready for delivery.'), findsOneWidget);
    });
  });
}
