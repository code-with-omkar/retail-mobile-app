import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_widgets.dart';
import 'orders_controller.dart';

String stageLabel(BuildContext context, OrderStage s) => context.tr(switch (s) {
      OrderStage.placed => 'Order placed',
      OrderStage.packed => 'Packed',
      OrderStage.onTheWay => 'Out for delivery',
      OrderStage.delivered => 'Delivered',
    });

String formatDate(DateTime d) {
  const m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  final h = d.hour % 12 == 0 ? 12 : d.hour % 12;
  return '${d.day} ${m[d.month - 1]}, $h:${d.minute.toString().padLeft(2, '0')} ${d.hour >= 12 ? 'PM' : 'AM'}';
}

class OrdersScreen extends ConsumerWidget {
  const OrdersScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final orders = ref.watch(ordersProvider);
    final guest = ref.watch(authProvider).status == AuthStatus.signedOut;
    return SafeArea(
      bottom: false,
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 20, QC.gutter, 16), child: Text(context.tr('Your orders'), style: const TextStyle(color: Colors.white, fontSize: 32, fontWeight: FontWeight.w900))),
        Expanded(
          child: guest
              ? EmptyState(icon: Icons.lock_outline, title: context.tr('Sign in to see your orders'), message: context.tr('Your orders are saved to your account.'), actionLabel: context.tr('Log in'), onAction: () => context.push(withNext('/welcome', '/orders')))
              : orders.isEmpty
              ? EmptyState(icon: Icons.receipt_long_outlined, title: context.tr('No orders yet'), message: context.tr('Your orders will show up here.'), actionLabel: context.tr('Start shopping'), onAction: () => context.go('/'))
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24),
                  itemCount: orders.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 12),
                  itemBuilder: (_, i) => _OrderCard(orders[i]),
                ),
        ),
      ]),
    );
  }
}

class _OrderCard extends StatelessWidget {
  const _OrderCard(this.order);
  final Order order;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final done = order.stage == OrderStage.delivered;
    return GestureDetector(
      onTap: () => context.push('/order/${order.id}'),
      child: Surface(
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
            Text('#${order.id}', style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 16)),
            Chip2(stageLabel(context, order.stage), dot: done ? Pal.green : Pal.yellow, background: Pal.ink, foreground: Colors.white),
          ]),
          const SizedBox(height: 4),
          Text(formatDate(order.placedAt), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
          const SizedBox(height: 14),
          Row(children: [
            for (final l in order.lines.take(4)) Padding(padding: const EdgeInsets.only(right: 6), child: ProductThumb(l.product, size: 44, radius: 14)),
            const Spacer(),
            Text(context.tr('{n} items · ₹{amt}', {'n': '${order.itemCount}', 'amt': amount(order.total)}), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
          ]),
        ]),
      ),
    );
  }
}
