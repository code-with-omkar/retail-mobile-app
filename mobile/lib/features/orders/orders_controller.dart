import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/models.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';

/// Whether the last quiet check of the orders worked. When it did not, the screen keeps showing what it had and says so.
class OrdersSyncNotifier extends Notifier<bool> {
  @override
  bool build() => false;
  void failed(bool value) => state = value;
}

final ordersSyncFailingProvider = NotifierProvider<OrdersSyncNotifier, bool>(OrdersSyncNotifier.new);

/// The signed-in customer's orders, active ones first and then the finished ones, each newest first. A guest has none.
class OrdersNotifier extends AsyncNotifier<List<Order>> {
  @override
  Future<List<Order>> build() async {
    final signedIn = ref.watch(authProvider.select((a) => a.isSignedIn));
    if (!signedIn) return const [];
    return _sorted(await ref.watch(orderRepositoryProvider).list());
  }

  /// Active orders (still changing) first, finished ones after.
  static List<Order> _sorted(List<Order> orders) {
    final sorted = [...orders]..sort((a, b) {
        if (a.stage.isActive != b.stage.isActive) return a.stage.isActive ? -1 : 1;
        return b.placedAt.compareTo(a.placedAt);
      });
    return sorted;
  }

  /// Reads the orders again (pull to refresh, and after placing an order). A failure is shown as an error.
  Future<void> refresh() async {
    if (!ref.read(authProvider).isSignedIn) return;
    state = await AsyncValue.guard(() async => _sorted(await ref.read(orderRepositoryProvider).list()));
  }

  /// A quiet check for changes (the shop accepted an order). The list on screen is replaced only when it worked; a failure keeps
  /// what is shown, is remembered in [ordersSyncFailingProvider] and is thrown so the caller can slow down.
  Future<void> poll() async {
    if (!ref.read(authProvider).isSignedIn) return;
    try {
      final list = _sorted(await ref.read(orderRepositoryProvider).list());
      if (!ref.mounted) return;
      state = AsyncData(list);
      ref.read(ordersSyncFailingProvider.notifier).failed(false);
    } catch (_) {
      if (ref.mounted) ref.read(ordersSyncFailingProvider.notifier).failed(true);
      rethrow;
    }
  }

  /// A quiet check of one order, put into the list in place of the old one. Throws on failure, like [poll].
  Future<void> pollOne(String id) async {
    if (!ref.read(authProvider).isSignedIn) return;
    try {
      final order = await ref.read(orderRepositoryProvider).get(id);
      if (!ref.mounted) return;
      replace(order);
      ref.read(ordersSyncFailingProvider.notifier).failed(false);
    } catch (_) {
      if (ref.mounted) ref.read(ordersSyncFailingProvider.notifier).failed(true);
      rethrow;
    }
  }

  /// Puts this order into the list (a new one at the top of its group, or in place of the old one).
  void replace(Order order) {
    final current = state.value ?? const <Order>[];
    state = AsyncData(_sorted([order, ...current.where((o) => o.id != order.id)]));
  }

  /// A just-placed order shows at the top at once, without waiting for another read.
  void placed(Order order) => replace(order);

  /// Cancels the order on the server and shows the result. Throws what the server said (for example a conflict when it is too late).
  Future<Order> cancel(String id) async {
    final order = await ref.read(orderRepositoryProvider).cancel(id);
    if (ref.mounted) replace(order);
    return order;
  }
}

final ordersProvider = AsyncNotifierProvider<OrdersNotifier, List<Order>>(OrdersNotifier.new);

/// Whether any order is still on its way, so the list is worth checking again.
final hasActiveOrderProvider = Provider<bool>((ref) => ref.watch(ordersProvider).value?.any((o) => o.stage.isActive) ?? false);

/// The order that was just placed, for the success screen (its number and total) before the list is read again.
class LastPlacedOrderNotifier extends Notifier<Order?> {
  @override
  Order? build() => null;
  void set(Order? order) => state = order;
}

final lastPlacedOrderProvider = NotifierProvider<LastPlacedOrderNotifier, Order?>(LastPlacedOrderNotifier.new);

/// One order, from the list when it is already there, otherwise from the server.
final orderProvider = FutureProvider.family<Order, String>((ref, id) async {
  final known = ref.watch(ordersProvider).value?.where((o) => o.id == id).firstOrNull ?? ref.watch(lastPlacedOrderProvider.select((o) => o?.id == id ? o : null));
  if (known != null) return known;
  return ref.watch(orderRepositoryProvider).get(id);
});

/// Where "now" comes from for "arriving in N minutes"; tests replace it.
final orderClockProvider = Provider<DateTime Function()>((ref) => DateTime.now);
