import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/phone_launcher.dart';
import 'package:quickcart_customer/data/repositories/cart_repository.dart';
import 'package:quickcart_customer/data/repositories/notification_repository.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';

const testPricing = PricingSettings(deliveryFee: 25, handlingFee: 5, freeDeliveryThreshold: 199);

/// One line the fake server holds.
class FakeServerLine {
  FakeServerLine(this.productId, this.variantId, this.name, this.label, this.quantity, this.price, {double? current, this.available = 999, this.unavailable = false}) : current = current ?? price;
  final String productId;
  final String? variantId;
  final String name, label;
  int quantity;
  double price, current;
  int available;
  bool unavailable;

  String get key => variantId ?? productId;
}

/// A server cart in memory that behaves like the real one, records every call, and can be told to fail or to answer slowly.
class FakeCartRepository implements CartRepository {
  FakeCartRepository({this.pricingSettings = testPricing});

  PricingSettings pricingSettings;
  String? storeId;
  final lines = <FakeServerLine>[];
  final calls = <String>[];

  /// The next call throws this (then it is cleared).
  Object? failNext;

  /// Makes every call wait this long, so tests can tap faster than the server answers.
  Duration delay = Duration.zero;

  /// Products the server knows, by cart key, so merge can tell what exists: key to (productId, variantId, name, label, price, stock).
  final catalogue = <String, ({String productId, String? variantId, String name, String label, double price, int stock})>{};

  void know(String key, {required String productId, String? variantId, String name = 'Item', String label = '1 kg', double price = 30, int stock = 100}) =>
      catalogue[key] = (productId: productId, variantId: variantId, name: name, label: label, price: price, stock: stock);

  Future<void> _enter(String call) async {
    calls.add(call);
    if (delay > Duration.zero) await Future<void>.delayed(delay);
    final failure = failNext;
    if (failure != null) {
      failNext = null;
      throw failure;
    }
  }

  ServerCart? get snapshot => lines.isEmpty ? null : _build(storeId!);

  ServerCart _build(String store) {
    final items = [
      for (final l in lines)
        ServerCartLine(
          productId: l.productId,
          variantId: l.variantId,
          name: l.name,
          label: l.label,
          quantity: l.quantity,
          unitPrice: l.price,
          currentUnitPrice: l.unavailable ? null : l.current,
          available: l.available,
          unavailable: l.unavailable,
        ),
    ];
    final subtotal = items.fold<double>(0, (s, l) => s + l.unitPrice * l.quantity);
    return ServerCart(storeId: store, lines: items, pricing: pricingSettings.price(subtotal));
  }

  FakeServerLine? _find(String key) => lines.where((l) => l.key == key).firstOrNull;

  /// The fee settings cannot be read.
  bool failPricing = false;

  @override
  Future<PricingSettings> pricing() async {
    calls.add('pricing');
    if (failPricing) throw const NetworkException('offline');
    return pricingSettings;
  }

  @override
  Future<ServerCart?> current() async {
    await _enter('current');
    return snapshot;
  }

  @override
  Future<ServerCart> add(String store, CartLineRef line) async {
    await _enter('add ${line.variantId ?? line.productId} x${line.quantity}');
    storeId = store;
    final key = line.variantId ?? line.productId;
    final known = catalogue[key];
    final existing = _find(key);
    if (existing != null) {
      existing.quantity += line.quantity;
    } else {
      lines.add(FakeServerLine(line.productId, line.variantId, known?.name ?? 'Item', known?.label ?? '1 kg', line.quantity, known?.price ?? 30));
    }
    return _build(store);
  }

  @override
  Future<ServerCart> setQuantity(String store, CartLineRef line) async {
    await _enter('set ${line.variantId ?? line.productId} x${line.quantity}');
    _find(line.variantId ?? line.productId)!.quantity = line.quantity;
    return _build(store);
  }

  @override
  Future<ServerCart> remove(String store, {required String productId, String? variantId}) async {
    await _enter('remove ${variantId ?? productId}');
    lines.removeWhere((l) => l.key == (variantId ?? productId));
    return _build(store);
  }

  @override
  Future<void> clear(String store) async {
    await _enter('clear');
    lines.clear();
  }

