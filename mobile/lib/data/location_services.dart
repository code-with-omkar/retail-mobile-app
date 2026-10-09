import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart' show PlatformException;
import 'package:geocoding/geocoding.dart' as geo;
import 'package:geolocator/geolocator.dart';

/// Why a position could or could not be had.
enum LocationAccess {
  granted,

  /// The customer said no this time; asking again is fine.
  denied,

  /// "Don't ask again": only the system settings can change it.
  deniedForever,

  /// Location is switched off on the phone.
  servicesOff,

  /// Permission is fine but no position arrived (timeout, no signal, platform error).
  unavailable,
}

class LocationFix {
  const LocationFix(this.access, {this.latitude, this.longitude});
  final LocationAccess access;
  final double? latitude, longitude;

  bool get hasPosition => access == LocationAccess.granted && latitude != null && longitude != null;
}

/// The phone's location, behind an interface so screens and tests do not depend on the plugin.
abstract interface class LocationService {
  /// Asks for permission when needed (never blocks forever) and returns one position.
  Future<LocationFix> currentPosition();

  /// Opens the system screen that can fix [access]: app settings for [LocationAccess.deniedForever], location settings for [LocationAccess.servicesOff].
  Future<void> openSettings(LocationAccess access);
}

/// Android location through `geolocator`. Approximate location is enough: it finds the stores, and the customer confirms the address.
class PluginLocationService implements LocationService {
  const PluginLocationService();

  @override
  Future<LocationFix> currentPosition() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return const LocationFix(LocationAccess.servicesOff);
      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
      if (permission == LocationPermission.deniedForever) return const LocationFix(LocationAccess.deniedForever);
      if (permission == LocationPermission.denied || permission == LocationPermission.unableToDetermine) return const LocationFix(LocationAccess.denied);
      final position = await Geolocator.getCurrentPosition(locationSettings: const LocationSettings(accuracy: LocationAccuracy.medium, timeLimit: Duration(seconds: 15)));
      return LocationFix(LocationAccess.granted, latitude: position.latitude, longitude: position.longitude);
    } on TimeoutException {
      return const LocationFix(LocationAccess.unavailable);
    } catch (e) {
      if (kDebugMode) debugPrint('[location] failed: ${e.runtimeType}');
      return const LocationFix(LocationAccess.unavailable);
    }
  }

  @override
  Future<void> openSettings(LocationAccess access) async {
    if (access == LocationAccess.servicesOff) {
      await Geolocator.openLocationSettings();
    } else {
      await Geolocator.openAppSettings();
    }
  }
}

/// The address lookup could not run (no connection, no geocoder on the phone).
class GeocodingUnavailable implements Exception {
  const GeocodingUnavailable();
}

/// Turns a pin into address text and typed address text into a pin. Behind an interface for the same reason as [LocationService].
abstract interface class GeocodingService {
  /// A readable address for a point, or null when none is known.
  Future<String?> reverse({required double latitude, required double longitude});

  /// The point for typed address text, or null when nothing matches. Throws [GeocodingUnavailable] when the lookup cannot run.
  Future<({double latitude, double longitude})?> forward(String address);
}

/// The platform geocoder (Google's on Android, no API key needed).
class PluginGeocodingService implements GeocodingService {
  PluginGeocodingService();

  final _geocoding = geo.Geocoding();

  @override
  Future<String?> reverse({required double latitude, required double longitude}) async {
    try {
      final places = await _geocoding.placemarkFromCoordinates(latitude, longitude);
      if (places.isEmpty) return null;
      final p = places.first;
      final parts = <String>[];
      for (final part in [p.street ?? p.name, p.subLocality, p.locality, p.administrativeArea, p.postalCode]) {
        final text = part?.trim();
        if (text != null && text.isNotEmpty && !parts.contains(text)) parts.add(text);
      }
      return parts.isEmpty ? null : parts.join(', ');
    } catch (_) {
      return null; // a missing address text is not an error: the pin still works
    }
  }

  @override
  Future<({double latitude, double longitude})?> forward(String address) async {
    try {
      final found = await _geocoding.locationFromAddress(address.trim());
      if (found.isEmpty) return null;
      return (latitude: found.first.latitude, longitude: found.first.longitude);
    } on PlatformException catch (e) {
      // Android reports "nothing found" as an error with this code; anything else means the lookup could not run.
      if (e.code == 'NOT_FOUND') return null;
      throw const GeocodingUnavailable();
    } catch (_) {
      throw const GeocodingUnavailable();
    }
  }
}
