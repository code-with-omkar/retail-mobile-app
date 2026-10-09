/// Hand-written DTOs mirroring `QuickCommerce.Application/DTOs/AddressDtos.cs`.
library;

import '../models.dart';
import 'catalog_dto.dart';

String _str(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is String) return v;
  throw FormatException('Field "$key" expected a string but was ${v.runtimeType}');
}

double _double(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is num) return v.toDouble();
  throw FormatException('Field "$key" expected a number but was ${v.runtimeType}');
}

/// `GET/POST/PUT api/customer/addresses`.
SavedAddress savedAddressFromJson(Map<String, dynamic> j) => SavedAddress(
      id: _str(j, 'id'),
      label: _str(j, 'label'),
      line: _str(j, 'line'),
      flatOrBuilding: (j['flatOrBuilding'] as String?) ?? '',
      landmark: (j['landmark'] as String?) ?? '',
      latitude: _double(j, 'latitude'),
      longitude: _double(j, 'longitude'),
      receiverName: _str(j, 'receiverName'),
      receiverPhone: _str(j, 'receiverPhone'),
      isDefault: j['isDefault'] == true,
      // Older API builds do not say; an address is then treated as deliverable rather than wrongly blocked.
      serviceable: j['serviceable'] is bool ? j['serviceable'] as bool : true,
      serviceabilityReason: (j['serviceabilityReason'] as String?) ?? ServiceabilityReasons.serviceable,
    );

List<SavedAddress> savedAddressesFromJson(Object? data) => asList(data, savedAddressFromJson);

/// `GET api/catalog/serviceability`.
ServiceabilityResult serviceabilityFromJson(Map<String, dynamic> j) => ServiceabilityResult(
      serviceable: j['serviceable'] == true,
      reason: _str(j, 'reason'),
      nearestDistanceKm: j['nearestDistanceKm'] is num ? (j['nearestDistanceKm'] as num).toDouble() : null,
    );

/// The body of create and update.
Map<String, Object?> addressDraftToJson(AddressDraft d) => {
      'label': d.label.trim(),
      'line': d.line.trim(),
      'flatOrBuilding': d.flatOrBuilding.trim(),
      'landmark': d.landmark.trim(),
      'latitude': d.latitude,
      'longitude': d.longitude,
      'receiverName': d.receiverName.trim(),
      'receiverPhone': d.receiverPhone.trim(),
    };
