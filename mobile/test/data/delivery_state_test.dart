import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/location_fakes.dart';

/// A session a test controls: no server, no restore.
class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;

  @override
  AuthState build() => start;

  void force(AuthState next) => state = next;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');
const _signedIn = AuthState(AuthStatus.signedIn, _asha);
const _signedOut = AuthState(AuthStatus.signedOut);
const _restoring = AuthState(AuthStatus.restoring);

/// Counts store lookups; every location is served by one store.
class _Catalog implements CatalogRepository {
  int storeCalls = 0;
  List<NearestStore> answer = const [NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10)];

  @override
  Future<List<Category>> categories({String? storeId}) async => const [Category('c1', 'Vegetables', Icons.eco_outlined)];

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async {
    storeCalls++;
    return answer;
  }

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async => answer.firstOrNull;

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async => const ProductPage(items: [], page: 1, totalCount: 0, hasMore: false);

  @override
  Future<Product> product(String id, {String? storeId}) => throw UnimplementedError();
}

void main() {
  Future<({ProviderContainer container, FakeAddressRepository repo, _Catalog catalog})> make({
    Map<String, Object> prefs = const {},
    AuthState auth = _signedOut,
    FakeAddressRepository? repo,
    bool seed = false,
    List<Override> overrides = const [],
  }) async {
    SharedPreferences.setMockInitialValues(prefs);
    final addresses = repo ?? FakeAddressRepository();
    final catalog = _Catalog();
    final container = ProviderContainer(retry: noAutomaticRetry, overrides: [
      sharedPrefsProvider.overrideWithValue(await SharedPreferences.getInstance()),
      appConfigProvider.overrideWithValue(AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://x', useSeedData: seed)),
      authProvider.overrideWith(() => _Auth(auth)),
      addressRepositoryProvider.overrideWithValue(addresses),
      catalogRepositoryProvider.overrideWithValue(catalog),
      ...overrides,
    ]);
    addTearDown(container.dispose);
    return (container: container, repo: addresses, catalog: catalog);
  }

  Future<DeliveryState> settled(ProviderContainer c) async {
    if (c.read(authProvider).isSignedIn) await c.read(savedAddressesProvider.future);
    return c.read(deliveryStateProvider);
  }

  DeliveryPlace? placeOf(DeliveryState s) => s is DeliveryChosen ? s.place : null;

  group('a guest', () {
    test('delivers to the place kept on the device', () async {
      final t = await make(prefs: deviceLocationPrefs);

      final place = placeOf(await settled(t.container))!;

      expect((place.label, place.isSaved, place.latitude, place.longitude), ('Home', false, 19.0596, 72.8295));
    });

    test('with no place chosen the app has to ask', () async {
      final t = await make();
      expect(await settled(t.container), isA<DeliveryNone>());
      expect(t.container.read(deliveryPlaceProvider), isNull);
    });

    test('a damaged saved place is ignored rather than crashing', () async {
      final t = await make(prefs: {'device_place': '{not json'});
      expect(await settled(t.container), isA<DeliveryNone>());
    });

    test('choosing a place is remembered across launches, and can be cleared', () async {
      final first = await make();
      first.container.read(devicePlaceProvider.notifier).set(const DeliveryPlace(label: 'Current location', line: '12 Marine Drive', latitude: 19.076, longitude: 72.8777));
      expect(placeOf(first.container.read(deliveryStateProvider))!.line, '12 Marine Drive');

      final stored = (await SharedPreferences.getInstance()).getString('device_place')!;
      expect(jsonDecode(stored), containsPair('label', 'Current location'));

      final second = await make(prefs: {'device_place': stored});
      expect(placeOf(second.container.read(deliveryStateProvider))!.latitude, 19.076);

      second.container.read(devicePlaceProvider.notifier).clear();
      expect(second.container.read(deliveryStateProvider), isA<DeliveryNone>());
      expect((await SharedPreferences.getInstance()).getString('device_place'), isNull);
    });

    test('never reads or lists saved addresses', () async {
      final t = await make(prefs: deviceLocationPrefs);
      t.repo.seed('Home', isDefault: true);

      await settled(t.container);
      expect(t.repo.calls, isEmpty);
      expect(await t.container.read(savedAddressesProvider.future), isEmpty);
    });
  });

  group('while the session is being checked', () {
    test('a place on the device is used at once, otherwise the app waits instead of asking', () async {
      final withPlace = await make(prefs: deviceLocationPrefs, auth: _restoring);
      expect(placeOf(withPlace.container.read(deliveryStateProvider)), isNotNull);

      final without = await make(auth: _restoring);
      expect(without.container.read(deliveryStateProvider), isA<DeliveryLoading>());
    });

    test('offline development has no server to wait for, so its fixed place is used straight away', () async {
      final t = await make(auth: _restoring, seed: true);
      expect(placeOf(t.container.read(deliveryStateProvider))!.label, 'Home');
    });
  });

  group('a signed-in customer', () {
    test('delivers to the default address', () async {
      final t = await make(auth: _signedIn);
      t.repo
        ..seed('Work', latitude: 19.1, longitude: 72.9)
        ..seed('Home', isDefault: true, flat: 'Flat 4');

      final place = placeOf(await settled(t.container))!;

      expect((place.label, place.addressId, place.isSaved, place.line), ('Home', 'a2', true, 'Flat 4, 12 Marine Drive, Mumbai'));
    });

    test('the address the customer chose wins over the default', () async {
      final t = await make(auth: _signedIn, prefs: {'selected_address_id': 'a1'});
      t.repo
        ..seed('Work')
        ..seed('Home', isDefault: true);

      expect(placeOf(await settled(t.container))!.label, 'Work');
    });

    test('a chosen address that no longer exists falls back to the default', () async {
      final t = await make(auth: _signedIn, prefs: {'selected_address_id': 'gone'});
      t.repo
        ..seed('Work')
        ..seed('Home', isDefault: true);

      expect(placeOf(await settled(t.container))!.label, 'Home');
    });

    test('without a default flag the first address is used', () async {
      final t = await make(auth: _signedIn);
      t.repo.seed('Work');

      expect(placeOf(await settled(t.container))!.label, 'Work');
    });

    test('saved addresses beat a place left on the device from before signing in', () async {
      final t = await make(auth: _signedIn, prefs: deviceLocationPrefs);
      t.repo.seed('Work', isDefault: true);

      expect(placeOf(await settled(t.container))!.label, 'Work');
    });

    test('with no saved address the device place is used, and with neither the app asks', () async {
      final withDevice = await make(auth: _signedIn, prefs: deviceLocationPrefs);
      expect(placeOf(await settled(withDevice.container))!.isSaved, isFalse);

      final neither = await make(auth: _signedIn);
      expect(await settled(neither.container), isA<DeliveryNone>());
    });

    test('while the addresses load the app waits, then shows them', () async {
      final gate = Completer<void>();
      final repo = _SlowRepo(gate)..seed('Home', isDefault: true);
      final t = await make(auth: _signedIn, repo: repo);

      expect(t.container.read(deliveryStateProvider), isA<DeliveryLoading>());
      gate.complete();
      expect(placeOf(await settled(t.container))!.label, 'Home');
    });

    test('signing out drops the saved addresses and goes back to the device place', () async {
      final t = await make(auth: _signedIn, prefs: deviceLocationPrefs);
      t.repo.seed('Work', isDefault: true);
      expect(placeOf(await settled(t.container))!.label, 'Work');

      (t.container.read(authProvider.notifier) as _Auth).force(_signedOut);
      await t.container.read(savedAddressesProvider.future);

      final place = placeOf(t.container.read(deliveryStateProvider))!;
      expect((place.label, place.isSaved), ('Home', false));
    });

    test('a failed list does not leave the app stuck: it falls back to what is on the device', () async {
      final repo = FakeAddressRepository()..failNext = const NetworkException('offline');
      final t = await make(auth: _signedIn, repo: repo, prefs: deviceLocationPrefs);

      await expectLater(t.container.read(savedAddressesProvider.future), throwsA(isA<NetworkException>()));

      expect(placeOf(t.container.read(deliveryStateProvider))!.isSaved, isFalse);
    });

    test('the choice of address is remembered', () async {
      final t = await make(auth: _signedIn);
      t.repo
        ..seed('Home', isDefault: true)
        ..seed('Work');
      await settled(t.container);

      t.container.read(selectedAddressIdProvider.notifier).select('a2');

      expect(placeOf(t.container.read(deliveryStateProvider))!.label, 'Work');
      expect((await SharedPreferences.getInstance()).getString('selected_address_id'), 'a2');
    });
  });

  group('changing saved addresses', () {
    test('adding goes to the server and the list is read again, first address as the default', () async {
      final t = await make(auth: _signedIn);
      await settled(t.container);
      expect(t.container.read(deliveryStateProvider), isA<DeliveryNone>());

      final saved = await t.container.read(savedAddressesProvider.notifier).add(const AddressDraft(label: 'Home', line: '12 Marine Drive', latitude: 19.076, longitude: 72.8777, receiverName: 'Asha', receiverPhone: '9876543210'));

      expect(t.repo.calls, containsAllInOrder(['list', 'create', 'list']));
      expect(saved.isDefault, isTrue);
      expect(placeOf(t.container.read(deliveryStateProvider))!.addressId, saved.id);
    });

    test('editing, making default and deleting each reload the list', () async {
      final t = await make(auth: _signedIn);
      t.repo
        ..seed('Home', isDefault: true)
        ..seed('Work');
      final notifier = t.container.read(savedAddressesProvider.notifier);
      await settled(t.container);

      await notifier.edit('a2', const AddressDraft(label: 'Office', line: '1 Trade Centre', latitude: 19.07, longitude: 72.87, receiverName: 'Asha', receiverPhone: '9876543210'));
      expect(t.container.read(savedAddressesProvider).value!.map((a) => a.label), containsAll(['Home', 'Office']));

      await notifier.makeDefault('a2');
      expect(t.container.read(savedAddressesProvider).value!.first.label, 'Office');

      await notifier.remove('a2');
      expect(t.container.read(savedAddressesProvider).value!.map((a) => a.id), ['a1']);
      expect(placeOf(t.container.read(deliveryStateProvider))!.label, 'Home');
    });

    test('a refused change leaves the list as it was and the error reaches the caller', () async {
      final t = await make(auth: _signedIn, repo: FakeAddressRepository(limit: 1));
      t.repo.seed('Home', isDefault: true);
      await settled(t.container);

      await expectLater(
        t.container.read(savedAddressesProvider.notifier).add(const AddressDraft(label: 'Work', line: 'x', latitude: 19.07, longitude: 72.87, receiverName: 'A', receiverPhone: '9876543210')),
        throwsA(isA<ConflictException>()),
      );

      expect(t.container.read(savedAddressesProvider).value!.map((a) => a.label), ['Home']);
    });
  });

  group('what the rest of the app reads', () {
    test('the delivery location is the chosen place, rounded for catalogue calls, and follows a change of address', () async {
      final t = await make(auth: _signedIn);
      t.repo
        ..seed('Home', isDefault: true, latitude: 19.059649, longitude: 72.82951)
        ..seed('Work', latitude: 19.1234567, longitude: 72.9);
      await settled(t.container);

      expect(t.container.read(deliveryLocationProvider), (latitude: 19.06, longitude: 72.83));
      t.container.read(selectedAddressIdProvider.notifier).select('a2');
      expect(t.container.read(deliveryLocationProvider), (latitude: 19.123, longitude: 72.9));
    });

    test('without a place there is nothing to look up: no store call is made and the list is empty', () async {
      final t = await make();

      expect(await t.container.read(serviceableStoresProvider.future), isEmpty);
      expect(t.catalog.storeCalls, 0);
      expect(t.container.read(deliveryServiceableProvider), isNull);
    });

    test('whether the place can be served follows the stores found for it', () async {
      final t = await make(prefs: deviceLocationPrefs);

      expect(t.container.read(deliveryServiceableProvider), isNull, reason: 'not known until the stores arrive');
      await t.container.read(serviceableStoresProvider.future);
      expect(t.container.read(deliveryServiceableProvider), isTrue);

      final none = await make(prefs: deviceLocationPrefs);
      none.catalog.answer = const [];
      await none.container.read(serviceableStoresProvider.future);
      expect(none.container.read(deliveryServiceableProvider), isFalse);
    });
  });
}

/// A server whose address list takes a moment.
class _SlowRepo extends FakeAddressRepository {
  _SlowRepo(this.gate);
  final Completer<void> gate;

  @override
  Future<List<SavedAddress>> list() async {
    await gate.future;
    return super.list();
  }
}
