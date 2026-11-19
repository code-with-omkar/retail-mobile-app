import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/l10n.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/features/orders/order_detail_screen.dart';

import '../support/commerce_fakes.dart';

Future<String> _line(WidgetTester tester, Order order, {Locale locale = const Locale('en')}) async {
  var text = '';
  await tester.pumpWidget(MaterialApp(
    locale: locale,
    supportedLocales: AppStrings.supported,
    localizationsDelegates: const [AppStrings.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
    home: Builder(builder: (context) {
      text = endedMoneyLine(context, order);
      return const SizedBox();
    }),
  ));
  await tester.pump();
  return text;
}

Order _online(PaymentState state) => sampleOrder(stage: OrderStage.cancelled, payment: 'Online').withPayment(stage: OrderStage.cancelled, state: state);

void main() {
  testWidgets('cash orders and online orders that were never paid say nothing was charged', (tester) async {
    expect(await _line(tester, sampleOrder(stage: OrderStage.cancelled)), 'You were not charged for this order.');
    expect(await _line(tester, _online(PaymentState.created)), 'You were not charged for this order.');
    expect(await _line(tester, _online(PaymentState.failed)), 'You were not charged for this order.');
  });

  testWidgets('a refund on its way names the amount and how long it takes', (tester) async {
    final order = _online(PaymentState.refunding);
    final line = await _line(tester, order);
    expect(line, contains('₹'));
    expect(line, contains('5 to 7 working days'));
  });

  testWidgets('a finished refund and a failed refund are told apart', (tester) async {
    expect(await _line(tester, _online(PaymentState.refunded)), contains('was refunded to your original payment method'));
    expect(await _line(tester, _online(PaymentState.refundFailed)), contains('Please contact the store'));
  });

  testWidgets('in Marathi every line is Marathi', (tester) async {
    for (final state in [PaymentState.created, PaymentState.refunding, PaymentState.refunded, PaymentState.refundFailed]) {
      final line = await _line(tester, _online(state), locale: const Locale('mr'));
      expect(RegExp(r'[ऀ-ॿ]').hasMatch(line), isTrue, reason: '$state: $line');
    }
  });
}
