import '../api/api_client.dart';
import '../api/api_exception.dart';
import '../dto/catalog_dto.dart';

/// A store an offer is for.
class OfferStore {
  const OfferStore(this.id, this.name);
  final String id, name;
}

/// An offer from a store (or the whole business): a message, a percent off some products, a picture, or a mix.
class Offer {
  const Offer({
    required this.id,
    required this.title,
    this.titleMr,
    this.body,
    this.bodyMr,
    this.imageUrl,
    this.discountPercent = 0,
    this.endsAt,
    this.showOnLoad = false,
    this.running = true,
    this.productIds = const [],
    this.stores = const [],
  });

  final String id, title;
  final String? titleMr, body, bodyMr;

  /// Already a full link (the repository turns the API's /media path into one).
  final String? imageUrl;
  final int discountPercent;
  final DateTime? endsAt;

  /// Shown once as a pop-up when the app opens.
  final bool showOnLoad;

  /// For an opened offer: the discount is on right now (false once it ended).
  final bool running;
  final List<String> productIds;
  final List<OfferStore> stores;

  String titleIn(bool marathi) => marathi && (titleMr?.isNotEmpty ?? false) ? titleMr! : title;
  String? bodyIn(bool marathi) => marathi && (bodyMr?.isNotEmpty ?? false) ? bodyMr : body;
}

/// A store the customer bought from before.
class RecentStore {
  const RecentStore({required this.id, required this.name, required this.address, required this.lastOrderedAt});
  final String id, name, address;
  final DateTime lastOrderedAt;
}

/// What is on offer, and what the customer bought before. The customer comes from the sign-in, never from the app.
abstract class OffersRepository {
  /// Running discounts and picture messages at this store, newest first.
  Future<List<Offer>> active(String storeId);

  /// One offer, for the screen a notification or banner opens. Null when it is gone (not started, cancelled).
  Future<Offer?> offer(String id);

  /// Up to four stores the customer bought from last, newest first.
  Future<List<RecentStore>> recentStores();

  /// What the customer bought at this store before, with today's price and stock, most recent first.
  Future<List<CatalogProductDto>> boughtBefore(String storeId);
}

Offer offerFromJson(Map<String, dynamic> j, {String? imageBase}) {
  String? image = j['imageUrl'] as String?;
  if (image != null && image.startsWith('/') && imageBase != null) image = '$imageBase$image';
  return Offer(
    id: j['id'] as String,
    title: (j['titleEn'] as String?) ?? '',
    titleMr: j['titleMr'] as String?,
    body: j['bodyEn'] as String?,
    bodyMr: j['bodyMr'] as String?,
    imageUrl: image,
    discountPercent: (j['discountPercent'] as num?)?.toInt() ?? 0,
    endsAt: j['endsAt'] is String ? DateTime.parse(j['endsAt'] as String).toLocal() : null,
    showOnLoad: j['showOnLoad'] == true,
    running: j['running'] != false,
    productIds: [for (final id in (j['productIds'] as List? ?? const [])) '$id'],
    stores: [for (final s in (j['stores'] as List? ?? const [])) OfferStore((s as Map)['id'] as String, (s['name'] as String?) ?? '')],
  );
}

class ApiOffersRepository implements OffersRepository {
  ApiOffersRepository(this._api);
  final ApiClient _api;

  @override
  Future<List<Offer>> active(String storeId) =>
      _api.get('/api/catalog/offers', query: {'storeId': storeId}, parse: (d) => [for (final j in (d as List? ?? const [])) offerFromJson(asObject(j), imageBase: _api.baseUrl)]);

  @override
  Future<Offer?> offer(String id) async {
    try {
      return await _api.get('/api/catalog/offers/$id', parse: (d) => offerFromJson(asObject(d), imageBase: _api.baseUrl));
    } on NotFoundException {
      return null;
    }
  }

  @override
  Future<List<RecentStore>> recentStores() => _api.get(
        '/api/customer/recent-stores',
        parse: (d) => [
          for (final j in (d as List? ?? const []))
            RecentStore(id: (j as Map)['id'] as String, name: (j['name'] as String?) ?? '', address: (j['address'] as String?) ?? '', lastOrderedAt: DateTime.parse(j['lastOrderedAt'] as String).toLocal()),
        ],
      );

  @override
  Future<List<CatalogProductDto>> boughtBefore(String storeId) => _api.get('/api/customer/stores/$storeId/bought-before', parse: (d) => asList(d, CatalogProductDto.fromJson));
}

/// Offline development and previews: nothing is on offer and nothing was bought.
class LocalOffersRepository implements OffersRepository {
  @override
  Future<List<Offer>> active(String storeId) async => const [];

  @override
  Future<Offer?> offer(String id) async => null;

  @override
  Future<List<RecentStore>> recentStores() async => const [];

  @override
  Future<List<CatalogProductDto>> boughtBefore(String storeId) async => const [];
}
