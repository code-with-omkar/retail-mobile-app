import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// Where the app can show an ad. The keys are the same on the server and in the portal.
class AdPlace {
  static const home = 'home';
  static const homeMiddle = 'home_middle';
  static const categories = 'categories';
  static const search = 'search';
  static const cart = 'cart';
  static const order = 'order';
  static const product = 'product';
}

/// What a tap on an ad opens.
enum AdTarget { none, product, category, offer, url }

AdTarget adTargetOf(String? name) => switch (name) {
  'Product' => AdTarget.product,
  'Category' => AdTarget.category,
  'Offer' => AdTarget.offer,
  'Url' => AdTarget.url,
  _ => AdTarget.none,
};

/// An ad from the administrator (`GET api/ads?placement=`): a picture, an optional title and where a tap goes.
class AppAd {
  const AppAd({
    required this.id,
    required this.placement,
    required this.imageUrl,
    this.title,
    this.target = AdTarget.none,
    this.targetValue,
  });
  final String id, placement, imageUrl;
  final String? title, targetValue;
  final AdTarget target;

  /// [imageBase] makes the server's /media path a whole link, like an offer's picture.
  factory AppAd.fromJson(Map<String, dynamic> j, {String? imageBase}) {
    var image = (j['imageUrl'] as String?) ?? '';
    if (image.startsWith('/') && imageBase != null) image = '$imageBase$image';
    final title = (j['title'] as String?)?.trim();
    return AppAd(
      id: '${j['id']}',
      placement: (j['placement'] as String?) ?? '',
      imageUrl: image,
      title: title == null || title.isEmpty ? null : title,
      target: adTargetOf(j['targetType'] as String?),
      targetValue: j['target'] as String?,
    );
  }
}

/// The ads, and counting. Anyone can see ads, signed in or not. Counting is best effort: a failure never reaches the customer.
abstract interface class AdsRepository {
  Future<List<AppAd>> forPlacement(String placement, {String? storeId});
  Future<void> countView(String id);
  Future<void> countTap(String id);
}

class ApiAdsRepository implements AdsRepository {
  ApiAdsRepository(this._api);
  final ApiClient _api;

  @override
  Future<List<AppAd>> forPlacement(String placement, {String? storeId}) =>
      _api.get(
        '/api/ads',
        query: {'placement': placement, 'storeId': ?storeId},
        parse: (d) =>
            asList(d, (j) => AppAd.fromJson(j, imageBase: _api.baseUrl)),
      );

  @override
  Future<void> countView(String id) =>
      _api.post<void>('/api/ads/$id/view', parse: (_) {});

  @override
  Future<void> countTap(String id) =>
      _api.post<void>('/api/ads/$id/tap', parse: (_) {});
}

/// Offline demo data has no ads.
class LocalAdsRepository implements AdsRepository {
  @override
  Future<List<AppAd>> forPlacement(String placement, {String? storeId}) async =>
      const [];

  @override
  Future<void> countView(String id) async {}

  @override
  Future<void> countTap(String id) async {}
}
