import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../data/api/api_exception.dart';
import '../../data/location_services.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';
import '../cart/cart_controller.dart';
import 'address_controller.dart';

/// What the editor starts with: an address to change, or a position and text found by "use my current location".
class AddressEditorArgs {
  const AddressEditorArgs({this.existing, this.line, this.latitude, this.longitude});
  final SavedAddress? existing;
  final String? line;
  final double? latitude, longitude;
}

/// One cart belongs to one store. When the new place is not served by the cart's store the customer is asked first, and the cart is
/// emptied when they agree. Returns false when they keep the current place.
Future<bool> cartAllowsPlace(BuildContext context, WidgetRef ref, double latitude, double longitude) async {
  if (ref.read(cartProvider).isEmpty) return true;
  final current = ref.read(selectedStoreProvider).value;
  if (current == null) return true;
  try {
    final stores = await ref.read(catalogRepositoryProvider).stores(latitude: roundCoordinate(latitude), longitude: roundCoordinate(longitude));
    if (stores.any((s) => s.id == current.id)) return true;
  } catch (_) {
    return true; // cannot tell: the store lookup falls back to the nearest store by itself
  }
  if (!context.mounted) return false;
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(context.tr('Change delivery address?')),
      content: Text(context.tr('Your cart has items from {store}, which does not deliver to this address. Changing the address will empty your cart.', {'store': current.name})),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(false), child: Text(context.tr('Keep current address'))),
        TextButton(onPressed: () => Navigator.of(context).pop(true), child: Text(context.tr('Change and empty cart'))),
      ],
    ),
  );
  if (confirmed != true) return false;
  ref.read(cartProvider.notifier).clear();
  return true;
}

/// Makes a saved address the one to deliver to. Returns true when it is now selected.
Future<bool> chooseSavedAddress(BuildContext context, WidgetRef ref, SavedAddress address) async {
  final current = ref.read(deliveryPlaceProvider);
  if (current?.addressId == address.id) return true;
  if (!await cartAllowsPlace(context, ref, address.latitude, address.longitude)) return false;
  ref.read(selectedAddressIdProvider.notifier).select(address.id);
  return true;
}

/// Makes a place that lives on this device the one to deliver to (guests, and "use my current location" before anything is saved).
Future<bool> chooseDevicePlace(BuildContext context, WidgetRef ref, DeliveryPlace place) async {
  if (!await cartAllowsPlace(context, ref, place.latitude, place.longitude)) return false;
  ref.read(devicePlaceProvider.notifier).set(place);
  return true;
}

typedef FoundLocation = ({double latitude, double longitude, String line});

/// Asks for the phone's position, explaining every way it can fail, and looks up a readable address for it. Null means "no position":
/// the reason has already been shown, and the customer can enter an address instead.
Future<FoundLocation?> locateMe(BuildContext context, WidgetRef ref) async {
  final service = ref.read(locationServiceProvider);
  final fix = await service.currentPosition();
  if (!context.mounted) return null;

  switch (fix.access) {
    case LocationAccess.granted:
      final lat = fix.latitude!, lng = fix.longitude!;
      final text = await ref.read(geocodingServiceProvider).reverse(latitude: lat, longitude: lng);
      if (!context.mounted) return null;
      return (latitude: lat, longitude: lng, line: text ?? context.tr('Current location'));
    case LocationAccess.denied:
      _say(context, 'We need permission to use your location. You can enter an address instead.');
    case LocationAccess.unavailable:
      _say(context, 'Could not get your location. Try again, or enter an address.');
    case LocationAccess.deniedForever:
      await _explain(context, service, fix.access, 'Location permission needed', 'QuickCart cannot see your location because permission was turned off. You can allow it in the app settings, or enter an address instead.');
    case LocationAccess.servicesOff:
      await _explain(context, service, fix.access, 'Location is off', 'Turn on location on your phone to use your current location, or enter an address instead.');
  }
  return null;
}

void _say(BuildContext context, String key) => ScaffoldMessenger.of(context)
  ..hideCurrentSnackBar()
  ..showSnackBar(SnackBar(content: Text(context.tr(key))));

Future<void> _explain(BuildContext context, LocationService service, LocationAccess access, String title, String message) async {
  final open = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(context.tr(title)),
      content: Text(context.tr(message)),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(false), child: Text(context.tr('Not now'))),
        TextButton(onPressed: () => Navigator.of(context).pop(true), child: Text(context.tr('Open settings'))),
      ],
    ),
  );
  if (open == true) await service.openSettings(access);
}

/// "Use my current location": a guest's device place is set at once; a signed-in customer is taken to the editor to save it as an address.
Future<void> useCurrentLocation(BuildContext context, WidgetRef ref) async {
  final found = await locateMe(context, ref);
  if (found == null || !context.mounted) return;
  if (ref.read(authProvider).isSignedIn) {
    await context.push('/addresses/new', extra: AddressEditorArgs(line: found.line, latitude: found.latitude, longitude: found.longitude));
    return;
  }
  final place = DeliveryPlace(label: 'Current location', line: found.line, latitude: found.latitude, longitude: found.longitude);
  if (await chooseDevicePlace(context, ref, place) && context.mounted) {
    final router = GoRouter.of(context);
    if (router.canPop()) router.pop();
  }
}

/// What to tell the customer about a failed address call. The server's own validation messages are written for customers and shown as
/// they are; everything else uses translated text, so server internals never reach the screen.
String addressErrorText(BuildContext context, Object error) {
  switch (error) {
    case ConflictException():
      return context.tr('You have reached the limit of saved addresses. Delete one to add another.');
    case NotFoundException():
      return context.tr('This address no longer exists.');
    case ValidationException(:final errors):
      return errors.isEmpty ? context.tr(error.userMessageKey) : errors.take(3).join('\n');
    case ApiException():
      return context.tr(error.userMessageKey);
    default:
      return context.tr('Something went wrong. Please try again.');
  }
}
