/// Hand-written DTOs mirroring `QuickCommerce.Application/DTOs/CatalogDtos.cs`.
/// Field names are the API's camelCase JSON names. Parsing is strict: a missing or wrongly typed
/// required field throws a [FormatException], which `ApiClient` reports as an unexpected response.
library;

T _req<T>(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is T) return v;
  throw FormatException('Field "$key" expected ${T.toString()} but was ${v.runtimeType}');
}

double _num(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is num) return v.toDouble();
  throw FormatException('Field "$key" expected a number but was ${v.runtimeType}');
}

Map<String, dynamic> asObject(Object? v) {
  if (v is Map<String, dynamic>) return v;
  throw FormatException('Expected a JSON object but was ${v.runtimeType}');
}

List<T> asList<T>(Object? v, T Function(Map<String, dynamic>) fromJson) {
  if (v is! List) throw FormatException('Expected a JSON array but was ${v.runtimeType}');
  return v.map((e) => fromJson(asObject(e))).toList();
}

class CategoryDto {
  const CategoryDto({required this.id, required this.name, this.parentCategoryId, required this.isActive});
  final String id, name;
  final String? parentCategoryId;
  final bool isActive;

  factory CategoryDto.fromJson(Map<String, dynamic> j) => CategoryDto(
        id: _req<String>(j, 'id'),
        name: _req<String>(j, 'name'),
        parentCategoryId: j['parentCategoryId'] as String?,
        isActive: _req<bool>(j, 'isActive'),
      );
}

class ProductDto {
  const ProductDto({required this.id, required this.sku, required this.name, required this.description, required this.price, required this.unitOfMeasure, required this.categoryId, this.imageUrl, required this.isActive});
  final String id, sku, name, description, unitOfMeasure, categoryId;
  final double price;
  final String? imageUrl;
  final bool isActive;

  factory ProductDto.fromJson(Map<String, dynamic> j) => ProductDto(
        id: _req<String>(j, 'id'),
        sku: _req<String>(j, 'sku'),
        name: _req<String>(j, 'name'),
        description: (j['description'] as String?) ?? '',
        price: _num(j, 'price'),
        unitOfMeasure: _req<String>(j, 'unitOfMeasure'),
        categoryId: _req<String>(j, 'categoryId'),
        imageUrl: j['imageUrl'] as String?,
        isActive: _req<bool>(j, 'isActive'),
      );
}

class StoreDto {
  const StoreDto({required this.id, required this.name, required this.address, required this.latitude, required this.longitude, required this.serviceRadiusKm, required this.isActive});
  final String id, name, address;
  final double latitude, longitude, serviceRadiusKm;
  final bool isActive;

  factory StoreDto.fromJson(Map<String, dynamic> j) => StoreDto(
        id: _req<String>(j, 'id'),
        name: _req<String>(j, 'name'),
        address: _req<String>(j, 'address'),
        latitude: _num(j, 'latitude'),
        longitude: _num(j, 'longitude'),
        serviceRadiusKm: _num(j, 'serviceRadiusKm'),
        isActive: _req<bool>(j, 'isActive'),
      );
}

/// Translated text for one language. [description] null means "use the English description".
class TranslationDto {
  const TranslationDto({required this.name, this.description});
  final String name;
  final String? description;

  factory TranslationDto.fromJson(Map<String, dynamic> j) => TranslationDto(name: _req<String>(j, 'name'), description: j['description'] as String?);
}

Map<String, TranslationDto> _translations(Object? v) {
  if (v == null) return const {};
  final map = asObject(v);
  return {for (final e in map.entries) e.key.toLowerCase(): TranslationDto.fromJson(asObject(e.value))};
}

/// Customer product from `GET /api/catalog/products` and `/api/catalog/products/{id}`. No SKU or active flag by design.
/// [mrp] falls back to [price] and [translations] to empty when an older server omits them.
/// One pack size in `variants` of a catalogue product. `inStock` / `lowStock` are null without a store.
class CatalogVariantDto {
  const CatalogVariantDto({required this.id, required this.label, required this.price, required this.mrp, required this.isDefault, this.inStock, this.lowStock});
  final String id, label;
  final double price, mrp;
  final bool isDefault;
  final bool? inStock, lowStock;

