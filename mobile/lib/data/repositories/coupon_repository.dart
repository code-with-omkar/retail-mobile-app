import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// What the cart shows for a typed code (`POST api/carts/{storeId}/coupon`). When [valid] is false, [message] says why and [discount] is 0.
class CouponPreview {
  const CouponPreview({required this.valid, required this.code, required this.discount, required this.message, required this.total, this.reason, this.animation});
  final bool valid;
  final String code, message;

  /// When not valid, the kind of refusal (for example BelowMinimum), for counting; null when valid.
  final String? reason;
  final double discount, total;

  /// Played when the coupon is applied; null for none.
  final String? animation;

  factory CouponPreview.fromJson(Map<String, dynamic> j) => CouponPreview(
        valid: j['valid'] == true,
        code: (j['code'] as String?) ?? '',
        discount: (j['discount'] as num?)?.toDouble() ?? 0,
        message: (j['message'] as String?) ?? '',
        total: (j['total'] as num?)?.toDouble() ?? 0,
        reason: j['reason'] as String?,
        animation: j['animation'] as String?,
      );
}

/// A coupon the business shows in the app (`GET api/offers/coupons`).
class FeaturedCoupon {
  const FeaturedCoupon({
    required this.id,
    required this.code,
    required this.kind,
    this.value = 0,
    this.maxDiscount,
    this.minSubtotal = 0,
    this.firstOrderOnly = false,
    this.design,
    this.imageUrl,
    this.ribbon,
    this.showAsPopup = false,
    this.animation,
  });
  final String id, code, kind;
  final double value, minSubtotal;
  final double? maxDiscount;
  final bool firstOrderOnly, showAsPopup;
  final String? design, imageUrl, ribbon, animation;

  factory FeaturedCoupon.fromJson(Map<String, dynamic> j, {String? imageBase}) {
    var image = j['imageUrl'] as String?;
    if (image != null && image.startsWith('/') && imageBase != null) image = '$imageBase$image';
    return FeaturedCoupon(
      id: j['id'] as String,
      code: (j['code'] as String?) ?? '',
      kind: (j['kind'] as String?) ?? 'Percent',
      value: (j['value'] as num?)?.toDouble() ?? 0,
      maxDiscount: (j['maxDiscount'] as num?)?.toDouble(),
      minSubtotal: (j['minSubtotal'] as num?)?.toDouble() ?? 0,
      firstOrderOnly: j['firstOrderOnly'] == true,
      design: j['design'] as String?,
      imageUrl: image,
      ribbon: j['ribbon'] as String?,
      showAsPopup: j['showAsPopup'] == true,
      animation: j['animation'] as String?,
    );
  }
}

/// Trying a coupon code on the cart. Checkout checks the code again on the server, so a preview is a courtesy, never the last word.
abstract interface class CouponRepository {
  Future<CouponPreview> preview({required String storeId, required String code});

  /// The coupons to list at this store.
  Future<List<FeaturedCoupon>> featured(String storeId);
}

class ApiCouponRepository implements CouponRepository {
  ApiCouponRepository(this._api);
  final ApiClient _api;

  @override
  Future<CouponPreview> preview({required String storeId, required String code}) =>
      _api.post('/api/carts/$storeId/coupon', body: {'code': code}, parse: (d) => CouponPreview.fromJson(asObject(d)));

  @override
  Future<List<FeaturedCoupon>> featured(String storeId) => _api.get('/api/offers/coupons',
      query: {'storeId': storeId}, parse: (d) => [for (final j in (d as List? ?? const [])) FeaturedCoupon.fromJson(asObject(j), imageBase: _api.baseUrl)]);
}

/// Offline demo data has no coupons.
class LocalCouponRepository implements CouponRepository {
  @override
  Future<CouponPreview> preview({required String storeId, required String code}) async =>
      CouponPreview(valid: false, code: code, discount: 0, message: 'Coupons are not available here.', total: 0);

  @override
  Future<List<FeaturedCoupon>> featured(String storeId) async => const [];
}
