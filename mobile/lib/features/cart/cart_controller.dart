import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/api/api_exception.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/repositories/cart_repository.dart';
import '../address/address_controller.dart';
import '../auth/auth_controller.dart';

/// What the cart says to the customer after something did not go as asked. Shown once on the cart screen.
class CartNotice {
  const CartNotice({this.problem, this.notes = const []});

  /// An English message key for a change the server refused or that could not be sent (translated when shown).
  final String? problem;

  /// Lines a merge could not add, or added fewer of.
  final List<CartNote> notes;

  bool get isEmpty => problem == null && notes.isEmpty;
}

class CartNoticeNotifier extends Notifier<CartNotice> {
  @override
  CartNotice build() => const CartNotice();

  void problem(String messageKey) => state = CartNotice(problem: messageKey, notes: state.notes);
  void notes(List<CartNote> notes) => state = CartNotice(problem: state.problem, notes: [...state.notes, ...notes]);
  void dismiss() => state = const CartNotice();
}

final cartNoticeProvider = NotifierProvider<CartNoticeNotifier, CartNotice>(CartNoticeNotifier.new);

/// The customer's cart as the server last confirmed it (prices, stock, fees). Null for a guest or before it is read.
class ServerCartNotifier extends Notifier<ServerCart?> {
  @override
  ServerCart? build() => null;
  void set(ServerCart? cart) => state = cart;
}

final serverCartProvider = NotifierProvider<ServerCartNotifier, ServerCart?>(ServerCartNotifier.new);

/// A signed-in customer who also has a different cart on this phone: which one to keep.
class CartChoice {
  const CartChoice({required this.saved, required this.phone});

  /// The cart already on the server (in another store).
  final ServerCart saved;

  /// The cart built on this phone before signing in, as quantities by cart key.
  final Map<String, int> phone;
}

class CartChoiceNotifier extends Notifier<CartChoice?> {
  @override
  CartChoice? build() => null;
  void set(CartChoice? choice) => state = choice;
}

final cartChoiceProvider = NotifierProvider<CartChoiceNotifier, CartChoice?>(CartChoiceNotifier.new);

/// The fees the server charges. Null while loading or when they could not be read (then fees are not shown).
final pricingProvider = FutureProvider<PricingSettings>((ref) => ref.watch(cartRepositoryProvider).pricing());

/// Cart quantities keyed by [cartKey]: the pack (variant) id, or the product id for a product without packs.
///
/// A guest's cart lives here only. A signed-in customer's cart also lives on the server: changes show at once and are sent in the
/// background (one at a time, in order); if the server refuses one, the cart goes back to what the server holds and the
/// customer is told. Signing in merges the phone's cart into the server's; signing out leaves the server's cart where it is.
class CartNotifier extends Notifier<Map<String, int>> {
  /// What the server last confirmed, by key.
  Map<String, int> _confirmed = {};
  String? _serverStoreId;
  var _running = false;
  var _signingIn = false;

  /// A cart built on this phone that signing in could not put on the server yet (no store was known). Tried again on the next reload,
  /// and never thrown away meanwhile.
  Map<String, int>? _unmerged;

  /// The store the customer was shopping in when they last added something: the store a cart built on this phone belongs to.
  String? _shoppingStoreId;

  CartRepository get _repo => ref.read(cartRepositoryProvider);
  bool get _signedIn => ref.read(authProvider).isSignedIn;

  @override
  Map<String, int> build() {
    ref.listen(authProvider, (previous, next) {
      final was = previous?.isSignedIn ?? false;
      if (next.isSignedIn && !was) {
        _signingIn = true; // set now, so nothing reads the server cart before the phone cart is merged
        Future.microtask(() => ref.mounted ? _onSignedIn() : (_signingIn = false));
      } else if (!next.isSignedIn && was) {
        _onSignedOut();
      }
    }, fireImmediately: true);
    return {};
  }

  /// True when no store delivers to the chosen place: nothing can be added to a cart then. The customer is told why.
  bool _noStoreHere() {
    final store = ref.read(selectedStoreProvider);
    if (!store.hasValue || store.value != null) return false;
    // No place chosen yet is a different thing to say than a place nobody delivers to.
    ref.read(cartNoticeProvider.notifier).problem(ref.read(deliveryPlaceProvider) == null ? 'Choose your delivery location first.' : 'We do not deliver to your location yet. Choose another address.');
    return true;
  }

  void add(String key, [int by = 1]) {
    if (_noStoreHere()) return;
    _noteStore();
    state = {...state, key: (state[key] ?? 0) + by};
    _sync();
  }

