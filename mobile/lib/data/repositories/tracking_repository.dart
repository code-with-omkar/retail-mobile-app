import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// How far the delivery partner is from the customer. Never coordinates: only distance and a time.
class Tracking {
  const Tracking({required this.available, this.distanceKm, this.etaMinutes, this.partnerName});
  final bool available;
  final double? distanceKm;
  final int? etaMinutes;
  final String? partnerName;
}

/// `GET api/customer/orders/{id}/tracking`.
abstract interface class TrackingRepository {
  /// The latest distance and ETA while the order is out for delivery and the partner's phone has reported recently; otherwise not available.
  Future<Tracking> get(String orderId);
}

class ApiTrackingRepository implements TrackingRepository {
  ApiTrackingRepository(this._api);
  final ApiClient _api;

  @override
  Future<Tracking> get(String orderId) => _api.get('/api/customer/orders/$orderId/tracking', parse: (d) {
        final j = asObject(d);
        return Tracking(
          available: j['available'] == true,
          distanceKm: (j['distanceKm'] as num?)?.toDouble(),
          etaMinutes: (j['etaMinutes'] as num?)?.toInt(),
          partnerName: j['partnerName'] as String?,
        );
      });
}

/// Offline development and previews: there is nothing to track.
class LocalTrackingRepository implements TrackingRepository {
  @override
  Future<Tracking> get(String orderId) async => const Tracking(available: false);
}
