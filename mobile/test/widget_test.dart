import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/main.dart';

void main() {
  testWidgets('customer home renders catalog and adds to cart', (tester) async {
    await tester.pumpWidget(const QuickCartApp());

    expect(find.text('Popular near you'), findsOneWidget);
    expect(find.text('Tomato'), findsOneWidget);
    expect(find.text('Farm Milk'), findsOneWidget);

    final addButton = find.byIcon(Icons.add_circle).first;
    await tester.drag(find.byType(CustomScrollView), const Offset(0, -520));
    await tester.pumpAndSettle();
    await tester.tap(addButton);
    await tester.pump();

    expect(find.text('1'), findsNWidgets(2));
  });
}