  @override
  Future<CartMergeResult> merge(String store, List<CartLineRef> wanted) async {
    await _enter('merge ${wanted.map((l) => '${l.variantId ?? l.productId}x${l.quantity}').join(',')}');
    storeId = store;
    final notes = <CartNote>[];
    for (final w in wanted) {
      final key = w.variantId ?? w.productId;
      final known = catalogue[key];
      if (known == null) {
        notes.add(CartNote(productId: w.productId, variantId: w.variantId, name: '', kind: CartNote.unavailable, quantity: w.quantity));
        continue;
      }
      final existing = _find(key);
      final have = existing?.quantity ?? 0;
      final room = known.stock - have;
      final add = w.quantity < room ? w.quantity : room;
      if (add <= 0) {
        notes.add(CartNote(productId: w.productId, variantId: w.variantId, name: known.name, label: known.label, kind: CartNote.outOfStock, quantity: w.quantity));
        continue;
      }
      if (add < w.quantity) notes.add(CartNote(productId: w.productId, variantId: w.variantId, name: known.name, label: known.label, kind: CartNote.reduced, quantity: add));
      if (existing != null) {
        existing.quantity += add;
      } else {
        lines.add(FakeServerLine(known.productId, known.variantId, known.name, known.label, add, known.price));
      }
    }
    return CartMergeResult(_build(store), notes);
  }

  @override
  Future<ServerCart> reprice(String store) async {
    await _enter('reprice');
    for (final l in lines.where((l) => !l.unavailable)) {
      l.price = l.current;
    }
    return _build(store);
  }
}

/// Orders in memory. Records each attempt (key, store, delivery) and fails or answers as told.
class FakeOrderRepository implements OrderRepository {
  final orders = <Order>[];
  final attempts = <({String storeId, String key, String? addressId, DeliveryPlace? place, bool online})>[];

  /// The next calls to [place] throw these, one each, in order.
  final failures = <Object>[];

  /// What to do when [place] succeeds, for example to empty the fake server cart the way the real server does.
  void Function()? onPlaced;

  /// The next [list] or [get] throws this.
  Object? failRead;
  var listCalls = 0;
  Duration delay = Duration.zero;
  var _number = 2000;

  /// Keys already used: the same key returns the same order.
  final _byKey = <String, Order>{};

