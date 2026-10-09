import 'package:flutter/material.dart';

class Category {
  const Category(this.id, this.label, this.icon, {String? labelMr}) : labelMr = labelMr ?? label;
  final String id;
  final String label;

  /// Marathi name; equals [label] when there is no translation.
  final String labelMr;
  final IconData icon;

  bool get hasMarathi => labelMr != label;
}

/// Groups API categories (free-form names) into the few visual groups the app has icons for.
String categoryKeyFor(String name) {
  final n = name.toLowerCase();
  if (n.contains('veg')) return 'vegetables';
  if (n.contains('fruit')) return 'fruits';
  if (n.contains('dairy') || n.contains('milk')) return 'dairy';
  if (n.contains('grocer') || n.contains('pantry') || n.contains('staple')) return 'pantry';
  if (n.contains('snack') || n.contains('bakery')) return 'snacks';
  return 'other';
}

IconData iconForCategoryKey(String key) => switch (key) {
      'vegetables' => Icons.eco_outlined,
      'fruits' => Icons.apple,
      'dairy' => Icons.local_drink_outlined,
      'pantry' => Icons.shopping_basket_outlined,
      'snacks' => Icons.cookie_outlined,
      _ => Icons.category_outlined,
    };

/// One pack size of a product (an API variant): its own price, MRP and stock. [id] is what the cart is keyed by.
class PackOption {
  const PackOption({required this.id, required this.label, required this.price, required this.mrp, this.isDefault = false, this.inStock = true, this.lowStock = false});
  final String id, label;
  final double price, mrp;
  final bool isDefault;

  /// Stock of this pack at the selected store. True when no store was asked (unknown).
  final bool inStock;
  final bool lowStock;

  int get discountPct => mrp > price ? (((mrp - price) / mrp) * 100).round() : 0;
}

class Product {
  const Product({
    required this.id,
    required this.name,
    required this.nameMr,
    required this.unit,
    required this.price,
    required this.mrp,
    required this.color,
    required this.category,
    String? categoryId,
    required this.description,
    String? descriptionMr,
    this.imageUrl,
    this.packs = const [],
    this.rating = 4.5,
    this.inStock = true,
    this.lowStock = false,
  })  : categoryId = categoryId ?? category,
        descriptionMr = descriptionMr ?? description;

  final String id, name, nameMr, unit, category, categoryId, description, descriptionMr;

  /// Money is decimal because the API returns decimals (for example 34.50).
  final double price, mrp;
  final Color color;
  final double rating;
  final bool inStock;

  /// In stock but nearly out: show "Only a few left".
  final bool lowStock;

  /// Pack sizes, smallest first as the API orders them. The product's own price, MRP, unit and stock describe the default pack.
  final List<PackOption> packs;

  /// The pack that is bought when none is chosen, or null for a product without packs.
  PackOption? get defaultPack => packs.where((p) => p.isDefault).firstOrNull ?? packs.firstOrNull;

  /// Optional product photo URL. When null or failing to load, a generated category icon is shown.
  final String? imageUrl;

  /// [category] is a visual group key (see [categoryKeyFor]); [categoryId] is the catalog's id for filtering.
  IconData get icon => iconForCategoryKey(category);

  int get discountPct => mrp > price ? (((mrp - price) / mrp) * 100).round() : 0;
}

double _money(double v) => (v * 100).round() / 100;

/// One cart entry: a product in one pack size ([pack] null means a product without packs).
class CartLine {
  const CartLine(this.product, this.quantity, [this.pack]) : priceSnapshot = null, currentPrice = null, available = null, unavailable = false;
  const CartLine._server(this.product, this.quantity, this.pack, this.priceSnapshot, this.currentPrice, this.available, this.unavailable);
  final Product product;
  final int quantity;
  final PackOption? pack;

  /// For a signed-in customer: the price the server holds for this line, which is what checkout charges. Null for a guest's line.
  final double? priceSnapshot;

  /// The shop's price now, when it differs from [priceSnapshot] the line is marked "price changed".
  final double? currentPrice;

  /// How many the store can supply now (null when not known).
  final int? available;

  /// The product or pack is gone, so checkout would refuse.
  final bool unavailable;

