import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/fake_adapter.dart';
import '../support/location_fakes.dart';

Map<String, dynamic> _store(String id, {bool? sponsored}) => {
      'id': id,
      'name': 'Store $id',
      'address': '1 Road',
      'distanceKm': 2.5,
      'estimatedMinutes': 14,
      'serviceRadiusKm': 8,
      'sponsored': ?sponsored,
    };

ApiClient _client(FakeAdapter adapter) => ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);

class _Repo implements CatalogRepository {
  final calls = <({double latitude, double longitude, String? pincode})>[];

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude, String? pincode}) async {
    calls.add((latitude: latitude, longitude: longitude, pincode: pincode));
    return const [NearestStore(id: 'a', name: 'A', distanceKm: 1, estimatedMinutes: 10, sponsored: true), NearestStore(id: 'b', name: 'B', distanceKm: 2, estimatedMinutes: 12)];
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => throw UnimplementedError(invocation.memberName.toString());
}

void main() {
  group('paid placement in the store list', () {
    test('the pincode is sent with the stores request only when there is one, and the sponsored store is read', () async {
      final adapter = FakeAdapter()
        ..replyJson({'success': true, 'data': [_store('a', sponsored: true), _store('b')]})
        ..replyJson({'success': true, 'data': [_store('a')]});
      final repo = ApiCatalogRepository(_client(adapter));

      final withPin = await repo.stores(latitude: 19.0, longitude: 73.0, pincode: '410209');
      await repo.stores(latitude: 19.0, longitude: 73.0);

      expect(adapter.requests[0].queryParameters, {'latitude': 19.0, 'longitude': 73.0, 'pincode': '410209'});
      expect(adapter.requests[1].queryParameters, {'latitude': 19.0, 'longitude': 73.0});
      expect(withPin.map((s) => s.sponsored), [true, false]);
    });

    test('an older answer without the flag reads as not sponsored', () {
      expect(NearestStoreDto.fromJson(_store('a')).sponsored, isFalse);
      expect(NearestStoreDto.fromJson(_store('a', sponsored: true)).sponsored, isTrue);
    });

    test('the store request carries the pincode the phone finds for the delivery point, and none when it cannot', () async {
      SharedPreferences.setMockInitialValues(const {});
      final prefs = await SharedPreferences.getInstance();
      final geocoder = FakeGeocodingService()..pincodeResult = '410209';
      final repo = _Repo();
      final container = ProviderContainer(retry: noAutomaticRetry, overrides: [
        sharedPrefsProvider.overrideWithValue(prefs),
        geocodingServiceProvider.overrideWithValue(geocoder),
        catalogRepositoryProvider.overrideWithValue(repo),
        deliveryLocationProvider.overrideWithValue((latitude: 19.07, longitude: 73.01)),
      ]);
      addTearDown(container.dispose);

      final found = await container.read(serviceableStoresProvider.future);

      expect(repo.calls.single.pincode, '410209');
      expect(found.first.sponsored, isTrue);
      expect(geocoder.pincodeLookups.single, (latitude: 19.07, longitude: 73.01));
    });

    test('when the pincode cannot be found the stores are still listed, with no pincode sent', () async {
      SharedPreferences.setMockInitialValues(const {});
      final prefs = await SharedPreferences.getInstance();
      final repo = _Repo();
      final container = ProviderContainer(retry: noAutomaticRetry, overrides: [
        sharedPrefsProvider.overrideWithValue(prefs),
        geocodingServiceProvider.overrideWithValue(FakeGeocodingService()),
        catalogRepositoryProvider.overrideWithValue(repo),
        deliveryLocationProvider.overrideWithValue((latitude: 19.07, longitude: 73.01)),
      ]);
      addTearDown(container.dispose);

      await container.read(serviceableStoresProvider.future);

      expect(repo.calls.single.pincode, isNull);
    });
  });
}