  @override
  Future<Order> place({required String storeId, required String idempotencyKey, String? addressId, DeliveryPlace? place, bool online = false}) async {
    attempts.add((storeId: storeId, key: idempotencyKey, addressId: addressId, place: place, online: online));
    if (delay > Duration.zero) await Future<void>.delayed(delay);
    if (_byKey[idempotencyKey] case final known?) return known;
    if (failures.isNotEmpty) throw failures.removeAt(0);
    final number = 'ORD-${_number++}';
    final order = Order(
      id: 'id-$number',
      number: number,
      storeId: storeId,
      lines: const [OrderLine(productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', label: '1 kg', unitPrice: 28, quantity: 1)],
      subtotal: 28,
      deliveryFee: 25,
      handlingFee: 5,
      total: 58,
      placedAt: DateTime(2026, 10, 9, 18, 30),
      stage: online ? OrderStage.awaitingPayment : OrderStage.placed,
      address: addressId == null ? place!.line : 'Flat 4, 12 Marine Drive, Mumbai',
      payment: online ? 'Online' : 'CashOnDelivery',
      paymentState: online ? PaymentState.created : PaymentState.notRequired,
      paymentExpiresAt: online ? DateTime.now().add(const Duration(minutes: 15)) : null,
      receiverName: addressId == null ? null : 'Asha Patil',
      receiverPhone: addressId == null ? null : '9876543210',
    );
    _byKey[idempotencyKey] = order;
    orders.insert(0, order);
    onPlaced?.call();
    return order;
  }

  @override
  Future<List<Order>> list() async {
    listCalls++;
    if (delay > Duration.zero) await Future<void>.delayed(delay);
    if (failRead case final failure?) {
      failRead = null;
      throw failure;
    }
    return List.of(orders);
  }

  /// What [cancel] does next: null cancels, otherwise it throws this once.
  Object? failCancel;
  final cancelled = <String>[];

  @override
  Future<Order> cancel(String id) async {
    cancelled.add(id);
    if (delay > Duration.zero) await Future<void>.delayed(delay);
    if (failCancel case final failure?) {
      failCancel = null;
      throw failure;
    }
    final at = orders.indexWhere((o) => o.id == id);
    if (at < 0) throw const NotFoundException('Order not found', statusCode: 404);
    return orders[at] = orders[at].withStage(OrderStage.cancelled);
  }

  @override
  Future<Order> get(String id) async {
    if (failRead case final failure?) {
      failRead = null;
      throw failure;
    }
    return orders.firstWhere((o) => o.id == id);
  }
}

Order sampleOrder({
  String number = 'ORD-1001',
  OrderStage stage = OrderStage.delivered,
  double deliveryFee = 0,
  double handlingFee = 5,
  List<OrderLine>? lines,
  String? receiverName = 'Asha Patil',
  String? storeName = 'Harbor Point Dark Store',
  String? storePhone = '02249000001',
  int? estimatedMinutes,
  DateTime? placedAt,
}) {
  final ls = lines ?? const [OrderLine(productId: 'milk', variantId: 'milk:1l', name: 'Farm Milk', label: '1 l', unitPrice: 34, quantity: 2), OrderLine(productId: 'rice', variantId: 'rice:5kg', name: 'Basmati Rice', label: '5 kg', unitPrice: 89, quantity: 1)];
  final subtotal = ls.fold<double>(0, (s, l) => s + l.total);
  return Order(
    id: 'id-$number',
    number: number,
    storeId: 'store-1',
    lines: ls,
    subtotal: subtotal,
    deliveryFee: deliveryFee,
    handlingFee: handlingFee,
    total: subtotal + deliveryFee + handlingFee,
    placedAt: placedAt ?? DateTime(2026, 10, 2, 18, 40),
    stage: stage,
    address: 'Flat 4, 12 Marine Drive, Mumbai',
    payment: 'CashOnDelivery',
    receiverName: receiverName,
    receiverPhone: receiverName == null ? null : '9876543210',
    storeName: storeName,
    storePhone: storePhone,
    estimatedMinutes: estimatedMinutes,
  );
}

/// Notifications in memory. Records what the app asked for and can fail on demand.
class FakeNotificationRepository implements NotificationRepository {
  final items = <AppNotification>[];
  final calls = <String>[];

  /// The next call throws this, [failTimes] times in a row (once by default).
  Object? failNext;
  var failTimes = 1;

  void _maybeFail() {
    if (failNext case final failure?) {
      if (--failTimes <= 0) {
        failNext = null;
        failTimes = 1;
      }
      throw failure;
    }
  }

  @override
  Future<List<AppNotification>> list() async {
    calls.add('list');
    _maybeFail();
    return List.of(items)..sort((a, b) => b.createdAt.compareTo(a.createdAt)); // newest first, like the real repository
  }

  @override
  Future<int> unreadCount() async {
    calls.add('count');
    _maybeFail();
    return items.where((n) => !n.isRead).length;
  }

  @override
  Future<void> markRead(String id) async {
    calls.add('read $id');
    _maybeFail();
    final at = items.indexWhere((n) => n.id == id);
    if (at >= 0) items[at] = items[at].asRead();
  }

  @override
  Future<void> markAllRead() async {
    calls.add('read-all');
    _maybeFail();
    for (var i = 0; i < items.length; i++) {
      items[i] = items[i].asRead();
    }
  }
}

AppNotification sampleNotification({String id = 'n1', String? orderId = 'id-ORD-1001', String title = 'Order status updated', String message = 'Your order is now Accepted.', bool read = false, DateTime? at}) =>
    AppNotification(id: id, orderId: orderId, type: 'OrderStatusChanged', title: title, message: message, isRead: read, createdAt: at ?? DateTime.now().subtract(const Duration(minutes: 5)));

/// Opens no phone app; remembers the numbers it was asked to dial.
class FakePhoneLauncher implements PhoneLauncher {
  final dialled = <String>[];
  var opens = true;

  @override
  Future<bool> dial(String number) async {
    dialled.add(number);
    return opens;
  }
}
