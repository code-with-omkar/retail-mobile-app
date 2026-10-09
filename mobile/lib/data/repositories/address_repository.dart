import '../api/api_client.dart';
import '../dto/address_dto.dart';
import '../dto/catalog_dto.dart';
import '../models.dart';

/// Saved addresses and the serviceability check. Screens depend on this interface, not on HTTP.
abstract interface class AddressRepository {
  /// The signed-in customer's addresses, default first. Needs a signed-in customer.
  Future<List<SavedAddress>> list();

  /// A [ValidationException] carries the server's message per problem; a [ConflictException] means the address limit was reached.
  Future<SavedAddress> create(AddressDraft draft);

  Future<SavedAddress> update(String id, AddressDraft draft);

  Future<SavedAddress> setDefault(String id);

  Future<void> delete(String id);

  /// Public: works for guests. Coordinates should already be rounded.
  Future<ServiceabilityResult> serviceability({required double latitude, required double longitude});
}

/// `api/customer/addresses` and `api/catalog/serviceability`.
class ApiAddressRepository implements AddressRepository {
  ApiAddressRepository(this._api);
  final ApiClient _api;

  @override
  Future<List<SavedAddress>> list() => _api.get('/api/customer/addresses', parse: savedAddressesFromJson);

  @override
  Future<SavedAddress> create(AddressDraft draft) =>
      _api.post('/api/customer/addresses', body: addressDraftToJson(draft), parse: (d) => savedAddressFromJson(asObject(d)));

  @override
  Future<SavedAddress> update(String id, AddressDraft draft) =>
      _api.put('/api/customer/addresses/$id', body: addressDraftToJson(draft), parse: (d) => savedAddressFromJson(asObject(d)));

  @override
  Future<SavedAddress> setDefault(String id) => _api.post('/api/customer/addresses/$id/default', parse: (d) => savedAddressFromJson(asObject(d)));

  @override
  Future<void> delete(String id) => _api.delete('/api/customer/addresses/$id', parse: (_) {});

  @override
  Future<ServiceabilityResult> serviceability({required double latitude, required double longitude}) =>
      _api.get('/api/catalog/serviceability', query: {'latitude': latitude, 'longitude': longitude}, parse: (d) => serviceabilityFromJson(asObject(d)));
}
