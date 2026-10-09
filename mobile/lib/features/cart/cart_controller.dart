import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/seed.dart';

/// Cart quantities keyed by [cartKey]: the pack (variant) id, or the product id for a product without packs.
class CartNotifier extends Notifier<Map<String, int>> {
  @override
  Map<String, int> build() => {};

  void add(String key, [int by = 1]) => state = {...state, key: (state[key] ?? 0) + by};

  void remove(String key) {
    final next = (state[key] ?? 0) - 1;
    final copy = {...state};
    if (next <= 0) {
      copy.remove(key);
    } else {
      copy[key] = next;
    }
    state = copy;
  }

  void delete(String key) => state = {...state}..remove(key);
  void clear() => state = {};
}

final cartProvider = NotifierProvider<CartNotifier, Map<String, int>>(CartNotifier.new);

/// Builds a cart line from its key using products the app has already loaded. Returns null if the
/// product is unknown (for example the cache was cleared), so a stale key never crashes the cart.
CartLine? lineFromKey(String key, int qty, Product? Function(String key) lookup) {
  final product = lookup(key);
  if (product == null) return null;
  return CartLine(product, qty, product.packs.where((p) => p.id == key).firstOrNull);
}

class CartTotals {
  const CartTotals(this.lines);
  final List<CartLine> lines;

  int get count => lines.fold(0, (s, l) => s + l.quantity);
  double get subtotal => lines.fold(0.0, (s, l) => s + l.total);
  double get savings => lines.fold(0.0, (s, l) => s + (l.unitMrp - l.unitPrice) * l.quantity);
  bool get isEmpty => lines.isEmpty;
  int get delivery => isEmpty || subtotal >= freeDeliveryThreshold ? 0 : deliveryFee;
  int get handling => isEmpty ? 0 : handlingFee;
  double get total => subtotal + delivery + handling;
  double get awayFromFreeDelivery => isEmpty || subtotal >= freeDeliveryThreshold ? 0 : freeDeliveryThreshold - subtotal;
}

final cartTotalsProvider = Provider<CartTotals>((ref) {
  final cart = ref.watch(cartProvider);
  final store = ref.watch(productStoreProvider);
  return CartTotals([
    for (final e in cart.entries)
      ?lineFromKey(e.key, e.value, store.get),
  ]);
});
