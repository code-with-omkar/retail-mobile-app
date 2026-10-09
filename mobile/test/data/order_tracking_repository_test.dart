import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/order_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/repositories/local_shop.dart';
import 'package:quickcart_customer/data/repositories/notification_repository.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';

Map<String, dynamic> _order({Object status = 0, Object? storeName = 'Harbor Point Dark Store', Object? storePhone = '02249000001', Object? estimate = 9}) => {
      'id': 'o1',
      'orderNumber': 'ORD-1',
      'userId': 'u1',
      'storeId': 's1',
      'totalAmount': 86,
      'status': status,
      'deliveryAddress': '12 Marine Drive',
      'latitude': 19.07,
      'longitude': 72.87,
      'createdAt': '2026-10-09T10:00:00Z',
      'items': [
        {'productId': 'p1', 'productNameSnapshot': 'Tomato', 'unitPrice': 28, 'quantity': 2, 'totalPrice': 56, 'variantId': 'v1', 'variantLabel': '1 kg'}
      ],
      'statusHistory': [
        {'status': 0, 'changedAt': '2026-10-09T10:00:00Z'}
      ],
      'subtotalAmount': 56,
      'deliveryFee': 25,
      'handlingFee': 5,
      'paymentMethod': 'CashOnDelivery',
      'storeName': storeName,
      'storePhone': storePhone,
      'estimatedDeliveryMinutes': estimate,
    };

Map<String, dynamic> _note({String id = 'n1', bool isRead = false, String createdAt = '2026-10-09T10:05:00Z'}) => {
      'id': id,
      'orderId': 'o1',
      'type': 'OrderStatusChanged',
      'title': 'Order status updated',
      'message': 'Your order is now Accepted.',
      'isRead': isRead,
      'createdAt': createdAt,
    };

ApiClient _client(FakeAdapter adapter) => ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

