import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/models.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';

/// The ids of the products the signed-in customer saved, newest first. Empty for a guest.
class FavouritesState {
  const FavouritesState({this.ids = const [], this.loaded = false});
  final List<String> ids;

  /// False until the list was read once after signing in.
  final bool loaded;

  bool has(String productId) => ids.contains(productId);
}

class FavouritesNotifier extends Notifier<FavouritesState> {
  @override
  FavouritesState build() {
    ref.listen(authProvider, (previous, next) {
      final was = previous?.isSignedIn ?? false;
      if (next.isSignedIn && !was) {
        Future.microtask(reload);
      } else if (!next.isSignedIn && was) {
        state = const FavouritesState();
      }
    }, fireImmediately: true);
    return const FavouritesState();
  }

  Future<void> reload() async {
    if (!ref.read(authProvider).isSignedIn) return;
    try {
      final ids = await ref.read(favouritesRepositoryProvider).ids();
      if (ref.mounted) state = FavouritesState(ids: ids, loaded: true);
    } catch (_) {
      // Offline: the hearts stay as they were.
    }
  }

  /// Saves or removes a product. The heart changes at once; if the server refuses, it goes back. Returns false in that case.
  Future<bool> toggle(String productId) async {
    final saving = !state.has(productId);
    final before = state;
    state = FavouritesState(ids: saving ? [productId, ...before.ids] : [for (final id in before.ids) if (id != productId) id], loaded: true);
    try {
      final repo = ref.read(favouritesRepositoryProvider);
      if (saving) {
        await repo.add(productId);
      } else {
        await repo.remove(productId);
      }

      return true;
    } catch (_) {
      if (ref.mounted) state = before;
      return false;
    }
  }
}

final favouritesProvider = NotifierProvider<FavouritesNotifier, FavouritesState>(FavouritesNotifier.new);

/// The saved products themselves, for the Saved items screen, read for the store being shopped in. A product that cannot be read is left out.
final savedProductsProvider = FutureProvider.autoDispose<List<Product>>((ref) async {
  final ids = ref.watch(favouritesProvider.select((state) => state.ids));
  final storeId = ref.watch(selectedStoreProvider).value?.id; // rebuilds with the store's stock once the store is known
  final catalog = ref.watch(catalogRepositoryProvider);
  final found = await Future.wait(ids.map((id) async {
    try {
      return await catalog.product(id, storeId: storeId);
    } catch (_) {
      return null;
    }
  }));
  return [for (final product in found) ?product];
});
