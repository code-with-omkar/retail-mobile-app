import 'package:geolocator/geolocator.dart';

/// Where the phone is now, or null when it cannot be known (location off, permission refused, no fix in time).
typedef LocationSource = Future<({double latitude, double longitude})?> Function();

/// The phone's position. Asks for permission the first time; a refusal simply means nothing is shared.
Future<({double latitude, double longitude})?> currentPosition() async {
  try {
    if (!await Geolocator.isLocationServiceEnabled()) return null;
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
    if (permission == LocationPermission.denied || permission == LocationPermission.deniedForever) return null;
    final p = await Geolocator.getCurrentPosition(locationSettings: const LocationSettings(accuracy: LocationAccuracy.high, timeLimit: Duration(seconds: 10)));
    return (latitude: p.latitude, longitude: p.longitude);
  } catch (_) {
    return null;
  }
}
