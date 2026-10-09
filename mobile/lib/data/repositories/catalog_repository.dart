import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/api_exception.dart';
import '../dto/catalog_dto.dart';
import '../models.dart';
import '../seed.dart' as seed;
import 'product_store.dart';

/// One page of products plus paging info.
class ProductPage {
  const ProductPage({required this.items, required this.page, required this.totalCount, required this.hasMore});
  final List<Product> items;
  final int page, totalCount;
  final bool hasMore;
}

/// Screens depend on this interface, not on HTTP or seed data.
abstract interface class CatalogRepository {
  /// Categories. With a [storeId], only those that have products that store carries.
  Future<List<Category>> categories({String? storeId});

  /// The store that serves a location, or null when no store covers it. Coordinates should already be rounded.
  Future<NearestStore?> nearestStore({required double latitude, required double longitude});

  /// Every store that delivers to a location, nearest first. Empty when none does.
  Future<List<NearestStore>> stores({required double latitude, required double longitude});

  /// Active products, paged. [categoryId] is a catalog category id from [categories]. With a [storeId], each product
  /// carries that store's stock flags; without one, stock is unknown and products count as in stock. With [carriedOnly]
  /// (needs a [storeId]) only products that store carries are returned.
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20});

  Future<Product> product(String id, {String? storeId});
}

/// Local seed data: used by tests and for offline development (`--dart-define=USE_SEED_DATA=true`).
class SeedCatalogRepository implements CatalogRepository {
  const SeedCatalogRepository();

  @override
  Future<List<Category>> categories({String? storeId}) async => seed.categories;

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async => const [
        NearestStore(id: 'seed-store', name: seed.storeName, address: '12 Marine Drive', distanceKm: seed.storeDistanceKm, estimatedMinutes: seed.etaMinutes, serviceRadiusKm: 8),
        NearestStore(id: 'seed-store-2', name: 'Green Basket Express', address: '44 Cedar Avenue', distanceKm: 3.4, estimatedMinutes: 15, serviceRadiusKm: 7),
      ];

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async =>
      const NearestStore(id: 'seed-store', name: seed.storeName, distanceKm: seed.storeDistanceKm, estimatedMinutes: seed.etaMinutes);

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async {
    final text = (search ?? '').trim();
    final q = text.toLowerCase();
    final all = seed.products.where((p) => (categoryId == null || p.categoryId == categoryId) && (q.isEmpty || p.name.toLowerCase().contains(q) || p.nameMr.contains(text))).toList();
    final start = (page - 1) * pageSize;
    final items = all.skip(start).take(pageSize).toList();
    return ProductPage(items: items, page: page, totalCount: all.length, hasMore: start + items.length < all.length);
  }

  @override
  Future<Product> product(String id, {String? storeId}) async => seed.productById(id);
}

/// `/api/catalog/categories`, `/api/catalog/products`, `/api/catalog/products/{id}` and `/api/catalog/stores/nearest` (all public).
class ApiCatalogRepository implements CatalogRepository {
  ApiCatalogRepository(this._api);
  final ApiClient _api;
  // Kept for the session, per store (null = all categories).
  final _categories = <String?, List<Category>>{};

