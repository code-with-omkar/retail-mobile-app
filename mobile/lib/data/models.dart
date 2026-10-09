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
  const CartLine(this.product, this.quantity, [this.pack]);
  final Product product;
  final int quantity;
  final PackOption? pack;

  double get unitPrice => pack?.price ?? product.price;
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

enum OrderStage { placed, packed, onTheWay, delivered }

class Order {
  const Order({
    required this.id,
    required this.lines,
    required this.total,
    required this.placedAt,
    required this.stage,
    required this.address,
    required this.payment,
  });
  final String id, address, payment;
  final List<CartLine> lines;
  final double total;
  final DateTime placedAt;
  final OrderStage stage;

  int get itemCount => lines.fold(0, (s, l) => s + l.quantity);
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
