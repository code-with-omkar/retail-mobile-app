import '../api/api_client.dart';

/// The products a signed-in customer saved (`api/customer/favourites`). Newest first.
abstract class FavouritesRepository {
  Future<List<String>> ids();
  Future<void> add(String productId);
  Future<void> remove(String productId);
}

class ApiFavouritesRepository implements FavouritesRepository {
  ApiFavouritesRepository(this._api);
  final ApiClient _api;

  @override
  Future<List<String>> ids() => _api.get('/api/customer/favourites/ids', parse: (d) => [for (final id in (d as List? ?? const [])) '$id']);

  @override
  Future<void> add(String productId) => _api.put('/api/customer/favourites/$productId', parse: (_) {});

  @override
  Future<void> remove(String productId) => _api.delete('/api/customer/favourites/$productId', parse: (_) {});
}

/// Offline development and previews: the list lives in memory, newest first.
class LocalFavouritesRepository implements FavouritesRepository {
  final _ids = <String>[];

  @override
  Future<List<String>> ids() async => List.of(_ids);

  @override
  Future<void> add(String productId) async {
    if (!_ids.contains(productId)) _ids.insert(0, productId);
  }

  @override
  Future<void> remove(String productId) async => _ids.remove(productId);
}
