import '../api/api_client.dart';

class HealthReport {
  const HealthReport({required this.liveOk, required this.readyOk, required this.readyBody, this.correlationId});
  final bool liveOk, readyOk;
  final String readyBody;
  final String? correlationId;
}

/// `/health/live` (process is up) and `/health/ready` (dependencies such as the database are up).
class HealthRepository {
  const HealthRepository(this._api);
  final ApiClient _api;

  Future<HealthReport> check() async {
    final live = await _api.probe('/health/live');
    final ready = await _api.probe('/health/ready');
    return HealthReport(liveOk: live.statusCode == 200, readyOk: ready.statusCode == 200, readyBody: ready.body, correlationId: ready.correlationId);
  }
}
