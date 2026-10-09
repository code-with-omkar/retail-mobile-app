import '../models.dart';

/// In-memory lookup of products the app has already loaded. The cart and reorder flows read
/// products from here, so they do not need a network call for every cart line. A cart key is either a
/// product id or one of its pack ids, and both find the product.
class ProductStore {
  final _byKey = <String, Product>{};

  Product? get(String key) => _byKey[key];
  void put(Product product) {
    _byKey[product.id] = product;
    for (final pack in product.packs) {
      _byKey[pack.id] = product;
    }
  }

  void putAll(Iterable<Product> products) {
    for (final p in products) {
      put(p);
    }
  }
}