  void remove(String key) {
    final next = (state[key] ?? 0) - 1;
    final copy = {...state};
    if (next <= 0) {
      copy.remove(key);
    } else {
      copy[key] = next;
    }
    state = copy;
    _sync();
  }

  void delete(String key) {
    state = {...state}..remove(key);
    _sync();
  }

  /// Sets a line to an exact quantity; zero removes it.
  void setQuantity(String key, int quantity) {
    if (quantity > (state[key] ?? 0) && _noStoreHere()) return;
    _noteStore();
    final copy = {...state};
    if (quantity <= 0) {
      copy.remove(key);
    } else {
      copy[key] = quantity;
    }
    state = copy;
    _sync();
  }

  /// Empties the cart, here and on the server.
  void clear() {
    state = {};
    if (_signedIn && _serverStoreId != null && _confirmed.isNotEmpty) {
      final storeId = _serverStoreId!;
      _confirmed = {};
      _serverStoreId = null;
      ref.read(serverCartProvider.notifier).set(null);
      _running = true;
      _repo.clear(storeId).catchError((Object e) {
        _problem(e);
        return _reload();
      }).whenComplete(() {
        _running = false;
        _sync();
      });
    }
  }

  /// The order was placed: the server emptied the cart itself.
  void orderPlaced() {
    state = {};
    _confirmed = {};
    _serverStoreId = null;
    ref.read(serverCartProvider.notifier).set(null);
  }

  /// Reads the server's cart again (when the cart or checkout opens, and after a refusal).
  Future<void> reload() async {
    if (!_signedIn) return;
    await settled();
    if (!ref.mounted || _running) return;
    if (_unmerged != null) {
      await _onSignedIn();
      if (_unmerged != null || !ref.mounted) return;
    }
    await _reload();
  }

  /// Completes when nothing is being sent or merged, so checkout never reads the server's cart halfway through a change.
  Future<void> settled() async {
    for (var waited = 0; (_running || _signingIn) && waited < 400 && ref.mounted; waited++) {
      await Future<void>.delayed(const Duration(milliseconds: 25));
    }
  }

  /// "Remove unavailable items": takes out the lines the shop no longer sells.
  void removeUnavailable() {
    for (final line in ref.read(serverCartProvider)?.lines.where((l) => l.unavailable) ?? const <ServerCartLine>[]) {
      delete(line.key);
    }
  }

  /// "Use available quantity": lowers each short line to what the store has left (removing it when none is left).
  void useAvailableQuantities() {
    for (final line in ref.read(serverCartProvider)?.lines.where((l) => !l.unavailable && l.available != null && l.quantity > l.available!) ?? const <ServerCartLine>[]) {
      setQuantity(line.key, line.available!);
    }
  }

  /// Brings the server's prices up to date after the customer has seen and accepted the change.
  Future<void> reprice() async {
    final storeId = _serverStoreId;
    if (!_signedIn || storeId == null) return;
    try {
      _applyServer(await _repo.reprice(storeId));
      state = Map.of(_confirmed);
    } catch (e) {
      _problem(e);
    }
  }

  // ---- talking to the server ----

  void _sync() {
    if (!_signedIn || _running) return;
    _running = true;
    _reconcile().whenComplete(() => _running = false);
  }

  /// Sends the difference between what the customer wants and what the server holds, one line at a time, until they agree.
  Future<void> _reconcile() async {
    for (var guard = 0; guard < 100 && ref.mounted; guard++) {
      final wanted = state;
      final key = {...wanted.keys, ..._confirmed.keys}.where((k) => (wanted[k] ?? 0) != (_confirmed[k] ?? 0)).firstOrNull;
      if (key == null) return;
      try {
        _applyServer(await _send(key, wanted[key] ?? 0));
      } catch (e) {
        _problem(e);
        await _reload();
        return;
      }
      if (!ref.mounted) return;
      // The server may hold a different number than asked for; show what it holds rather than asking again for ever.
      if ((_confirmed[key] ?? 0) != (wanted[key] ?? 0) && identical(state, wanted)) {
        final copy = {...state};
        final held = _confirmed[key] ?? 0;
        held == 0 ? copy.remove(key) : copy[key] = held;
        state = copy;
      }
    }
  }

