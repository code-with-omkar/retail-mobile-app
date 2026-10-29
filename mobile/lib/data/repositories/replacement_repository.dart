import '../api/api_client.dart';

/// `POST api/customer/orders/{id}/replacements/{proposalId}/accept|decline`. Accepting swaps the item; declining (or letting it run out) removes it.
abstract interface class ReplacementRepository {
  Future<void> answer(String orderId, String proposalId, {required bool accept});
}

class ApiReplacementRepository implements ReplacementRepository {
  ApiReplacementRepository(this._api);
  final ApiClient _api;

  @override
  Future<void> answer(String orderId, String proposalId, {required bool accept}) =>
      _api.post('/api/customer/orders/$orderId/replacements/$proposalId/${accept ? 'accept' : 'decline'}', parse: (_) {});
}

/// Offline development and previews: nothing to answer.
class LocalReplacementRepository implements ReplacementRepository {
  @override
  Future<void> answer(String orderId, String proposalId, {required bool accept}) async {}
}
