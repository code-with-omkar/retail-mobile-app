import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import 'address_controller.dart';

/// Whether this build has a Google Maps key (the Android side only says yes or no; the key is never read here). Tests replace it.
final mapsAvailableProvider = FutureProvider<bool>((ref) async {
  try {
    return await const MethodChannel('quickcart/maps').invokeMethod<bool>('hasKey') ?? false;
  } catch (_) {
    return false;
  }
});

/// The map itself. Behind a builder so tests can put a plain widget in its place: the real map cannot run in a unit test.
typedef MapViewBuilder = Widget Function(BuildContext context, ({double latitude, double longitude}) start, void Function(double latitude, double longitude) onCenterChanged);

Widget googleMapView(BuildContext context, ({double latitude, double longitude}) start, void Function(double latitude, double longitude) onCenterChanged) {
  var center = LatLng(start.latitude, start.longitude);
  return GoogleMap(
    initialCameraPosition: CameraPosition(target: center, zoom: 17),
    myLocationEnabled: true,
    myLocationButtonEnabled: true,
    zoomControlsEnabled: false,
    mapToolbarEnabled: false,
    onCameraMove: (position) => center = position.target,
    onCameraIdle: () => onCenterChanged(center.latitude, center.longitude),
  );
}

final mapViewBuilderProvider = Provider<MapViewBuilder>((ref) => googleMapView);

/// The answer of the map screen: the point under the pin and its address text when one was found.
class PickedPlace {
  const PickedPlace({required this.latitude, required this.longitude, this.line});
  final double latitude, longitude;
  final String? line;
}

/// Full-screen map with the pin fixed in the middle: move the map until the pin is on the door, then confirm.
class MapPickerScreen extends ConsumerStatefulWidget {
  const MapPickerScreen({super.key, required this.start});
  final ({double latitude, double longitude}) start;

  @override
  ConsumerState<MapPickerScreen> createState() => _MapPickerScreenState();
}

class _MapPickerScreenState extends ConsumerState<MapPickerScreen> {
  late double _lat = widget.start.latitude, _lng = widget.start.longitude;
  String? _line;
  bool _finding = true;
  int _lookup = 0;

  @override
  void initState() {
    super.initState();
    _findAddress();
  }

  void _moved(double latitude, double longitude) {
    _lat = latitude;
    _lng = longitude;
    _findAddress();
  }

  /// The address text of the spot under the pin. A missing text is not an error: the point still works.
  Future<void> _findAddress() async {
    final id = ++_lookup;
    if (mounted) setState(() => _finding = true);
    final text = await ref.read(geocodingServiceProvider).reverse(latitude: _lat, longitude: _lng);
    if (mounted && id == _lookup) {
      setState(() {
        _line = text;
        _finding = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('Choose on map'))),
      body: Stack(children: [
        Positioned.fill(child: ref.watch(mapViewBuilderProvider)(context, widget.start, _moved)),
        // The pin stays in the middle; its tip is on the spot.
        const IgnorePointer(child: Center(child: Padding(padding: EdgeInsets.only(bottom: 40), child: Icon(Icons.location_on, size: 48, color: Pal.pink)))),
        Positioned(
          left: QC.gutter,
          right: QC.gutter,
          bottom: 16,
          child: SafeArea(
            child: Surface(
              child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(context.tr('Move the map to put the pin on your door'), style: TextStyle(color: p.mutedOnCard, fontSize: 12, fontWeight: FontWeight.w700)),
                const SizedBox(height: 4),
                Text(_finding ? context.tr('Finding the address…') : (_line ?? context.tr('No address found here. You can still use this spot.')), key: const Key('map-address'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
                const SizedBox(height: 10),
                PillButton(context.tr('Confirm location'), height: 52, onPressed: () => Navigator.of(context).pop(PickedPlace(latitude: _lat, longitude: _lng, line: _line))),
              ]),
            ),
          ),
        ),
      ]),
    );
  }
}
