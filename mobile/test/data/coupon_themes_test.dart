import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/data/repositories/coupon_repository.dart';
import 'package:quickcart_customer/features/cart/featured_coupons.dart';
import 'package:quickcart_customer/features/offers/card_extras.dart';

const _coupon = FeaturedCoupon(id: 'c1', code: 'FEST', kind: 'Flat', value: 50, minSubtotal: 200, design: 'diwali', ribbon: 'new', showAsPopup: true, animation: 'confetti');

Widget _app(Widget child) => ProviderScope(child: MaterialApp(theme: QC.theme(Brightness.light), home: Scaffold(body: child)));

void main() {
  test('a listed coupon is read with its look, and the picture gets the full address', () {
    final c = FeaturedCoupon.fromJson({'id': 'c1', 'code': 'FEST', 'kind': 'Percent', 'value': 10, 'maxDiscount': 40, 'imageUrl': '/media/offers/a.png', 'showAsPopup': true, 'animation': 'gift'}, imageBase: 'http://api.test');
    expect((c.imageUrl, c.maxDiscount, c.showAsPopup, c.animation), ('http://api.test/media/offers/a.png', 40, true, 'gift'));
  });

  testWidgets('the card shows the code, the condition and the ribbon, and Apply is off once applied', (tester) async {
    var taps = 0;
    await tester.pumpWidget(_app(FeaturedCouponCard(_coupon, applied: false, onApply: () => taps++)));
    expect(find.text('NEW'), findsOneWidget);
    expect(find.text('Use code FEST'), findsOneWidget);
    expect(find.textContaining('Minimum order'), findsOneWidget);
    await tester.tap(find.text('Apply'));
    expect(taps, 1);

    await tester.pumpWidget(_app(FeaturedCouponCard(_coupon, applied: true, onApply: () => taps++)));
    await tester.tap(find.text('Applied'));
    expect(taps, 1);
  });

  for (final kind in celebrationKeys) {
    testWidgets('the $kind animation plays once and removes itself', (tester) async {
      await tester.pumpWidget(_app(CelebrateOnce(kind, child: const Text('x'))));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));
      expect(find.byType(Celebration), findsOneWidget);
      await tester.pumpAndSettle();
      expect(find.byType(Celebration), findsNothing);
    });
  }

  testWidgets('several coupons in the cart sit side by side and swipe', (tester) async {
    const second = FeaturedCoupon(id: 'c2', code: 'SECOND', kind: 'Percent', value: 10);
    await tester.pumpWidget(ProviderScope(
      overrides: [featuredCouponsProvider.overrideWith((ref) async => const [_coupon, second])],
      child: MaterialApp(theme: QC.theme(Brightness.light), home: const Scaffold(body: FeaturedCoupons())),
    ));
    await tester.pumpAndSettle();
    final before = tester.getTopLeft(find.text('Use code SECOND'));
    expect(before.dy, closeTo(tester.getTopLeft(find.text('Use code FEST')).dy, 30));
    await tester.drag(find.text('Use code FEST'), const Offset(-500, 0));
    await tester.pumpAndSettle();
    expect(tester.getTopLeft(find.text('Use code SECOND')).dx, lessThan(before.dx));
  });

  testWidgets('an unknown animation plays nothing', (tester) async {
    await tester.pumpWidget(_app(const CelebrateOnce('fireworks', child: Text('x'))));
    await tester.pump();
    await tester.pump();
    expect(find.byType(Celebration), findsNothing);
  });
}