  /// The same line with what the server says about it.
  CartLine withServer(ServerCartLine s) => CartLine._server(product, quantity, pack, s.unitPrice, s.currentUnitPrice, s.available, s.unavailable);

  bool get priceChanged => priceSnapshot != null && currentPrice != null && (currentPrice! - priceSnapshot!).abs() > 0.004;
  bool get notEnoughStock => available != null && quantity > available!;

  double get unitPrice => priceSnapshot ?? pack?.price ?? product.price;
  double get unitMrp => pack?.mrp ?? product.mrp;
  String get unitLabel => pack?.label ?? product.unit;
  double get total => _money(unitPrice * quantity);
  String get key => cartKey(product, pack);
}

/// Cart key: the pack's id (the API variant id), or the product id for a product without packs. A product added without
/// naming a pack is its default pack.
String cartKey(Product p, [PackOption? pack]) => (pack ?? p.defaultPack)?.id ?? p.id;

/// A store that delivers to the delivery location, with distance and delivery estimate from the API.
class NearestStore {
  const NearestStore({required this.id, required this.name, required this.distanceKm, required this.estimatedMinutes, this.address = '', this.serviceRadiusKm = 0});
  final String id, name, address;
  final double distanceKm;

  /// How far this store delivers; 0 when unknown.
  final double serviceRadiusKm;
  final int estimatedMinutes;
}

/// How the shop has got on with an order.
enum OrderStage {
  /// An online order waiting for its payment: the items are held for a short while, and the shop has not seen it yet.
  awaitingPayment,
  placed,
  packed,
  onTheWay,
  delivered,

  /// The shop declined it.
  rejected,

  /// The customer cancelled it before the shop accepted it.
  cancelled;

  /// Still on its way to being delivered, so its status can still change.
  bool get isActive => this == awaitingPayment || this == placed || this == packed || this == onTheWay;
}

/// Where the payment of an order stands. Cash on delivery orders are [notRequired].
enum PaymentState {
  notRequired,
  created,
  failed,
  paid,
  refunding,
  refunded,
  refundFailed,
}

/// One line of a placed order, as it was when the order was placed: later price or name changes never touch it.
class OrderLine {
  const OrderLine({required this.productId, this.variantId, required this.name, this.label = '', required this.unitPrice, required this.quantity});
  final String productId, name, label;
  final String? variantId;
  final double unitPrice;
  final int quantity;

  double get total => _money(unitPrice * quantity);

  /// The key the cart and the product store use for this line.
  String get key => variantId ?? productId;
}

/// A placed order (`api/customer/orders`).
class Order {
  const Order({
    required this.id,
    required this.number,
    required this.storeId,
    required this.lines,
    required this.subtotal,
    required this.deliveryFee,
    required this.handlingFee,
    required this.total,
    required this.placedAt,
    required this.stage,
    required this.address,
    required this.payment,
    this.receiverName,
    this.receiverPhone,
    this.storeName,
    this.storePhone,
    this.estimatedMinutes,
    this.paymentState = PaymentState.notRequired,
    this.paymentExpiresAt,
  });

  /// The server's id (used in routes). [number] is what the customer sees and quotes.
  final String id, number, storeId, address, payment;
  final List<OrderLine> lines;
  final double subtotal, deliveryFee, handlingFee, total;
  final DateTime placedAt;
  final OrderStage stage;
  final String? receiverName, receiverPhone;

  /// Who has the order and how to reach them. The phone is null until the store has one on record.
  final String? storeName, storePhone;

  /// The arrival estimate given when the order was placed; null for orders placed before it was kept.
  final int? estimatedMinutes;

  /// For online orders: how the payment stands, and until when the items are held while it is unpaid.
  final PaymentState paymentState;
  final DateTime? paymentExpiresAt;

  bool get isOnline => payment == 'Online';

  /// A copy with another payment state (and status), for example once an online order is paid.
  Order withPayment({required OrderStage stage, required PaymentState state}) => Order(
        id: id, number: number, storeId: storeId, lines: lines, subtotal: subtotal, deliveryFee: deliveryFee, handlingFee: handlingFee, total: total,
        placedAt: placedAt, stage: stage, address: address, payment: payment, receiverName: receiverName, receiverPhone: receiverPhone,
        storeName: storeName, storePhone: storePhone, estimatedMinutes: estimatedMinutes, paymentState: state, paymentExpiresAt: stage == OrderStage.awaitingPayment ? paymentExpiresAt : null,
      );

