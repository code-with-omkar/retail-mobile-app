import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/repositories/catalog_repository.dart';
import '../../data/repositories/offers_repository.dart';
import '../auth/auth_controller.dart';

/// Running discounts and picture messages at the store being shopped in. A failure just means no banners.
final activeOffersProvider = FutureProvider.autoDispose<List<Offer>>((ref) async {
  final storeId = ref.watch(selectedStoreProvider).value?.id;
  if (storeId == null) return const [];
  try {
    return await ref.watch(offersRepositoryProvider).active(storeId);
  } catch (_) {
    return const [];
  }
});

/// One offer by id (null when it is gone). Errors surface to the screen so it can offer a retry.
final offerProvider = FutureProvider.autoDispose.family<Offer?, String>((ref, id) => ref.watch(offersRepositoryProvider).offer(id));

/// The stores the signed-in customer bought from last (up to four). Empty when signed out.
final recentStoresProvider = FutureProvider.autoDispose<List<RecentStore>>((ref) async {
  if (!ref.watch(authProvider.select((a) => a.isSignedIn))) return const [];
  try {
    return await ref.watch(offersRepositoryProvider).recentStores();
  } catch (_) {
    return const [];
  }
});

/// An offer's products with the prices of the store being shopped in (the discount is already in them). The key is the ids joined by commas.
final offerProductsProvider = FutureProvider.autoDispose.family<List<Product>, String>((ref, joinedIds) async {
  final storeId = ref.watch(selectedStoreProvider).value?.id;
  final catalog = ref.watch(catalogRepositoryProvider);
  final found = await Future.wait(joinedIds.split(',').where((id) => id.isNotEmpty).map((id) async {
    try {
      return await catalog.product(id, storeId: storeId);
    } catch (_) {
      return null;
    }
  }));
  return [for (final product in found) ?product];
});

/// What the signed-in customer bought before at the store being shopped in, with today's price and stock there.
final boughtBeforeProvider = FutureProvider.autoDispose<List<Product>>((ref) async {
  if (!ref.watch(authProvider.select((a) => a.isSignedIn))) return const [];
  final storeId = ref.watch(selectedStoreProvider).value?.id;
  if (storeId == null) return const [];
  try {
    final catalog = ref.watch(catalogRepositoryProvider);
    final keys = {for (final c in await catalog.categories()) c.id: categoryKeyFor(c.label)};
    final base = ref.watch(apiClientProvider).baseUrl;
    final products = [for (final d in await ref.watch(offersRepositoryProvider).boughtBefore(storeId)) productFromCatalogDto(d, keys[d.categoryId] ?? 'other', imageBase: base)];
    ref.read(productStoreProvider).putAll(products); // so the cart can find them
    return products;
  } catch (_) {
    return const [];
  }
});

/// Offers already shown as a pop-up during this run of the app, so each pops up once.
class ShownLoadOffers extends Notifier<Set<String>> {
  @override
  Set<String> build() => <String>{};

  void mark(String id) => state = {...state, id};
}

final shownLoadOffersProvider = NotifierProvider<ShownLoadOffers, Set<String>>(ShownLoadOffers.new);
