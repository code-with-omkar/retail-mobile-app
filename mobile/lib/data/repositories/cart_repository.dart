import '../api/api_client.dart';
import '../dto/cart_dto.dart';
import '../dto/catalog_dto.dart';
import '../models.dart';
import 'local_shop.dart';

/// One line to add to the server's cart.
typedef CartLineRef = ({String productId, String? variantId, int quantity});

/// The signed-in customer's cart on the server, and the fee settings. Screens and the cart controller depend on this
/// interface, not on HTTP. A cart belongs to one store.
abstract interface class CartRepository {
  /// The fees the server charges (public, so a guest sees them too).
  Future<PricingSettings> pricing();

  /// The customer's cart in whichever store it is, or null when there is none.
  Future<ServerCart?> current();

  Future<ServerCart> add(String storeId, CartLineRef line);

  /// Sets a line to an exact quantity (1 or more).
  Future<ServerCart> setQuantity(String storeId, CartLineRef line);

  Future<ServerCart> remove(String storeId, {required String productId, String? variantId});

  /// Empties the cart.
  Future<void> clear(String storeId);

  /// Adds a guest's cart; the answer lists what could not be added or was reduced.
  Future<CartMergeResult> merge(String storeId, List<CartLineRef> lines);

  /// Brings the cart's prices up to date with the shop's current prices.
  Future<ServerCart> reprice(String storeId);
}

/// `api/carts/*` and `api/catalog/pricing`.
class ApiCartRepository implements CartRepository {
  ApiCartRepository(this._api);
  final ApiClient _api;

  static ServerCart _cart(Object? d) => serverCartFromJson(asObject(d));

  static String _variantQuery(String? variantId) => variantId == null ? '' : '?variantId=$variantId';

  @override
  Future<PricingSettings> pricing() => _api.get('/api/catalog/pricing', parse: (d) => pricingFromJson(asObject(d)));

  @override
  Future<ServerCart?> current() => _api.get('/api/carts/current', parse: (d) => d == null ? null : _cart(d));

  @override
  Future<ServerCart> add(String storeId, CartLineRef line) =>
      _api.post('/api/carts/$storeId/items', body: {'productId': line.productId, 'quantity': line.quantity, 'variantId': ?line.variantId}, parse: _cart);

  @override
  Future<ServerCart> setQuantity(String storeId, CartLineRef line) =>
      _api.put('/api/carts/$storeId/items/${line.productId}${_variantQuery(line.variantId)}', body: {'quantity': line.quantity}, parse: _cart);

  @override
  Future<ServerCart> remove(String storeId, {required String productId, String? variantId}) =>
      _api.delete('/api/carts/$storeId/items/$productId${_variantQuery(variantId)}', parse: _cart);

  @override
  Future<void> clear(String storeId) => _api.delete('/api/carts/$storeId', parse: (_) {});

  @override
  Future<CartMergeResult> merge(String storeId, List<CartLineRef> lines) => _api.post(
        '/api/carts/$storeId/merge',
        body: {
          'items': [for (final l in lines) {'productId': l.productId, 'quantity': l.quantity, 'variantId': ?l.variantId}]
        },
        parse: (d) {
          final j = asObject(d);
          return CartMergeResult(_cart(j['cart']), asList(j['notes'], cartNoteFromJson));
        },
      );

  @override
  Future<ServerCart> reprice(String storeId) => _api.post('/api/carts/$storeId/reprice', parse: _cart);
}

/// The cart without a server (offline seed mode and previews): it behaves like the server does, in memory.
class LocalCartRepository implements CartRepository {
  LocalCartRepository(this._shop);
  final LocalShop _shop;

  ServerCart _empty(String storeId) => ServerCart(storeId: storeId, lines: const [], pricing: _shop.pricing.price(0));

  @override
  Future<PricingSettings> pricing() async => _shop.pricing;

  @override
  Future<ServerCart?> current() async => _shop.cart();

  @override
  Future<ServerCart> add(String storeId, CartLineRef line) async {
    final key = line.variantId ?? line.productId;
    return _shop.setLine(storeId, key, _shop.quantityOf(key) + line.quantity);
  }

  @override
  Future<ServerCart> setQuantity(String storeId, CartLineRef line) async => _shop.setLine(storeId, line.variantId ?? line.productId, line.quantity);

  @override
  Future<ServerCart> remove(String storeId, {required String productId, String? variantId}) async => _shop.setLine(storeId, variantId ?? productId, 0);

  @override
  Future<void> clear(String storeId) async => _shop.clearCart();

  @override
  Future<CartMergeResult> merge(String storeId, List<CartLineRef> lines) async {
    final notes = <CartNote>[];
    for (final l in lines) {
      final key = l.variantId ?? l.productId;
      if (_shop.lookup(key) == null) {
        notes.add(CartNote(productId: l.productId, variantId: l.variantId, name: '', kind: CartNote.unavailable, quantity: l.quantity));
        continue;
      }
      _shop.setLine(storeId, key, _shop.quantityOf(key) + l.quantity);
    }
    return CartMergeResult(_shop.cart() ?? _empty(storeId), notes);
  }

  @override
  Future<ServerCart> reprice(String storeId) async => _shop.reprice(storeId) ?? _empty(storeId);
}