  /// A copy with another status (used when the customer cancels, before the next read).
  Order withStage(OrderStage next) => Order(
        id: id, number: number, storeId: storeId, lines: lines, subtotal: subtotal, deliveryFee: deliveryFee, handlingFee: handlingFee, total: total,
        placedAt: placedAt, stage: next, address: address, payment: payment, receiverName: receiverName, receiverPhone: receiverPhone,
        storeName: storeName, storePhone: storePhone, estimatedMinutes: estimatedMinutes, paymentState: paymentState, paymentExpiresAt: paymentExpiresAt,
      );

  int get itemCount => lines.fold(0, (s, l) => s + l.quantity);
}

/// What the customer pays on top of the products, as the server is set up (`GET api/catalog/pricing`).
class PricingSettings {
  const PricingSettings({required this.deliveryFee, required this.handlingFee, required this.freeDeliveryThreshold});
  final double deliveryFee, handlingFee, freeDeliveryThreshold;

  /// The same rule the server uses, so the cart shows what checkout will charge.
  CartPricing price(double subtotal) {
    if (subtotal <= 0) return CartPricing(subtotal: 0, deliveryFee: 0, handlingFee: 0, total: 0, freeDeliveryThreshold: freeDeliveryThreshold, amountToFreeDelivery: 0);
    final free = freeDeliveryThreshold > 0 && subtotal >= freeDeliveryThreshold;
    final delivery = free ? 0.0 : deliveryFee;
    final toFree = freeDeliveryThreshold > 0 && !free ? _money(freeDeliveryThreshold - subtotal) : 0.0;
    return CartPricing(subtotal: subtotal, deliveryFee: delivery, handlingFee: handlingFee, total: _money(subtotal + delivery + handlingFee), freeDeliveryThreshold: freeDeliveryThreshold, amountToFreeDelivery: toFree);
  }
}

class CartPricing {
  const CartPricing({required this.subtotal, required this.deliveryFee, required this.handlingFee, required this.total, required this.freeDeliveryThreshold, required this.amountToFreeDelivery});
  final double subtotal, deliveryFee, handlingFee, total, freeDeliveryThreshold, amountToFreeDelivery;
}

/// A line of the server's cart (`api/carts`).
class ServerCartLine {
  const ServerCartLine({required this.productId, this.variantId, required this.name, this.label = '', required this.quantity, required this.unitPrice, this.currentUnitPrice, this.available, this.unavailable = false});
  final String productId, name, label;
  final String? variantId;
  final int quantity;

  /// The price when the line was added; checkout charges this and refuses when it no longer matches the shop's price.
  final double unitPrice;
  final double? currentUnitPrice;
  final int? available;
  final bool unavailable;

  /// The key the cart and the product store use for this line.
  String get key => variantId ?? productId;
}

/// The customer's cart as the server holds it.
class ServerCart {
  const ServerCart({required this.storeId, required this.lines, required this.pricing});
  final String storeId;
  final List<ServerCartLine> lines;
  final CartPricing pricing;

  Map<String, int> get quantities => {for (final l in lines) l.key: l.quantity};
}

/// What a merge did not do as asked.
class CartNote {
  const CartNote({required this.productId, this.variantId, required this.name, this.label = '', required this.kind, required this.quantity});
  final String productId, name, label, kind;
  final String? variantId;
  final int quantity;

  static const unavailable = 'Unavailable';
  static const outOfStock = 'OutOfStock';
  static const reduced = 'Reduced';
}

class CartMergeResult {
  const CartMergeResult(this.cart, this.notes);
  final ServerCart cart;
  final List<CartNote> notes;
}

/// Stable codes the API gives when checkout cannot go ahead (`reason` of a 409).
class CheckoutReasons {
  static const cartEmpty = 'CartEmpty';
  static const productUnavailable = 'ProductUnavailable';
  static const priceChanged = 'PriceChanged';
  static const inventoryConflict = 'InventoryConflict';
  static const idempotencyKeyReused = 'IdempotencyKeyReused';
}

