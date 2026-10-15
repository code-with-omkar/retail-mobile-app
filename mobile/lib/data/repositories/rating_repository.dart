import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// A customer's rating of one delivered order.
class OrderRating {
  const OrderRating({required this.storeStars, this.deliveryStars, this.comment, this.canEdit = true});
  final int storeStars;
  final int? deliveryStars;
  final String? comment;

  /// False once the 14 days after delivery have passed.
  final bool canEdit;

  factory OrderRating.fromJson(Map<String, dynamic> j) => OrderRating(
        storeStars: (j['storeStars'] as num).toInt(),
        deliveryStars: (j['deliveryStars'] as num?)?.toInt(),
        comment: j['comment'] as String?,
        canEdit: j['canEdit'] != false,
      );
}

/// Where an order stands for rating: can it be rated now, can the delivery be rated, and what was given already.
class OrderRatingState {
  const OrderRatingState({required this.canRate, required this.canRateDelivery, this.rating});
  final bool canRate, canRateDelivery;
  final OrderRating? rating;

  factory OrderRatingState.fromJson(Map<String, dynamic> j) => OrderRatingState(
        canRate: j['canRate'] == true,
        canRateDelivery: j['canRateDelivery'] == true,
        rating: j['rating'] is Map<String, dynamic> ? OrderRating.fromJson(j['rating'] as Map<String, dynamic>) : null,
      );
}

abstract class RatingRepository {
  Future<OrderRatingState> state(String orderId);
  Future<OrderRating> save(String orderId, {required int storeStars, int? deliveryStars, String? comment});
}

/// `api/customer/orders/{id}/rating`.
class ApiRatingRepository implements RatingRepository {
  ApiRatingRepository(this._api);
  final ApiClient _api;

  @override
  Future<OrderRatingState> state(String orderId) => _api.get('/api/customer/orders/$orderId/rating', parse: (d) => OrderRatingState.fromJson(asObject(d)));

  @override
  Future<OrderRating> save(String orderId, {required int storeStars, int? deliveryStars, String? comment}) => _api.put(
        '/api/customer/orders/$orderId/rating',
        body: {'storeStars': storeStars, 'deliveryStars': ?deliveryStars, if (comment != null && comment.trim().isNotEmpty) 'comment': comment.trim()},
        parse: (d) => OrderRating.fromJson(asObject(d)),
      );
}

/// Offline development and previews: ratings live in memory, and every delivered order can be rated.
class LocalRatingRepository implements RatingRepository {
  final _ratings = <String, OrderRating>{};

  @override
  Future<OrderRatingState> state(String orderId) async => OrderRatingState(canRate: true, canRateDelivery: false, rating: _ratings[orderId]);

  @override
  Future<OrderRating> save(String orderId, {required int storeStars, int? deliveryStars, String? comment}) async =>
      _ratings[orderId] = OrderRating(storeStars: storeStars, deliveryStars: deliveryStars, comment: comment?.trim().isEmpty == true ? null : comment?.trim());
}
