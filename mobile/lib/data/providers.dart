import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/config/app_config.dart';
import '../core/prefs.dart';
import 'api/api_client.dart';
import '../features/address/address_controller.dart';
import '../features/auth/auth_controller.dart';
import 'models.dart';
import 'session_store.dart';
import 'repositories/auth_repository.dart';
import 'repositories/catalog_repository.dart';
import 'repositories/health_repository.dart';
import 'repositories/product_store.dart';

/// Overridden in tests to point at a fake environment.
final appConfigProvider = Provider<AppConfig>((ref) => AppConfig.fromEnvironment());

/// Lets the API client use the current session without depending on the repository that uses the API client.
class SessionBinding {
  AuthRepository? _auth;
  void attach(AuthRepository auth) => _auth = auth;
  Future<String?> accessToken() async => _auth?.accessToken();
  Future<bool> refresh() async => await _auth?.refreshSession() ?? false;
}

final sessionBindingProvider = Provider<SessionBinding>((ref) => SessionBinding());

/// Tests replace the network layer by overriding this.
final httpClientAdapterProvider = Provider<HttpClientAdapter?>((ref) => null);

final apiClientProvider = Provider<ApiClient>((ref) {
  final config = ref.watch(appConfigProvider);
  final adapter = ref.watch(httpClientAdapterProvider);
  final binding = ref.watch(sessionBindingProvider);
  // The language is read lazily so switching it applies to the next request.
  return ApiClient(
    config: config,
    dio: adapter == null ? null : (Dio()..httpClientAdapter = adapter),
    languageCode: () => ref.read(localeProvider).languageCode,
    tokenProvider: binding.accessToken,
    onUnauthorized: binding.refresh,
  );
});

/// Overridden in tests with a [MemorySessionStore].
final sessionStoreProvider = Provider<SessionStore>((ref) => const SecureSessionStore());

final authRepositoryProvider = Provider<AuthRepository>((ref) {
  final repo = AuthRepository(
    ref.watch(apiClientProvider),
    ref.watch(sessionStoreProvider),
    onSessionEnded: () => ref.read(authProvider.notifier).sessionEnded(),
  );
  ref.read(sessionBindingProvider).attach(repo);
  return repo;
});

final healthRepositoryProvider = Provider<HealthRepository>((ref) => HealthRepository(ref.watch(apiClientProvider)));

/// Products loaded so far, by id. Used by the cart and reorder.
final productStoreProvider = Provider<ProductStore>((ref) => ProductStore());

final catalogRepositoryProvider = Provider<CatalogRepository>((ref) {
  final config = ref.watch(appConfigProvider);
  final CatalogRepository inner = config.useSeedData ? const SeedCatalogRepository() : ApiCatalogRepository(ref.watch(apiClientProvider));
  return CachingCatalogRepository(inner, ref.watch(productStoreProvider));
});

/// Rounds a coordinate to 3 decimals (about 110 m) before it is sent, so precise locations do not reach server logs.
double roundCoordinate(double value) => (value * 1000).round() / 1000;

/// Where to deliver, rounded for the catalogue calls, or null while no place is chosen (see `deliveryStateProvider`).
final deliveryLocationProvider = Provider<({double latitude, double longitude})?>((ref) {
  final place = ref.watch(deliveryPlaceProvider);
  if (place == null) return null;
  return (latitude: roundCoordinate(place.latitude), longitude: roundCoordinate(place.longitude));
});

/// Every store that delivers to the delivery location, nearest first.
final serviceableStoresProvider = FutureProvider<List<NearestStore>>((ref) {
  final where = ref.watch(deliveryLocationProvider);
  if (where == null) return const <NearestStore>[]; // no place chosen: nothing to look up
  return ref.watch(catalogRepositoryProvider).stores(latitude: where.latitude, longitude: where.longitude);
});

/// The store the customer chose (id), remembered between launches. Null means "use the nearest".
class SelectedStoreNotifier extends Notifier<String?> {
  static const _key = 'selected_store_id';

  @override
  String? build() => ref.read(sharedPrefsProvider).getString(_key);

  void select(String id) {
    state = id;
    ref.read(sharedPrefsProvider).setString(_key, id);
  }
}

final selectedStoreIdProvider = NotifierProvider<SelectedStoreNotifier, String?>(SelectedStoreNotifier.new);

/// The store whose catalogue the app shows: the saved choice while it still delivers here, otherwise the nearest
/// store. Null when no store delivers to the location.
final selectedStoreProvider = FutureProvider<NearestStore?>((ref) async {
  final stores = await ref.watch(serviceableStoresProvider.future);
  if (stores.isEmpty) return null;
  final saved = ref.watch(selectedStoreIdProvider);
  return stores.firstWhere((s) => s.id == saved, orElse: () => stores.first);
});

/// Store id used to show stock. A failed or empty store lookup means "unknown", never a failed product list.
Future<String?> _storeIdOf(Ref ref) async {
  try {
    return (await ref.watch(selectedStoreProvider.future))?.id;
  } catch (_) {
    return null;
  }
}

/// For screens that page through products themselves (search, category).
final storeIdResolverProvider = Provider<Future<String?> Function()>((ref) => () async {
      try {
        return (await ref.read(selectedStoreProvider.future))?.id;
      } catch (_) {
        return null;
      }
    });

/// Categories of the selected store (all categories when there is no store).
final categoriesProvider = FutureProvider<List<Category>>((ref) async {
  final storeId = await _storeIdOf(ref);
  return ref.watch(catalogRepositoryProvider).categories(storeId: storeId);
});

/// First page of products for the Home "Popular near you" row.
final popularProductsProvider = FutureProvider<List<Product>>((ref) async {
  final storeId = await _storeIdOf(ref);
  return (await ref.watch(catalogRepositoryProvider).products(storeId: storeId, carriedOnly: storeId != null, pageSize: 10)).items;
});

/// Product detail with fresh stock for the nearest store.
final productProvider = FutureProvider.family<Product, String>((ref, id) async {
  final storeId = await _storeIdOf(ref);
  return ref.watch(catalogRepositoryProvider).product(id, storeId: storeId);
});

/// Other products in the same category, for "You might also like".
final relatedProductsProvider = FutureProvider.family<List<Product>, ({String categoryId, String excludeId})>((ref, q) async {
  final storeId = await _storeIdOf(ref);
  final page = await ref.watch(catalogRepositoryProvider).products(categoryId: q.categoryId, storeId: storeId, carriedOnly: storeId != null, pageSize: 10);
  return page.items.where((p) => p.id != q.excludeId).toList();
});
