import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/cart_dto.dart';
import 'package:quickcart_customer/data/dto/order_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/repositories/cart_repository.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';

import '../support/fake_adapter.dart';

Map<String, dynamic> _line({String productId = 'p1', String? variantId = 'v1', String name = 'Tomato', String label = '1 kg', int quantity = 2, double price = 28, Object? current = 28, Object? available = 40, bool unavailable = false}) => {
      'productId': productId,
      'productNameSnapshot': name,
      'unitPriceSnapshot': price,
      'quantity': quantity,
      'totalPrice': price * quantity,
      'variantId': variantId,
      'variantLabel': label,
      'available': available,
      'unavailable': unavailable,
      'currentUnitPrice': current,
    };

Map<String, dynamic> _cart({List<Map<String, dynamic>>? items, double subtotal = 56, double delivery = 25, double handling = 5}) => {
      'id': 'c1',
      'customerId': 'cu1',
      'storeId': 's1',
      'createdAt': '2026-10-09T10:00:00Z',
      'updatedAt': '2026-10-09T10:00:00Z',
      'items': items ?? [_line()],
      'totalAmount': subtotal,
      'subtotal': subtotal,
      'deliveryFee': delivery,
      'handlingFee': handling,
      'total': subtotal + delivery + handling,
      'freeDeliveryThreshold': 199,
      'amountToFreeDelivery': 199 - subtotal,
    };

Map<String, dynamic> _order({Object status = 0, Object? subtotal = 56, Object? delivery = 25, Object? handling = 5, double total = 86, String? receiver = 'Asha Patil'}) => {
      'id': 'o1',
      'orderNumber': 'ORD-20261009-abc',
      'userId': 'u1',
      'storeId': 's1',
      'totalAmount': total,
      'status': status,
      'deliveryAddress': 'Flat 4, 12 Marine Drive, Mumbai',
      'latitude': 19.07,
      'longitude': 72.87,
      'createdAt': '2026-10-09T10:00:00Z',
      'items': [
        {'productId': 'p1', 'productNameSnapshot': 'Tomato', 'unitPrice': 28, 'quantity': 2, 'totalPrice': 56, 'variantId': 'v1', 'variantLabel': '1 kg'}
      ],
      'statusHistory': [
        {'status': 0, 'changedAt': '2026-10-09T10:00:00Z'}
      ],
      'subtotalAmount': ?subtotal,
      'deliveryFee': ?delivery,
      'handlingFee': ?handling,
      'paymentMethod': 'CashOnDelivery',
      'receiverName': receiver,
      'receiverPhone': receiver == null ? null : '9876543210',
    };

ApiClient _client(FakeAdapter adapter) => ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