  Future<ServerCart> _send(String key, int quantity) async {
    final product = ref.read(productStoreProvider).get(key);
    final productId = product?.id ?? key;
    final variantId = product != null && product.packs.any((p) => p.id == key) ? key : null;
    final storeId = _serverStoreId ?? await _selectedStoreId();
    if (storeId == null) throw StateError('No store to put the cart in');
    final line = (productId: productId, variantId: variantId, quantity: quantity);
    if (quantity <= 0) return _repo.remove(storeId, productId: productId, variantId: variantId);
    return (_confirmed[key] ?? 0) == 0 ? _repo.add(storeId, line) : _repo.setQuantity(storeId, line);
  }

  void _noteStore() => _shoppingStoreId = ref.read(selectedStoreProvider).value?.id ?? _shoppingStoreId;

  /// The store to put a cart in: the one being shopped in, or the selected one.
  Future<String?> _selectedStoreId() async {
    if (_shoppingStoreId != null) return _shoppingStoreId;
    try {
      return (await ref.read(selectedStoreProvider.future))?.id;
    } catch (_) {
      return null;
    }
  }

  void _applyServer(ServerCart cart) {
    _confirmed = cart.quantities;
    _serverStoreId = cart.storeId;
    ref.read(serverCartProvider.notifier).set(cart);
  }

  void _problem(Object e) => ref.read(cartNoticeProvider.notifier).problem(switch (e) {
        ApiException() => e.userMessageKey,
        StateError() => 'We do not deliver to your location yet. Choose another address.', // no store to put the cart in
        _ => 'Something went wrong. Please try again.',
      });

  /// Replaces the phone's view with the server's. If the server cannot be reached, the last confirmed cart stays.
  Future<void> _reload() async {
    try {
      final cart = await _repo.current();
      if (!ref.mounted) return;
      if (cart == null) {
        _confirmed = {};
        _serverStoreId = null;
        ref.read(serverCartProvider.notifier).set(null);
        state = {};
      } else {
        await _loadProducts(cart);
        if (!ref.mounted) return;
        _applyServer(cart);
        state = Map.of(_confirmed);
      }
    } catch (_) {
      if (ref.mounted) state = Map.of(_confirmed);
    }
  }

  // ---- signing in and out ----

  Future<void> _onSignedIn() async {
    _signingIn = true;
    try {
      final server = await _repo.current();
      final phone = _unmerged ?? Map.of(state);
      if (!ref.mounted) return;
      final selected = await _selectedStoreId();
      if (!ref.mounted) return;
      if (phone.isEmpty) {
        if (server != null) await _adopt(server);
        return;
      }
      final storeId = server?.storeId ?? selected;
      if (storeId == null) {
        _unmerged = phone; // no store to put it in yet: the phone cart stays as it is until there is one
        return;
      }
      _unmerged = null;
      if (server != null && server.lines.isNotEmpty && server.storeId != selected) {
        // Two carts in two stores: the customer chooses which to keep (one cart belongs to one store).
        ref.read(cartChoiceProvider.notifier).set(CartChoice(saved: server, phone: phone));
        return;
      }
      await _mergePhoneCart(storeId, phone);
    } catch (e) {
      if (ref.mounted) _problem(e);
    } finally {
      _signingIn = false;
    }
  }

  Future<void> _mergePhoneCart(String storeId, Map<String, int> phone) async {
    final lines = <CartLineRef>[];
    for (final e in phone.entries) {
      final product = ref.read(productStoreProvider).get(e.key);
      lines.add((productId: product?.id ?? e.key, variantId: product != null && product.packs.any((p) => p.id == e.key) ? e.key : null, quantity: e.value));
    }
    final result = await _repo.merge(storeId, lines);
    if (!ref.mounted) return;
    if (result.notes.isNotEmpty) ref.read(cartNoticeProvider.notifier).notes(result.notes);
    await _adopt(result.cart);
  }

  Future<void> _adopt(ServerCart cart) async {
    await _loadProducts(cart);
    if (!ref.mounted) return;
    _applyServer(cart);
    state = Map.of(_confirmed);
    // The cart belongs to its store: shop in it.
    final stores = await ref.read(serviceableStoresProvider.future).catchError((_) => const <NearestStore>[]);
    if (ref.mounted && stores.any((s) => s.id == cart.storeId)) ref.read(selectedStoreIdProvider.notifier).select(cart.storeId);
  }

  /// "Keep my saved cart": the phone's cart is dropped.
  Future<void> keepSavedCart() async {
    final choice = ref.read(cartChoiceProvider);
    ref.read(cartChoiceProvider.notifier).set(null);
    if (choice != null) await _adopt(choice.saved);
  }

