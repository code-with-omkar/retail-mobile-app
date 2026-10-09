import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/models.dart';
import '../../data/seed.dart';

class OrdersNotifier extends Notifier<List<Order>> {
  @override
  List<Order> build() => [
        Order(
          id: 'QC1024',
          lines: [CartLine(productById('milk'), 2), CartLine(productById('rice'), 5, productById('rice').packs.last)],
          total: 34.0 * 2 + 89.0 * 5 + handlingFee,
          placedAt: DateTime(2026, 10, 2, 18, 40),
          stage: OrderStage.delivered,
          address: 'Flat 402, Sea Breeze Apts, Bandra West, Mumbai 400050',
          payment: 'Cash on delivery',
        ),
      ];

  /// Places an order from the given lines and returns its id.
  String place(List<CartLine> lines, double total, String payment, String address) {
    final id = 'QC${1025 + state.length - 1}';
    state = [
      Order(id: id, lines: lines, total: total, placedAt: DateTime.now(), stage: OrderStage.placed, address: address, payment: payment),
      ...state,
    ];
    return id;
  }
}

final ordersProvider = NotifierProvider<OrdersNotifier, List<Order>>(OrdersNotifier.new);
