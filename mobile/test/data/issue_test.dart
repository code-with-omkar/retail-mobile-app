import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/issue_repository.dart';
import 'package:quickcart_customer/features/orders/report_problem.dart';

import '../support/fake_adapter.dart';

class _Issues implements IssueRepository {
  final sent = <String>[];

  @override
  Future<OrderIssue> mine(String orderId) async => OrderIssue(reportUntil: DateTime.now().add(const Duration(hours: 5)));

  @override
  Future<void> report(String orderId, {required String kind, required List<IssueLine> lines, String note = ''}) async =>
      sent.add('$orderId $kind ${lines.map((l) => '${l.productId}x${l.quantity}').join(',')} $note');
}

final _order = Order(
  id: 'o1',
  number: 'ORD-1',
  storeId: 's1',
  lines: const [OrderLine(productId: 'rice', name: 'Rice', unitPrice: 50, quantity: 1)],
  subtotal: 50,
  deliveryFee: 0,
  handlingFee: 0,
  total: 50,
  placedAt: DateTime(2026, 11, 26),
  stage: OrderStage.delivered,
  address: 'x',
  payment: 'CashOnDelivery',
);

void main() {
  test('the report is read and sent as the server expects', () async {
    final adapter = FakeAdapter()
      ..replyJson({'success': true, 'data': {'issue': {'kind': 'Damaged', 'status': 'Approved', 'claimedAmount': 50, 'approvedAmount': 40, 'refundMethod': 'Cash'}, 'reportUntil': null}})
      ..replyJson({'success': true, 'data': {}});
    final repo = ApiIssueRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));

    final found = await repo.mine('o1');
    expect((found.report!.approvedAmount, found.report!.cash, found.report!.cashPaid), (40, true, false));
    await repo.report('o1', kind: 'Missing', lines: const [IssueLine(productId: 'p1', quantity: 2)]);
    expect(adapter.requests[1].path, '/api/customer/orders/o1/issue');
    expect(adapter.requests[1].data, {'kind': 'Missing', 'note': null, 'items': [{'productId': 'p1', 'variantId': null, 'quantity': 2}]});
  });

  testWidgets('a delivered order offers a report, which needs an item and is then sent', (tester) async {
    final issues = _Issues();
    await tester.pumpWidget(ProviderScope(
      overrides: [issueRepositoryProvider.overrideWithValue(issues)],
      child: MaterialApp(theme: QC.theme(Brightness.light), home: Scaffold(body: OrderIssueSection(_order))),
    ));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Report a problem'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Send report'));
    await tester.pumpAndSettle();
    expect(find.text('Choose at least one item.'), findsOneWidget);

    await tester.tap(find.text('Item damaged'));
    await tester.tap(find.text('Rice'));
    await tester.pump();
    await tester.tap(find.text('Send report'));
    await tester.pumpAndSettle();
    expect(issues.sent.single, 'o1 Damaged ricex1 ');
  });
}