  /// "Use the cart from this phone": the saved cart is emptied and the phone's cart takes its place.
  Future<void> usePhoneCart() async {
    final choice = ref.read(cartChoiceProvider);
    ref.read(cartChoiceProvider.notifier).set(null);
    final storeId = await _selectedStoreId();
    if (choice == null || storeId == null) return;
    try {
      await _repo.clear(choice.saved.storeId);
      await _mergePhoneCart(storeId, choice.phone);
    } catch (e) {
      if (ref.mounted) _problem(e);
    }
  }

  void _onSignedOut() {
    _unmerged = null;
    state = {};
    _confirmed = {};
    _serverStoreId = null;
    ref.read(serverCartProvider.notifier).set(null);
    ref.read(cartChoiceProvider.notifier).set(null);
  }

  /// Makes sure the products of the server's cart are known to the app, falling back to what the cart itself says.
  Future<void> _loadProducts(ServerCart cart) async {
    final store = ref.read(productStoreProvider);
    final catalog = ref.read(catalogRepositoryProvider);
    for (final line in cart.lines) {
      if (store.get(line.key) != null) continue;
      try {
        await catalog.product(line.productId, storeId: cart.storeId);
      } catch (_) {
        // not reachable: show the line from what the cart holds
      }
      if (store.get(line.key) == null) store.put(productFromCartLine(line));
    }
  }
}

final cartProvider = NotifierProvider<CartNotifier, Map<String, int>>(CartNotifier.new);

/// A stand-in product for a cart line the catalogue could not provide, so the line still shows and can be changed or removed.
Product productFromCartLine(ServerCartLine line) => Product(
      id: line.productId,
      name: line.name,
      nameMr: line.name,
      unit: line.label,
      price: line.unitPrice,
      mrp: line.unitPrice,
      color: const Color(0xFFE0E0E0),
      category: 'other',
      description: '',
      packs: line.variantId == null ? const [] : [PackOption(id: line.variantId!, label: line.label, price: line.unitPrice, mrp: line.unitPrice, isDefault: true)],
    );

/// Builds a cart line from its key using products the app has already loaded. Returns null if the
/// product is unknown (for example the cache was cleared), so a stale key never crashes the cart.
CartLine? lineFromKey(String key, int qty, Product? Function(String key) lookup) {
  final product = lookup(key);
  if (product == null) return null;
  return CartLine(product, qty, product.packs.where((p) => p.id == key).firstOrNull);
}

class CartTotals {
  const CartTotals(this.lines, [this.settings]);
  final List<CartLine> lines;

  /// The server's fee settings; null while they are loading or could not be read.
  final PricingSettings? settings;

  int get count => lines.fold(0, (s, l) => s + l.quantity);
  double get subtotal => lines.fold(0.0, (s, l) => s + l.total);
  double get savings => lines.fold(0.0, (s, l) => s + (l.unitMrp - l.unitPrice) * l.quantity);
  bool get isEmpty => lines.isEmpty;

  bool get feesKnown => settings != null;
  CartPricing get _price => settings?.price(subtotal) ?? CartPricing(subtotal: subtotal, deliveryFee: 0, handlingFee: 0, total: subtotal, freeDeliveryThreshold: 0, amountToFreeDelivery: 0);
  double get delivery => isEmpty ? 0 : _price.deliveryFee;
  double get handling => isEmpty ? 0 : _price.handlingFee;
  double get total => isEmpty ? 0 : _price.total;
  double get freeDeliveryThreshold => _price.freeDeliveryThreshold;
  double get awayFromFreeDelivery => isEmpty ? 0 : _price.amountToFreeDelivery;

  /// Lines that would stop checkout, so the customer can fix them before trying.
  List<CartLine> get priceChanged => [for (final l in lines) if (l.priceChanged && !l.unavailable) l];
  List<CartLine> get unavailable => [for (final l in lines) if (l.unavailable) l];
  List<CartLine> get notEnoughStock => [for (final l in lines) if (!l.unavailable && l.notEnoughStock) l];
  bool get hasProblems => priceChanged.isNotEmpty || unavailable.isNotEmpty || notEnoughStock.isNotEmpty;
}

final cartTotalsProvider = Provider<CartTotals>((ref) {
  final cart = ref.watch(cartProvider);
  final store = ref.watch(productStoreProvider);
  final server = ref.watch(serverCartProvider);
  final serverLines = {if (server != null) for (final l in server.lines) l.key: l};
  return CartTotals([
    for (final e in cart.entries)
      ?_withServer(lineFromKey(e.key, e.value, store.get), serverLines[e.key]),
  ], ref.watch(pricingProvider).value);
});

CartLine? _withServer(CartLine? line, ServerCartLine? server) => line == null || server == null ? line : line.withServer(server);