  @override
  Future<List<Category>> categories({String? storeId}) async {
    final cached = _categories[storeId];
    if (cached != null) return cached;
    final fresh = await _api.get('/api/catalog/categories', query: {'storeId': ?storeId}, parse: (data) => asList(data, CatalogCategoryDto.fromJson).map(categoryFromCatalogDto).toList());
    return _categories[storeId] = fresh;
  }

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) => _api.get(
        '/api/catalog/stores',
        query: {'latitude': latitude, 'longitude': longitude},
        parse: (data) => asList(data, NearestStoreDto.fromJson).map((d) => NearestStore(id: d.id, name: d.name, address: d.address, distanceKm: d.distanceKm, estimatedMinutes: d.estimatedMinutes, serviceRadiusKm: d.serviceRadiusKm)).toList(),
      );

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async {
    try {
      final dto = await _api.get('/api/catalog/stores/nearest', query: {'latitude': latitude, 'longitude': longitude}, parse: (data) => NearestStoreDto.fromJson(asObject(data)));
      return NearestStore(id: dto.id, name: dto.name, distanceKm: dto.distanceKm, estimatedMinutes: dto.estimatedMinutes);
    } on NotFoundException {
      return null; // no store serves this location
    }
  }

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async {
    final keys = await _categoryKeys();
    final dto = await _api.get(
      '/api/catalog/products',
      query: {'page': page, 'pageSize': pageSize, if (search != null && search.trim().isNotEmpty) 'search': search.trim(), 'categoryId': ?categoryId, 'storeId': ?storeId, if (carriedOnly && storeId != null) 'carriedOnly': true},
      parse: (data) => PagedDto.fromJson(data, CatalogProductDto.fromJson),
    );
    return ProductPage(items: [for (final d in dto.items) productFromCatalogDto(d, keys[d.categoryId] ?? 'other')], page: dto.page, totalCount: dto.totalCount, hasMore: dto.hasMore);
  }

  @override
  Future<Product> product(String id, {String? storeId}) async {
    final keys = await _categoryKeys();
    final dto = await _api.get('/api/catalog/products/$id', query: {'storeId': ?storeId}, parse: (data) => CatalogProductDto.fromJson(asObject(data)));
    return productFromCatalogDto(dto, keys[dto.categoryId] ?? 'other');
  }

  Future<Map<String, String>> _categoryKeys() async => {for (final c in await categories()) c.id: categoryKeyFor(c.label)};
}

/// Registers every product it returns in the [ProductStore] so the cart can find them later.
class CachingCatalogRepository implements CatalogRepository {
  const CachingCatalogRepository(this._inner, this._store);
  final CatalogRepository _inner;
  final ProductStore _store;

  @override
  Future<List<Category>> categories({String? storeId}) => _inner.categories(storeId: storeId);

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) => _inner.stores(latitude: latitude, longitude: longitude);

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) => _inner.nearestStore(latitude: latitude, longitude: longitude);

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async {
    final result = await _inner.products(search: search, categoryId: categoryId, storeId: storeId, carriedOnly: carriedOnly, page: page, pageSize: pageSize);
    _store.putAll(result.items);
    return result;
  }

  @override
  Future<Product> product(String id, {String? storeId}) async {
    final p = await _inner.product(id, storeId: storeId);
    _store.put(p);
    return p;
  }
}

Category categoryFromCatalogDto(CatalogCategoryDto d) => Category(d.id, d.name, iconForCategoryKey(categoryKeyFor(d.name)), labelMr: d.translations['mr']?.name);

// Defaults for fields the API does not provide: no pack sizes (variants are phase P3). Stock is unknown (treated as in stock)
// when the product was requested without a store.
const _pastels = [Color(0xFFFFC4E1), Color(0xFFFFEEAA), Color(0xFFB8FFD2), Color(0xFFE0D9FF)];
Color _pastelFor(String id) => _pastels[id.codeUnits.fold(0, (a, b) => a + b) % _pastels.length];

Product productFromCatalogDto(CatalogProductDto d, String categoryKey) {
  final mr = d.translations['mr'];
  return Product(
    id: d.id,
    name: d.name,
    nameMr: mr?.name ?? d.name,
    unit: d.unitOfMeasure,
    price: d.price,
    mrp: d.mrp,
    color: _pastelFor(d.id),
    category: categoryKey,
    categoryId: d.categoryId,
    description: d.description,
    descriptionMr: mr?.description ?? d.description,
    imageUrl: d.imageUrl,
    inStock: d.inStock ?? true,
    lowStock: d.lowStock ?? false,
    packs: [for (final v in d.variants) packFromVariantDto(v)],
  );
}

/// A variant becomes a pack. Without a store its stock is unknown and counts as in stock.
PackOption packFromVariantDto(CatalogVariantDto v) => PackOption(id: v.id, label: v.label, price: v.price, mrp: v.mrp, isDefault: v.isDefault, inStock: v.inStock ?? true, lowStock: v.lowStock ?? false);