void main() {
  late FakeAdapter adapter;
  late ApiCartRepository cart;
  late ApiOrderRepository orders;
  setUp(() {
    adapter = FakeAdapter();
    cart = ApiCartRepository(_client(adapter));
    orders = ApiOrderRepository(_client(adapter));
  });

  group('the cart', () {
    test('a cart parses with the server fees and each line state', () async {
      adapter.replyJson({
        'success': true,
        'data': _cart(items: [_line(), _line(productId: 'p2', variantId: null, name: 'Soap', label: '', current: 31.5, available: 1), _line(productId: 'p3', variantId: 'v3', current: null, unavailable: true, available: 0)])
      });

      final c = (await cart.current())!;

      expect(adapter.requests.single.path, '/api/carts/current');
      expect((c.storeId, c.lines.length), ('s1', 3));
      expect((c.pricing.subtotal, c.pricing.deliveryFee, c.pricing.handlingFee, c.pricing.total, c.pricing.freeDeliveryThreshold), (56.0, 25.0, 5.0, 86.0, 199.0));
      final tomato = c.lines[0];
      expect((tomato.key, tomato.name, tomato.label, tomato.quantity, tomato.unitPrice, tomato.currentUnitPrice, tomato.available, tomato.unavailable), ('v1', 'Tomato', '1 kg', 2, 28.0, 28.0, 40, false));
      expect(c.lines[1].key, 'p2', reason: 'a product without packs is keyed by its own id');
      expect((c.lines[1].currentUnitPrice, c.lines[1].available), (31.5, 1));
      expect((c.lines[2].unavailable, c.lines[2].currentUnitPrice), (true, null));
      expect(c.quantities, {'v1': 2, 'p2': 2, 'v3': 2});
    });

    test('no cart is a 204 with no body and reads as null', () async {
      adapter.replyJson(null, status: 204);

      expect(await cart.current(), isNull);
    });

    test('adding sends the product, quantity and pack; a product without packs sends no variant', () async {
      adapter.replyJson({'success': true, 'data': _cart()});
      adapter.replyJson({'success': true, 'data': _cart()});

      await cart.add('s1', (productId: 'p1', variantId: 'v1', quantity: 3));
      await cart.add('s1', (productId: 'p2', variantId: null, quantity: 1));

      expect((adapter.requests[0].method, adapter.requests[0].path), ('POST', '/api/carts/s1/items'));
      expect(adapter.requests[0].data, {'productId': 'p1', 'quantity': 3, 'variantId': 'v1'});
      expect(adapter.requests[1].data, {'productId': 'p2', 'quantity': 1});
    });

    test('setting a quantity, removing a line and clearing use the right routes and name the pack', () async {
      adapter.replyJson({'success': true, 'data': _cart()});
      adapter.replyJson({'success': true, 'data': _cart()});
      adapter.replyJson({'success': true, 'data': _cart()});
      adapter.replyJson({'success': true, 'data': _cart()});
      adapter.replyJson({'success': true, 'data': null});

      await cart.setQuantity('s1', (productId: 'p1', variantId: 'v1', quantity: 5));
      await cart.setQuantity('s1', (productId: 'p2', variantId: null, quantity: 2));
      await cart.remove('s1', productId: 'p1', variantId: 'v1');
      await cart.reprice('s1');
      await cart.clear('s1');

      final r = adapter.requests;
      expect((r[0].method, r[0].path), ('PUT', '/api/carts/s1/items/p1?variantId=v1'));
      expect(r[0].data, {'quantity': 5});
      expect((r[1].method, r[1].path), ('PUT', '/api/carts/s1/items/p2'));
      expect((r[2].method, r[2].path), ('DELETE', '/api/carts/s1/items/p1?variantId=v1'));
      expect((r[3].method, r[3].path), ('POST', '/api/carts/s1/reprice'));
      expect((r[4].method, r[4].path), ('DELETE', '/api/carts/s1'));
    });

    test('merging sends the guest cart and reads the cart and what was left out', () async {
      adapter.replyJson({
        'success': true,
        'data': {
          'cart': _cart(),
          'notes': [
            {'productId': 'p9', 'variantId': null, 'name': '', 'label': null, 'kind': 'Unavailable', 'quantity': 1},
            {'productId': 'p1', 'variantId': 'v1', 'name': 'Tomato', 'label': '1 kg', 'kind': 'Reduced', 'quantity': 3},
          ]
        }
      });

      final result = await cart.merge('s1', [(productId: 'p1', variantId: 'v1', quantity: 5), (productId: 'p9', variantId: null, quantity: 1)]);

      expect(adapter.requests.single.path, '/api/carts/s1/merge');
      expect(adapter.requests.single.data, {
        'items': [
          {'productId': 'p1', 'quantity': 5, 'variantId': 'v1'},
          {'productId': 'p9', 'quantity': 1}
        ]
      });
      expect(result.cart.lines, hasLength(1));
      expect(result.notes.map((n) => (n.kind, n.quantity)), [(CartNote.unavailable, 1), (CartNote.reduced, 3)]);
    });

    test('the fee settings are public and parse', () async {
      adapter.replyJson({'success': true, 'data': {'deliveryFee': 25, 'handlingFee': 5, 'freeDeliveryThreshold': 199}});

      final settings = await cart.pricing();

      expect(adapter.requests.single.path, '/api/catalog/pricing');
      expect((settings.deliveryFee, settings.handlingFee, settings.freeDeliveryThreshold), (25.0, 5.0, 199.0));
    });

    test('a field of the wrong type is an unexpected response, not a half-read cart', () async {
      adapter.replyJson({'success': true, 'data': _cart(items: [_line()])..['subtotal'] = 'lots'});

      await expectLater(cart.current(), throwsA(isA<UnexpectedResponseException>()));
    });
  });

  group('fees are worked out the way the server does', () {
    const settings = PricingSettings(deliveryFee: 25, handlingFee: 5, freeDeliveryThreshold: 199);

    test('below the threshold: delivery and handling are charged and the missing amount is known', () {
      final price = settings.price(100);

      expect((price.deliveryFee, price.handlingFee, price.total, price.amountToFreeDelivery), (25.0, 5.0, 130.0, 99.0));
    });

    test('at the threshold delivery is free', () {
      final price = settings.price(199);

      expect((price.deliveryFee, price.total, price.amountToFreeDelivery), (0.0, 204.0, 0.0));
    });

    test('an empty cart costs nothing', () {
      final price = settings.price(0);

      expect((price.deliveryFee, price.handlingFee, price.total), (0.0, 0.0, 0.0));
    });

    test('without a threshold delivery is never free', () {
      final price = const PricingSettings(deliveryFee: 20, handlingFee: 0, freeDeliveryThreshold: 0).price(5000);

      expect((price.deliveryFee, price.total, price.amountToFreeDelivery), (20.0, 5020.0, 0.0));
    });

    test('pennies are rounded to the paisa', () {
      final price = settings.price(198.99);

      expect((price.total, price.amountToFreeDelivery), (228.99, 0.01));
    });
  });

  group('placing an order', () {
    test('a saved address is sent by id with the attempt key in the header', () async {
      adapter.replyJson({'success': true, 'data': _order()}, status: 201);

      final order = await orders.place(storeId: 's1', idempotencyKey: 'key-0000000000000001', addressId: 'a1');

      final request = adapter.requests.single;
      expect((request.method, request.path), ('POST', '/api/checkout/s1'));
      expect(request.data, {'addressId': 'a1'});
      expect(request.headers['Idempotency-Key'], 'key-0000000000000001');
      expect((order.number, order.total, order.subtotal, order.deliveryFee, order.handlingFee, order.payment, order.receiverName), ('ORD-20261009-abc', 86.0, 56.0, 25.0, 5.0, 'CashOnDelivery', 'Asha Patil'));
    });

    test('a place on this phone is sent as text and a point', () async {
      adapter.replyJson({'success': true, 'data': _order(receiver: null)}, status: 201);

      await orders.place(
        storeId: 's1',
        idempotencyKey: 'key-0000000000000002',
        place: const DeliveryPlace(label: 'Current location', line: '12 Marine Drive, Mumbai', latitude: 19.076, longitude: 72.8777),
      );

      expect(adapter.requests.single.data, {'deliveryAddress': '12 Marine Drive, Mumbai', 'latitude': 19.076, 'longitude': 72.8777});
    });

    test('a repeat answer (200 with the same order) reads like the first', () async {
      adapter.replyJson({'success': true, 'data': _order(), 'replayed': true});

      final order = await orders.place(storeId: 's1', idempotencyKey: 'key-0000000000000001', addressId: 'a1');

      expect(order.id, 'o1');
    });

    test('a refused order carries the reason and the lines involved', () async {
      adapter.replyJson({
        'success': false,
        'message': 'A cart product price has changed',
        'reason': 'PriceChanged',
        'details': [
          {'productId': 'p1', 'variantId': 'v1', 'name': 'Tomato', 'label': '1 kg', 'quantity': 2, 'available': null, 'oldPrice': 28, 'newPrice': 31}
        ],
        'errors': <String>[]
      }, status: 409);

      final error = await orders.place(storeId: 's1', idempotencyKey: 'key-0000000000000003', addressId: 'a1').then<ConflictException?>((_) => null, onError: (Object e) => e as ConflictException);

      expect(error!.reason, CheckoutReasons.priceChanged);
      final issues = checkoutIssuesFrom(error.details);
      expect(issues, hasLength(1));
      expect((issues.single.key, issues.single.name, issues.single.oldPrice, issues.single.newPrice, issues.single.quantity), ('v1', 'Tomato', 28.0, 31.0, 2));
    });

    test('stock and gone-product details parse, and anything unreadable is skipped rather than hiding the reason', () {
      final issues = checkoutIssuesFrom([
        {'productId': 'p1', 'variantId': null, 'name': 'Soap', 'label': null, 'quantity': 3, 'available': 1},
        {'productId': 7},
        'nonsense',
      ]);

      expect(issues.map((i) => (i.key, i.available)), [('p1', 1)]);
    });

    test('a refusal without a reason or details still reads as a conflict', () async {
      adapter.replyJson({'success': false, 'message': 'Checkout could not be completed', 'errors': <String>[]}, status: 409);

      final error = await orders.place(storeId: 's1', idempotencyKey: 'key-0000000000000004', addressId: 'a1').then<ConflictException?>((_) => null, onError: (Object e) => e as ConflictException);

      expect(error!.reason, isNull);
      expect(error.details, isEmpty);
    });

    test('an unknown address is not found and a dropped connection is a network error', () async {
      adapter.replyJson({'success': false, 'message': 'Address not found'}, status: 404);
      adapter.fail(DioExceptionType.connectionError);

      await expectLater(orders.place(storeId: 's1', idempotencyKey: 'key-0000000000000005', addressId: 'gone'), throwsA(isA<NotFoundException>()));
      await expectLater(orders.place(storeId: 's1', idempotencyKey: 'key-0000000000000006', addressId: 'a1'), throwsA(isA<NetworkException>()));
    });
  });

  group('reading orders', () {
    test('orders come newest first', () async {
      adapter.replyJson({
        'success': true,
        'data': [
          {..._order(), 'id': 'old', 'createdAt': '2026-10-01T10:00:00Z'},
          {..._order(), 'id': 'new', 'createdAt': '2026-10-09T10:00:00Z'},
        ]
      });

      final list = await orders.list();

      expect(adapter.requests.single.path, '/api/customer/orders');
      expect(list.map((o) => o.id), ['new', 'old']);
    });

    test('one order is read by id', () async {
      adapter.replyJson({'success': true, 'data': _order()});

      final order = await orders.get('o1');

      expect(adapter.requests.single.path, '/api/customer/orders/o1');
      expect(order.lines.single.name, 'Tomato');
      expect((order.lines.single.variantId, order.lines.single.label, order.lines.single.unitPrice, order.lines.single.quantity, order.lines.single.total), ('v1', '1 kg', 28.0, 2, 56.0));
    });

    test('an order placed before fees existed reads with the products as the subtotal and no fees', () {
      final order = orderFromJson(_order(subtotal: null, delivery: null, handling: null, total: 56));

      expect((order.subtotal, order.deliveryFee, order.handlingFee, order.total), (56.0, 0.0, 0.0, 56.0));
    });

    test('the status is read from its number or its name', () {
      expect([0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(orderStageFrom), [
        OrderStage.placed,
        OrderStage.packed,
        OrderStage.packed,
        OrderStage.onTheWay,
        OrderStage.delivered,
        OrderStage.rejected,
        OrderStage.packed,
        OrderStage.onTheWay,
        OrderStage.delivered,
        OrderStage.cancelled,
      ]);
      expect(['Pending', 'Preparing', 'Ready', 'Completed', 'Rejected'].map(orderStageFrom), [OrderStage.placed, OrderStage.packed, OrderStage.onTheWay, OrderStage.delivered, OrderStage.rejected]);
      expect(() => orderStageFrom(99), throwsFormatException);
      expect(() => orderStageFrom('Teleported'), throwsFormatException);
    });
  });
}
