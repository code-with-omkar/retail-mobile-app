import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/prefs.dart';
import '../../data/location_services.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/repositories/address_repository.dart';
import '../auth/auth_controller.dart';

final locationServiceProvider = Provider<LocationService>((ref) => const PluginLocationService());
final geocodingServiceProvider = Provider<GeocodingService>((ref) => PluginGeocodingService());
final addressRepositoryProvider = Provider<AddressRepository>((ref) => ApiAddressRepository(ref.watch(apiClientProvider)));

/// The signed-in customer's saved addresses (empty for a guest). Changes go through the server and the list is read again.
class SavedAddressesNotifier extends AsyncNotifier<List<SavedAddress>> {
  @override
  Future<List<SavedAddress>> build() async {
    final auth = ref.watch(authProvider);
    if (!auth.isSignedIn) return const [];
    return ref.watch(addressRepositoryProvider).list();
  }

  Future<SavedAddress> add(AddressDraft draft) async {
    final saved = await ref.read(addressRepositoryProvider).create(draft);
    await _reload();
    return saved;
  }

  Future<SavedAddress> edit(String id, AddressDraft draft) async {
    final saved = await ref.read(addressRepositoryProvider).update(id, draft);
    await _reload();
    return saved;
  }

  Future<void> makeDefault(String id) async {
    await ref.read(addressRepositoryProvider).setDefault(id);
    await _reload();
  }

  Future<void> remove(String id) async {
    await ref.read(addressRepositoryProvider).delete(id);
    await _reload();
  }

  Future<void> _reload() async {
    ref.invalidateSelf();
    await future;
  }
}

final savedAddressesProvider = AsyncNotifierProvider<SavedAddressesNotifier, List<SavedAddress>>(SavedAddressesNotifier.new);

/// A place chosen on this device only: a guest's delivery location, or the location of a signed-in customer who has no saved address yet.
/// Kept in preferences; never sent as an account address.
class DevicePlaceNotifier extends Notifier<DeliveryPlace?> {
  static const _key = 'device_place';

  @override
  DeliveryPlace? build() {
    final raw = ref.read(sharedPrefsProvider).getString(_key);
    if (raw == null) return null;
    try {
      return DeliveryPlace.fromJson(jsonDecode(raw));
    } catch (_) {
      return null; // damaged value: ask again rather than crash
    }
  }

  void set(DeliveryPlace place) {
    state = place;
    ref.read(sharedPrefsProvider).setString(_key, jsonEncode(place.toJson()));
  }

  void clear() {
    state = null;
    ref.read(sharedPrefsProvider).remove(_key);
  }
}

final devicePlaceProvider = NotifierProvider<DevicePlaceNotifier, DeliveryPlace?>(DevicePlaceNotifier.new);

/// Which saved address the customer chose to deliver to. Null means "the default".
class SelectedAddressIdNotifier extends Notifier<String?> {
  static const _key = 'selected_address_id';

  @override
  String? build() => ref.read(sharedPrefsProvider).getString(_key);

  void select(String id) {
    state = id;
    ref.read(sharedPrefsProvider).setString(_key, id);
  }
}

final selectedAddressIdProvider = NotifierProvider<SelectedAddressIdNotifier, String?>(SelectedAddressIdNotifier.new);

sealed class DeliveryState {
  const DeliveryState();
}

/// Still finding out (the session or the saved addresses are loading and nothing local is known).
final class DeliveryLoading extends DeliveryState {
  const DeliveryLoading();
}

/// No place chosen yet: the app asks "Where should we deliver?".
final class DeliveryNone extends DeliveryState {
  const DeliveryNone();
}

final class DeliveryChosen extends DeliveryState {
  const DeliveryChosen(this.place);
  final DeliveryPlace place;
}

/// Where the app delivers to. A signed-in customer's saved addresses come first (the chosen one, else the default); otherwise the
/// place kept on this device; in seed mode (offline development) a fixed place so the app is usable without a server.
final deliveryStateProvider = Provider<DeliveryState>((ref) {
  final config = ref.watch(appConfigProvider);
  final auth = ref.watch(authProvider);
  final device = ref.watch(devicePlaceProvider);

  DeliveryState fallback() {
    if (device != null) return DeliveryChosen(device);
    if (config.useSeedData) {
      return DeliveryChosen(DeliveryPlace(label: 'Home', line: 'Flat 402, Sea Breeze Apts, Bandra West, Mumbai 400050', latitude: config.defaultLatitude, longitude: config.defaultLongitude));
    }
    return const DeliveryNone();
  }

  // The session is still being checked. With a place on the device use it; offline development has no server to wait for.
  if (auth.status == AuthStatus.restoring) return device != null || config.useSeedData ? fallback() : const DeliveryLoading();
  if (!auth.isSignedIn) return fallback();

  final saved = ref.watch(savedAddressesProvider);
  if (saved.isLoading && !saved.hasValue) return device != null ? DeliveryChosen(device) : const DeliveryLoading();
  final list = saved.value ?? const <SavedAddress>[];
  if (list.isEmpty) return fallback();

  final chosenId = ref.watch(selectedAddressIdProvider);
  final address = list.where((a) => a.id == chosenId).firstOrNull ?? list.where((a) => a.isDefault).firstOrNull ?? list.first;
  return DeliveryChosen(DeliveryPlace(addressId: address.id, label: address.label, line: address.fullLine, latitude: address.latitude, longitude: address.longitude));
});

/// The place to deliver to, or null while unknown or not chosen.
final deliveryPlaceProvider = Provider<DeliveryPlace?>((ref) => switch (ref.watch(deliveryStateProvider)) {
      DeliveryChosen(:final place) => place,
      _ => null,
    });

/// Whether the current delivery place can still be served by the stores the app knows, null while unknown.
final deliveryServiceableProvider = Provider<bool?>((ref) {
  if (ref.watch(deliveryPlaceProvider) == null) return null;
  final stores = ref.watch(serviceableStoresProvider);
  return stores.hasValue ? stores.requireValue.isNotEmpty : null;
});