  factory CatalogVariantDto.fromJson(Map<String, dynamic> j) {
    final price = _num(j, 'price');
    return CatalogVariantDto(
      id: _req<String>(j, 'id'),
      label: _req<String>(j, 'label'),
      price: price,
      mrp: j['mrp'] is num ? (j['mrp'] as num).toDouble() : price,
      isDefault: j['isDefault'] == true,
      inStock: j['inStock'] as bool?,
      lowStock: j['lowStock'] as bool?,
    );
  }
}

class CatalogProductDto {
  const CatalogProductDto({required this.id, required this.name, required this.description, required this.price, required this.mrp, required this.discountPercent, required this.unitOfMeasure, required this.categoryId, this.imageUrl, this.translations = const {}, this.inStock, this.lowStock, this.variants = const []});
  final String id, name, description, unitOfMeasure, categoryId;
  final double price, mrp;
  final int discountPercent;
  final String? imageUrl;
  final Map<String, TranslationDto> translations;

  /// Pack sizes; empty for an API that predates variants.
  final List<CatalogVariantDto> variants;

  /// Stock at the requested store; null when no store was requested (unknown). Never a quantity.
  final bool? inStock, lowStock;

  factory CatalogProductDto.fromJson(Map<String, dynamic> j) {
    final price = _num(j, 'price');
    return CatalogProductDto(
      id: _req<String>(j, 'id'),
      name: _req<String>(j, 'name'),
      description: (j['description'] as String?) ?? '',
      price: price,
      mrp: j['mrp'] is num ? (j['mrp'] as num).toDouble() : price,
      discountPercent: j['discountPercent'] is int ? j['discountPercent'] as int : 0,
      unitOfMeasure: _req<String>(j, 'unitOfMeasure'),
      categoryId: _req<String>(j, 'categoryId'),
      imageUrl: j['imageUrl'] as String?,
      translations: _translations(j['translations']),
      inStock: j['inStock'] as bool?,
      lowStock: j['lowStock'] as bool?,
      variants: j['variants'] is List ? asList(j['variants'], CatalogVariantDto.fromJson) : const [],
    );
  }
}

/// Customer category from `GET /api/catalog/categories` (active categories only).
class CatalogCategoryDto {
  const CatalogCategoryDto({required this.id, required this.name, this.parentCategoryId, this.translations = const {}});
  final String id, name;
  final String? parentCategoryId;
  final Map<String, TranslationDto> translations;

  factory CatalogCategoryDto.fromJson(Map<String, dynamic> j) => CatalogCategoryDto(
        id: _req<String>(j, 'id'),
        name: _req<String>(j, 'name'),
        parentCategoryId: j['parentCategoryId'] as String?,
        translations: _translations(j['translations']),
      );
}

/// Paged list envelope used by customer list endpoints: items plus paging metadata.
class PagedDto<T> {
  const PagedDto({required this.items, required this.page, required this.pageSize, required this.totalCount, required this.hasMore});
  final List<T> items;
  final int page, pageSize, totalCount;
  final bool hasMore;

  factory PagedDto.fromJson(Object? data, T Function(Map<String, dynamic>) itemFromJson) {
    final j = asObject(data);
    return PagedDto(
      items: asList(j['items'], itemFromJson),
      page: _req<int>(j, 'page'),
      pageSize: _req<int>(j, 'pageSize'),
      totalCount: _req<int>(j, 'totalCount'),
      hasMore: _req<bool>(j, 'hasMore'),
    );
  }
}

/// `GET /api/catalog/stores/nearest` and each item of `GET /api/catalog/stores` (which also carries the address).
class NearestStoreDto {
  const NearestStoreDto({required this.id, required this.name, required this.distanceKm, required this.estimatedMinutes, required this.serviceRadiusKm, this.address = ''});
  final String id, name, address;
  final double distanceKm, serviceRadiusKm;
  final int estimatedMinutes;

  factory NearestStoreDto.fromJson(Map<String, dynamic> j) => NearestStoreDto(
        id: _req<String>(j, 'id'),
        name: _req<String>(j, 'name'),
        address: (j['address'] as String?) ?? '',
        distanceKm: _num(j, 'distanceKm'),
        estimatedMinutes: _req<int>(j, 'estimatedMinutes'),
        serviceRadiusKm: _num(j, 'serviceRadiusKm'),
      );
}