void main() {
  late FakeAdapter adapter;
  late ApiOrderRepository orders;
  late ApiNotificationRepository notifications;
  setUp(() {
    adapter = FakeAdapter();
    orders = ApiOrderRepository(_client(adapter));
    notifications = ApiNotificationRepository(_client(adapter));
  });

  group('an order carries its store and estimate', () {
    test('the store name, phone and the estimate it was given are read', () {
      final order = orderFromJson(_order());

      expect((order.storeName, order.storePhone, order.estimatedMinutes), ('Harbor Point Dark Store', '02249000001', 9));
    });

    test('a store without a phone, an order without an estimate and an older answer without those fields all read without trouble', () {
      final noPhone = orderFromJson(_order(storePhone: null));
      final blankPhone = orderFromJson(_order(storePhone: '  '));
      final old = orderFromJson(_order(storeName: null, storePhone: null, estimate: null)..remove('storeName')..remove('storePhone')..remove('estimatedDeliveryMinutes'));

      expect(noPhone.storePhone, isNull);
      expect(blankPhone.storePhone, isNull);
      expect((old.storeName, old.storePhone, old.estimatedMinutes), (null, null, null));
    });

    test('cancelled is its own status, apart from declined', () {
      expect(orderStageFrom(9), OrderStage.cancelled);
      expect(orderStageFrom('Cancelled'), OrderStage.cancelled);
      expect(orderStageFrom(5), OrderStage.rejected);
      expect(OrderStage.values.where((s) => s.isActive), [OrderStage.awaitingPayment, OrderStage.placed, OrderStage.packed, OrderStage.onTheWay]);
    });
  });

  group('cancelling', () {
    test('posts to the order and reads the cancelled order back', () async {
      adapter.replyJson({'success': true, 'data': _order(status: 9)});

      final order = await orders.cancel('o1');

      expect((adapter.requests.single.method, adapter.requests.single.path), ('POST', '/api/customer/orders/o1/cancel'));
      expect(order.stage, OrderStage.cancelled);
    });

    test('too late is a conflict with its reason', () async {
      adapter.replyJson({'success': false, 'message': 'no', 'reason': 'OrderNotCancellable', 'errors': <String>[]}, status: 409);

      final error = await orders.cancel('o1').then<ConflictException?>((_) => null, onError: (Object e) => e as ConflictException);

      expect(error!.reason, 'OrderNotCancellable');
    });

    test("someone else's or a missing order is not found", () async {
      adapter.replyJson({'success': false, 'message': 'Order not found', 'errors': <String>[]}, status: 404);

      await expectLater(orders.cancel('gone'), throwsA(isA<NotFoundException>()));
    });

    test('the in-memory shop cancels a placed order, refuses one the shop took up and says nothing twice', () async {
      final shop = LocalShop(orders: [sampleOrder(number: 'QC1', stage: OrderStage.placed), sampleOrder(number: 'QC2', stage: OrderStage.packed)]);
      final local = LocalOrderRepository(shop);

      final cancelled = await local.cancel('id-QC1');
      final again = await local.cancel('id-QC1');

      expect((cancelled.stage, again.stage), (OrderStage.cancelled, OrderStage.cancelled));
      expect(shop.notifications, hasLength(1));
      await expectLater(local.cancel('id-QC2'), throwsA(isA<ConflictException>().having((e) => e.reason, 'reason', 'OrderNotCancellable')));
      await expectLater(local.cancel('nope'), throwsA(isA<NotFoundException>()));
    });
  });

  group('notifications', () {
    test('the list is read newest first', () async {
      adapter.replyJson({
        'success': true,
        'data': [_note(id: 'old', createdAt: '2026-10-01T10:00:00Z'), _note(id: 'new')]
      });

      final list = await notifications.list();

      expect(adapter.requests.single.path, '/api/customer/notifications');
      expect(list.map((n) => n.id), ['new', 'old']);
      expect((list.first.orderId, list.first.type, list.first.title, list.first.message, list.first.isRead), ('o1', 'OrderStatusChanged', 'Order status updated', 'Your order is now Accepted.', false));
    });

    test('the unread count is a number', () async {
      adapter.replyJson({'success': true, 'data': {'count': 3}});

      expect(await notifications.unreadCount(), 3);
      expect(adapter.requests.single.path, '/api/customer/notifications/unread-count');
    });

    test('marking one read and all read use the right routes', () async {
      adapter.replyJson({'success': true});
      adapter.replyJson({'success': true, 'data': {'updated': 2}});

      await notifications.markRead('n1');
      await notifications.markAllRead();

      expect((adapter.requests[0].method, adapter.requests[0].path), ('PUT', '/api/customer/notifications/n1/read'));
      expect(adapter.requests[0].data, {'isRead': true});
      expect((adapter.requests[1].method, adapter.requests[1].path), ('PUT', '/api/customer/notifications/read-all'));
    });

    test('a notification without an order is fine, and a field of the wrong type is an unexpected response', () async {
      adapter.replyJson({
        'success': true,
        'data': [
          {..._note(), 'orderId': null}
        ]
      });
      adapter.replyJson({
        'success': true,
        'data': [
          {..._note(), 'createdAt': 5}
        ]
      });

      expect((await notifications.list()).single.orderId, isNull);
      await expectLater(notifications.list(), throwsA(isA<UnexpectedResponseException>()));
    });

    test('without a server (seed mode) they behave the same', () async {
      final shop = LocalShop(orders: [sampleOrder(number: 'QC1', stage: OrderStage.placed)]);
      final repo = LocalNotificationRepository(shop);
      await LocalOrderRepository(shop).cancel('id-QC1');

      expect(await repo.unreadCount(), 1);
      await repo.markRead(shop.notifications.single.id);
      expect(await repo.unreadCount(), 0);
      await LocalOrderRepository(shop).cancel('id-QC1');
      await repo.markAllRead();
      expect((await repo.list()).every((n) => n.isRead), isTrue);
    });
  });
}
