import '../api/api_client.dart';
import '../api/api_exception.dart';
import '../dto/catalog_dto.dart';
import '../dto/order_dto.dart';
import '../models.dart';
import 'local_shop.dart';

/// Placing orders and reading them back. Screens depend on this interface, not on HTTP.
abstract interface class OrderRepository {
  /// Places the order for the customer's cart in [storeId]. Delivers to [addressId] (a saved address) or, without one, to [place].
  ///
  /// [idempotencyKey] names this attempt: sending the same key again (a retry, a double tap) returns the order the first
  /// attempt made instead of a second one. Use a new key for each new attempt to order.
  ///
  /// A [ConflictException] with a `reason` says why the order cannot be placed (see [CheckoutReasons]).
  ///
  /// [online] asks to pay now through the payment provider: the order then comes back [OrderStage.awaitingPayment] and must be paid
  /// (see PaymentRepository) before the store sees it. The default is cash on delivery.
  Future<Order> place({required String storeId, required String idempotencyKey, String? addressId, DeliveryPlace? place, bool online = false});

  /// The customer's orders, newest first.
  Future<List<Order>> list();

  Future<Order> get(String id);

  /// Cancels the customer's own order while the store has not accepted it. A [ConflictException] with reason `OrderNotCancellable`
  /// means it is too late (the store has accepted it); cancelling twice returns the same cancelled order.
  Future<Order> cancel(String id);
}

/// `api/checkout/*` and `api/customer/orders`.
class ApiOrderRepository implements OrderRepository {
  ApiOrderRepository(this._api);
  final ApiClient _api;

  @override
  Future<Order> place({required String storeId, required String idempotencyKey, String? addressId, DeliveryPlace? place, bool online = false}) {
    assert(addressId != null || place != null, 'Delivery needs a saved address or a place');
    return _api.post(
      '/api/checkout/$storeId',
      headers: {'Idempotency-Key': idempotencyKey},
      body: {
        ...(addressId != null ? {'addressId': addressId} : {'deliveryAddress': place!.line, 'latitude': place.latitude, 'longitude': place.longitude}),
        if (online) 'paymentMethod': 'Online',
      },
      parse: (d) => orderFromJson(asObject(d)),
    );
  }

  @override
  Future<List<Order>> list() async {
    final orders = await _api.get('/api/customer/orders', parse: ordersFromJson);
    return orders..sort((a, b) => b.placedAt.compareTo(a.placedAt));
  }

  @override
  Future<Order> get(String id) => _api.get('/api/customer/orders/$id', parse: (d) => orderFromJson(asObject(d)));

  @override
  Future<Order> cancel(String id) => _api.post('/api/customer/orders/$id/cancel', parse: (d) => orderFromJson(asObject(d)));
}

/// Orders without a server (offline seed mode): placed from the in-memory cart.
class LocalOrderRepository implements OrderRepository {
  LocalOrderRepository(this._shop);
  final LocalShop _shop;

  @override
  Future<Order> place({required String storeId, required String idempotencyKey, String? addressId, DeliveryPlace? place, bool online = false}) async {
    if (online) throw const ConflictException('Online payment is not available', statusCode: 409, reason: 'PaymentsUnavailable');
    if (_shop.cart() == null) throw const ConflictException('Cart is empty', statusCode: 409, reason: CheckoutReasons.cartEmpty);
    return _shop.placeOrder(address: place?.line ?? 'Saved address');
  }

  @override
  Future<List<Order>> list() async => List.of(_shop.orders);

  @override
  Future<Order> cancel(String id) async => _shop.cancelOrder(id);

  @override
  Future<Order> get(String id) async => _shop.orders.firstWhere((o) => o.id == id, orElse: () => throw const NotFoundException('Order not found', statusCode: 404));
}
