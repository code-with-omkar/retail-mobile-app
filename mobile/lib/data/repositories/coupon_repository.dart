import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// What the cart shows for a typed code (`POST api/carts/{storeId}/coupon`). When [valid] is false, [message] says why and [discount] is 0.
class CouponPreview {
  const CouponPreview({required this.valid, required this.code, required this.discount, required this.message, required this.total});
  final bool valid;
  final String code, message;
  final double discount, total;

  factory CouponPreview.fromJson(Map<String, dynamic> j) => CouponPreview(
        valid: j['valid'] == true,
        code: (j['code'] as String?) ?? '',
        discount: (j['discount'] as num?)?.toDouble() ?? 0,
        message: (j['message'] as String?) ?? '',
        total: (j['total'] as num?)?.toDouble() ?? 0,
      );
}

/// Trying a coupon code on the cart. Checkout checks the code again on the server, so a preview is a courtesy, never the last word.
abstract interface class CouponRepository {
  Future<CouponPreview> preview({required String storeId, required String code});
}

class ApiCouponRepository implements CouponRepository {
  ApiCouponRepository(this._api);
  final ApiClient _api;

  @override
  Future<CouponPreview> preview({required String storeId, required String code}) =>
      _api.post('/api/carts/$storeId/coupon', body: {'code': code}, parse: (d) => CouponPreview.fromJson(asObject(d)));
}

/// Offline demo data has no coupons.
class LocalCouponRepository implements CouponRepository {
  @override
  Future<CouponPreview> preview({required String storeId, required String code}) async =>
      CouponPreview(valid: false, code: code, discount: 0, message: 'Coupons are not available here.', total: 0);
}
