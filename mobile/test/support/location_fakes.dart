import 'dart:convert';

import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/location_services.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/repositories/address_repository.dart';

/// A delivery place already chosen on the device (Bandra West), for tests that are not about choosing one.
final deviceLocationPrefs = <String, Object>{
  'device_place': jsonEncode({'label': 'Home', 'line': 'Flat 402, Sea Breeze Apts, Bandra West, Mumbai 400050', 'latitude': 19.0596, 'longitude': 72.8295}),
};

/// A location service that answers what a test says, and counts how often it was asked.
class FakeLocationService implements LocationService {
  FakeLocationService([this.fix = const LocationFix(LocationAccess.granted, latitude: 19.076, longitude: 72.8777)]);

  LocationFix fix;
  int asked = 0;
  final opened = <LocationAccess>[];

  @override
  Future<LocationFix> currentPosition() async {
    asked++;
    return fix;
  }

  @override
  Future<void> openSettings(LocationAccess access) async => opened.add(access);
}

/// Address lookups that answer what a test says.
class FakeGeocodingService implements GeocodingService {
  String? reverseResult = '12 Marine Drive, Mumbai';
  ({double latitude, double longitude})? forwardResult = (latitude: 19.076, longitude: 72.8777);
  bool unavailable = false;
  final forwardQueries = <String>[];

  @override
  Future<String?> reverse({required double latitude, required double longitude}) async => reverseResult;

  @override
  Future<({double latitude, double longitude})?> forward(String address) async {
    forwardQueries.add(address);
    if (unavailable) throw const GeocodingUnavailable();
    return forwardResult;
  }
}

/// The address API in memory, with the server's rules that matter to the app: validation messages, the limit, one default,
/// and a serviceability answer by position (only points near Mumbai are served).
class FakeAddressRepository implements AddressRepository {
  FakeAddressRepository({this.limit = 10});

  final int limit;
  final addresses = <SavedAddress>[];
  final calls = <String>[];
  Object? failNext;
  int _next = 1;

  static bool servesPoint(double latitude, double longitude) => (latitude - 19.07).abs() < 0.5 && (longitude - 72.87).abs() < 0.5;

  SavedAddress seed(String label, {double latitude = 19.076, double longitude = 72.8777, bool isDefault = false, String line = '12 Marine Drive, Mumbai', String flat = ''}) {
    final a = SavedAddress(
      id: 'a${_next++}',
      label: label,
      line: line,
      flatOrBuilding: flat,
      latitude: latitude,
      longitude: longitude,
      receiverName: 'Asha Patil',
      receiverPhone: '9876543210',
      isDefault: isDefault,
      serviceable: servesPoint(latitude, longitude),
      serviceabilityReason: servesPoint(latitude, longitude) ? ServiceabilityReasons.serviceable : ServiceabilityReasons.outsideServiceArea,
    );
    addresses.add(a);
    return a;
  }

  void _maybeFail() {
    final failure = failNext;
    if (failure != null) {
      failNext = null;
      throw failure;
    }
  }

  SavedAddress _build(String id, AddressDraft d, {required bool isDefault}) => SavedAddress(
        id: id,
        label: d.label.trim(),
        line: d.line.trim(),
        flatOrBuilding: d.flatOrBuilding.trim(),
        landmark: d.landmark.trim(),
        latitude: d.latitude,
        longitude: d.longitude,
        receiverName: d.receiverName.trim(),
        receiverPhone: d.receiverPhone.replaceAll(' ', ''),
        isDefault: isDefault,
        serviceable: servesPoint(d.latitude, d.longitude),
        serviceabilityReason: servesPoint(d.latitude, d.longitude) ? ServiceabilityReasons.serviceable : ServiceabilityReasons.outsideServiceArea,
      );

  @override
  Future<List<SavedAddress>> list() async {
    calls.add('list');
    _maybeFail();
    return [...addresses]..sort((a, b) => a.isDefault == b.isDefault ? 0 : (a.isDefault ? -1 : 1));
  }

  @override
  Future<SavedAddress> create(AddressDraft draft) async {
    calls.add('create');
    _maybeFail();
    if (addresses.length >= limit) throw const ConflictException('limit', statusCode: 409);
    final saved = _build('a${_next++}', draft, isDefault: addresses.isEmpty);
    addresses.add(saved);
    return saved;
  }

  @override
  Future<SavedAddress> update(String id, AddressDraft draft) async {
    calls.add('update $id');
    _maybeFail();
    final index = addresses.indexWhere((a) => a.id == id);
    if (index < 0) throw const NotFoundException('missing', statusCode: 404);
    return addresses[index] = _build(id, draft, isDefault: addresses[index].isDefault);
  }

  @override
  Future<SavedAddress> setDefault(String id) async {
    calls.add('default $id');
    _maybeFail();
    if (!addresses.any((a) => a.id == id)) throw const NotFoundException('missing', statusCode: 404);
    for (var i = 0; i < addresses.length; i++) {
      final a = addresses[i];
      addresses[i] = SavedAddress(
        id: a.id,
        label: a.label,
        line: a.line,
        flatOrBuilding: a.flatOrBuilding,
        landmark: a.landmark,
        latitude: a.latitude,
        longitude: a.longitude,
        receiverName: a.receiverName,
        receiverPhone: a.receiverPhone,
        isDefault: a.id == id,
        serviceable: a.serviceable,
        serviceabilityReason: a.serviceabilityReason,
      );
    }
    return addresses.firstWhere((a) => a.id == id);
  }

  @override
  Future<void> delete(String id) async {
    calls.add('delete $id');
    _maybeFail();
    final index = addresses.indexWhere((a) => a.id == id);
    if (index < 0) throw const NotFoundException('missing', statusCode: 404);
    final wasDefault = addresses[index].isDefault;
    addresses.removeAt(index);
    if (wasDefault && addresses.isNotEmpty) await setDefault(addresses.last.id);
  }

  @override
  Future<ServiceabilityResult> serviceability({required double latitude, required double longitude}) async {
    calls.add('serviceability');
    final ok = servesPoint(latitude, longitude);
    return ServiceabilityResult(serviceable: ok, reason: ok ? ServiceabilityReasons.serviceable : ServiceabilityReasons.outsideServiceArea, nearestDistanceKm: ok ? 1.0 : 900);
  }
}