/// A cart line that stopped checkout.
class CheckoutIssue {
  const CheckoutIssue({required this.productId, this.variantId, required this.name, this.label = '', required this.quantity, this.available, this.oldPrice, this.newPrice});
  final String productId, name, label;
  final String? variantId;
  final int quantity;
  final int? available;
  final double? oldPrice, newPrice;

  String get key => variantId ?? productId;
}

/// Reasons the API gives when a point cannot be served (see `ServiceabilityReasons` in the API).
class ServiceabilityReasons {
  static const serviceable = 'Serviceable';
  static const outsideServiceArea = 'OutsideServiceArea';
  static const noStoreAvailable = 'NoStoreAvailable';
}

/// Whether anyone delivers to a point (`GET api/catalog/serviceability`).
class ServiceabilityResult {
  const ServiceabilityResult({required this.serviceable, required this.reason, this.nearestDistanceKm});
  final bool serviceable;
  final String reason;
  final double? nearestDistanceKm;
}

/// A delivery address the signed-in customer saved on the server.
class SavedAddress {
  const SavedAddress({
    required this.id,
    required this.label,
    required this.line,
    this.flatOrBuilding = '',
    this.landmark = '',
    required this.latitude,
    required this.longitude,
    required this.receiverName,
    required this.receiverPhone,
    this.isDefault = false,
    this.serviceable = true,
    this.serviceabilityReason = ServiceabilityReasons.serviceable,
  });

  final String id, label, line, flatOrBuilding, landmark, receiverName, receiverPhone, serviceabilityReason;
  final double latitude, longitude;
  final bool isDefault, serviceable;

  /// Flat or building followed by the address line, the way it is read out for a delivery.
  String get fullLine => flatOrBuilding.isEmpty ? line : '$flatOrBuilding, $line';
}

/// What the customer typed or picked when saving an address; the server validates it again.
class AddressDraft {
  const AddressDraft({
    required this.label,
    required this.line,
    this.flatOrBuilding = '',
    this.landmark = '',
    required this.latitude,
    required this.longitude,
    required this.receiverName,
    required this.receiverPhone,
  });

  final String label, line, flatOrBuilding, landmark, receiverName, receiverPhone;
  final double latitude, longitude;
}

/// Where the app delivers to right now: a saved address, or a place on this device only (guest, or "use my current location").
class DeliveryPlace {
  const DeliveryPlace({this.addressId, required this.label, required this.line, required this.latitude, required this.longitude});

  /// Set for a saved address; null for a device-only place.
  final String? addressId;
  final String label, line;
  final double latitude, longitude;

  bool get isSaved => addressId != null;

  /// The area name shown in the Home header: the part of the line before the city, when there is one.
  String get area {
    final parts = line.split(',').map((p) => p.trim()).where((p) => p.isNotEmpty).toList();
    return parts.length > 1 ? parts[parts.length > 2 ? parts.length - 2 : 0] : line;
  }

  Map<String, Object?> toJson() => {'label': label, 'line': line, 'latitude': latitude, 'longitude': longitude};

  static DeliveryPlace? fromJson(Object? json) {
    if (json is! Map) return null;
    final label = json['label'], line = json['line'], lat = json['latitude'], lng = json['longitude'];
    if (label is! String || line is! String || lat is! num || lng is! num) return null;
    return DeliveryPlace(label: label, line: line, latitude: lat.toDouble(), longitude: lng.toDouble());
  }
}


/// A message from the shop about an order (`api/customer/notifications`). [title] and [message] are the server's English text.
class AppNotification {
  const AppNotification({required this.id, this.orderId, required this.type, required this.title, required this.message, required this.isRead, required this.createdAt, this.category = 'Order'});
  final String id, type, title, message;

  /// Order, Payment, Offer or System: what kind of message it is (the icon, and whether it can be switched off).
  final String category;
  final String? orderId;
  final bool isRead;
  final DateTime createdAt;

  AppNotification asRead() => AppNotification(id: id, orderId: orderId, type: type, title: title, message: message, isRead: true, createdAt: createdAt, category: category);
}
