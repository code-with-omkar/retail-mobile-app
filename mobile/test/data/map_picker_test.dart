import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/l10n.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/address/map_picker.dart';

import '../support/location_fakes.dart';

/// The picker screen with a plain widget in place of the real map: a button stands for the customer moving the map.
class _Host extends StatelessWidget {
  const _Host({required this.onPicked});
  final void Function(PickedPlace?) onPicked;

  @override
  Widget build(BuildContext context) => Center(
        child: ElevatedButton(
          onPressed: () async => onPicked(await Navigator.of(context).push<PickedPlace>(MaterialPageRoute(builder: (_) => const MapPickerScreen(start: (latitude: 19.0, longitude: 73.0))))),
          child: const Text('open'),
        ),
      );
}

Future<void> _pump(WidgetTester tester, {required FakeGeocodingService geocoder, required void Function(PickedPlace?) onPicked, List<(double, double)>? moves}) async {
  tester.view.physicalSize = const Size(390 * 3, 800 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(ProviderScope(
    retry: noAutomaticRetry,
    overrides: [
      geocodingServiceProvider.overrideWithValue(geocoder),
      mapViewBuilderProvider.overrideWithValue((context, start, onCenterChanged) => Column(children: [
            Text('map at ${start.latitude},${start.longitude}', key: const Key('fake-map')),
            for (final move in moves ?? const <(double, double)>[]) TextButton(onPressed: () => onCenterChanged(move.$1, move.$2), child: Text('move to ${move.$1}')),
          ])),
    ],
    child: MaterialApp(
      theme: QC.theme(Brightness.light),
      localizationsDelegates: const [AppStrings.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
      supportedLocales: const [Locale('en'), Locale('mr')],
      home: _Host(onPicked: onPicked),
    ),
  ));
  await tester.pumpAndSettle();
  await tester.tap(find.text('open'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('the map opens where it is told to and shows the address of that spot', (tester) async {
    await _pump(tester, geocoder: FakeGeocodingService()..reverseResult = 'Plot 5, Kharghar', onPicked: (_) {});

    expect(find.text('map at 19.0,73.0'), findsOneWidget);
    expect(find.byKey(const Key('map-address')), findsOneWidget);
    expect(find.text('Plot 5, Kharghar'), findsOneWidget);
  });

  testWidgets('moving the map looks up the new spot, and confirming returns it with its address', (tester) async {
    PickedPlace? picked;
    final geocoder = FakeGeocodingService()..reverseResult = 'First spot';
    await _pump(tester, geocoder: geocoder, onPicked: (p) => picked = p, moves: const [(19.05, 73.07)]);

    geocoder.reverseResult = 'Second spot';
    await tester.tap(find.text('move to 19.05'));
    await tester.pumpAndSettle();
    expect(find.text('Second spot'), findsOneWidget);

    await tester.tap(find.text('Confirm location'));
    await tester.pumpAndSettle();

    expect((picked!.latitude, picked!.longitude, picked!.line), (19.05, 73.07, 'Second spot'));
  });

  testWidgets('confirming without moving returns the starting spot', (tester) async {
    PickedPlace? picked;
    await _pump(tester, geocoder: FakeGeocodingService(), onPicked: (p) => picked = p);

    await tester.tap(find.text('Confirm location'));
    await tester.pumpAndSettle();

    expect((picked!.latitude, picked!.longitude), (19.0, 73.0));
  });

  testWidgets('a spot with no address can still be used', (tester) async {
    PickedPlace? picked;
    await _pump(tester, geocoder: FakeGeocodingService()..reverseResult = null, onPicked: (p) => picked = p);

    expect(find.text('No address found here. You can still use this spot.'), findsOneWidget);
    await tester.tap(find.text('Confirm location'));
    await tester.pumpAndSettle();

    expect(picked!.line, isNull);
  });

  testWidgets('going back without confirming returns nothing', (tester) async {
    PickedPlace? picked = const PickedPlace(latitude: 1, longitude: 1);
    await _pump(tester, geocoder: FakeGeocodingService(), onPicked: (p) => picked = p);

    await tester.pageBack();
    await tester.pumpAndSettle();

    expect(picked, isNull);
  });
}
