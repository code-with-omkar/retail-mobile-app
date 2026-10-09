import '../api/api_exception.dart';
import '../models.dart';
import '../seed.dart' as seed;

/// A tiny in-memory shop for offline seed mode: one cart and the orders placed from it, working like the server does.
/// Prices come from the seed catalogue; the fee settings are the seed constants.
class LocalShop {
  LocalShop({PricingSettings? pricing, List<Order>? orders})
      : pricing = pricing ??
            PricingSettings(deliveryFee: seed.deliveryFee.toDouble(), handlingFee: seed.handlingFee.toDouble(), freeDeliveryThreshold: seed.freeDeliveryThreshold.toDouble()),
        orders = orders ?? [];

  final PricingSettings pricing;
  final List<Order> orders;
  final List<AppNotification> notifications = [];
  String? _storeId;
  final _quantities = <String, int>{};
  final _prices = <String, double>{};
  var _nextNumber = 1025;

  /// A seed product by cart key (its pack id or its own id), and the pack when the key names one.
  ({Product product, PackOption? pack})? lookup(String key) {
    for (final p in seed.products) {
      final pack = p.packs.where((x) => x.id == key).firstOrNull;
      if (pack != null) return (product: p, pack: pack);
      if (p.id == key) return (product: p, pack: p.defaultPack);
    }
    return null;
  }

  int quantityOf(String key) => _quantities[key] ?? 0;

  ServerCart? cart() => _quantities.isEmpty ? null : _build(_storeId!);

  ServerCart setLine(String storeId, String key, int quantity) {
    _storeId = storeId;
    final found = lookup(key);
    if (quantity <= 0 || found == null) {
      _quantities.remove(key);
      _prices.remove(key);
    } else {
      _quantities[key] = quantity;
      _prices.putIfAbsent(key, () => found.pack?.price ?? found.product.price);
    }
    return _build(storeId);
  }

  /// The prices in the cart become the shop's current prices.
  ServerCart? reprice(String storeId) {
    for (final key in _quantities.keys) {
      final found = lookup(key);
      if (found != null) _prices[key] = found.pack?.price ?? found.product.price;
    }
    return cart();
  }

  void clearCart() {
    _quantities.clear();
    _prices.clear();
  }

  ServerCart _build(String storeId) {
    final lines = <ServerCartLine>[
      for (final e in _quantities.entries)
        () {
          final found = lookup(e.key)!;
          return ServerCartLine(
            productId: found.product.id,
            variantId: found.pack?.id,
            name: found.product.name,
            label: found.pack?.label ?? found.product.unit,
            quantity: e.value,
            unitPrice: _prices[e.key]!,
            currentUnitPrice: found.pack?.price ?? found.product.price,
            available: 999,
          );
        }(),
    ];
    final subtotal = lines.fold<double>(0, (s, l) => s + l.unitPrice * l.quantity);
    return ServerCart(storeId: storeId, lines: lines, pricing: pricing.price(subtotal));
  }

  /// Cancels a pending order and tells the customer, like the server does.
  Order cancelOrder(String id) {
    final at = orders.indexWhere((o) => o.id == id);
    if (at < 0) throw const NotFoundException('Order not found', statusCode: 404);
    final order = orders[at];
    if (order.stage == OrderStage.cancelled) return order;
    if (order.stage != OrderStage.placed) throw const ConflictException('Too late to cancel', statusCode: 409, reason: 'OrderNotCancellable');
    final cancelled = order.withStage(OrderStage.cancelled);
    orders[at] = cancelled;
    notifications.insert(0, AppNotification(id: 'n${notifications.length + 1}', orderId: id, type: 'OrderStatusChanged', title: 'Order cancelled', message: 'Your order was cancelled.', isRead: false, createdAt: DateTime.now()));
    return cancelled;
  }

  /// Turns the cart into an order, like the server's checkout.
  Order placeOrder({required String address, String? receiverName, String? receiverPhone}) {
    final cart = this.cart()!;
    final number = 'QC${_nextNumber++}';
    final order = Order(
      id: number,
      number: number,
      storeId: cart.storeId,
      lines: [for (final l in cart.lines) OrderLine(productId: l.productId, variantId: l.variantId, name: l.name, label: l.label, unitPrice: l.unitPrice, quantity: l.quantity)],
      subtotal: cart.pricing.subtotal,
      deliveryFee: cart.pricing.deliveryFee,
      handlingFee: cart.pricing.handlingFee,
      total: cart.pricing.total,
      placedAt: DateTime.now(),
      stage: OrderStage.placed,
      address: address,
      payment: 'CashOnDelivery',
      receiverName: receiverName,
      receiverPhone: receiverPhone,
    );
    orders.insert(0, order);
    clearCart();
    return order;
  }
}
