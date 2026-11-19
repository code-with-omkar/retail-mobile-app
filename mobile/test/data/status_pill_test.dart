import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/core/widgets.dart';

void main() {
  for (final dark in [false, true]) {
    testWidgets('a status pill shows its label for every tone, ${dark ? 'dark' : 'light'}, even in a narrow space', (tester) async {
      await tester.pumpWidget(MaterialApp(
        theme: QC.theme(dark ? Brightness.dark : Brightness.light),
        home: Scaffold(
          body: Column(children: [
            for (final tone in StatusTone.values) SizedBox(width: 90, child: StatusPill('Out for delivery ${tone.name}', tone)),
          ]),
        ),
      ));
      expect(tester.takeException(), isNull);
      for (final tone in StatusTone.values) {
        expect(find.textContaining(tone.name), findsOneWidget);
      }
    });
  }
}
