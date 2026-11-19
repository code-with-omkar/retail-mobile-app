import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/polling.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_widgets.dart';
import 'orders_controller.dart';

String stageLabel(BuildContext context, OrderStage s) => context.tr(switch (s) {
      OrderStage.awaitingPayment => 'Awaiting payment',
      OrderStage.placed => 'Order placed',
      OrderStage.packed => 'Packed',
      OrderStage.onTheWay => 'Out for delivery',
      OrderStage.delivered => 'Delivered',
      OrderStage.rejected => 'Declined',
      OrderStage.cancelled => 'Cancelled',
    });

String formatDate(DateTime d) {
  const m = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  final h = d.hour % 12 == 0 ? 12 : d.hour % 12;
  return '${d.day} ${m[d.month - 1]}, $h:${d.minute.toString().padLeft(2, '0')} ${d.hour >= 12 ? 'PM' : 'AM'}';
}

/// The photo of an ordered product when the app knows the product, otherwise a plain basket.
class OrderLineThumb extends ConsumerWidget {
  const OrderLineThumb(this.line, {super.key, this.size = 48, this.radius = 14});
  final OrderLine line;
  final double size, radius;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final product = ref.watch(productStoreProvider).get(line.key) ?? ref.watch(productStoreProvider).get(line.productId);
    if (product != null) return ProductThumb(product, size: size, radius: radius);
    final p = context.pal;
    return ClipRRect(borderRadius: BorderRadius.circular(radius), child: Container(width: size, height: size, color: p.soft, child: Icon(Icons.shopping_basket_outlined, color: p.onSoft)));
  }
}

class OrdersScreen extends ConsumerWidget {
  const OrdersScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final orders = ref.watch(ordersProvider);
    final guest = ref.watch(authProvider).status == AuthStatus.signedOut;
    final failing = ref.watch(ordersSyncFailingProvider);
    // Orders still on their way are checked every 30 seconds while this tab is in front (nothing in the background or on another tab).
    return Polling(
      interval: const Duration(seconds: 30),
      active: ref.watch(hasActiveOrderProvider) && TickerMode.valuesOf(context).enabled,
      onTick: ref.read(ordersProvider.notifier).poll,
      child: SafeArea(
      bottom: false,
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Padding(padding: EdgeInsets.fromLTRB(QC.gutter, 20, QC.gutter, failing ? 6 : 16), child: Text(context.tr('Your orders'), style: QC.display(32, color: context.pal.onGround))),
        if (failing) Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 10), child: Text(context.tr('Could not refresh. Showing the last update.'), style: TextStyle(color: context.pal.mutedOnGround, fontSize: 12, fontWeight: FontWeight.w700))),
        Expanded(
          child: guest
              ? EmptyState(icon: Icons.lock_outline, title: context.tr('Sign in to see your orders'), message: context.tr('Your orders are saved to your account.'), actionLabel: context.tr('Log in'), onAction: () => context.push(withNext('/welcome', '/orders')))
              : orders.when(
                  skipLoadingOnReload: true,
                  loading: () => ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24), children: [for (var i = 0; i < 3; i++) const Padding(padding: EdgeInsets.only(bottom: 12), child: SkeletonBox(height: 120, radius: 24))]),
                  error: (e, _) => ErrorState(error: e, onRetry: () => ref.read(ordersProvider.notifier).refresh()),
                  data: (list) => list.isEmpty
                      ? EmptyState(icon: Icons.receipt_long_outlined, title: context.tr('No orders yet'), message: context.tr('Your orders will show up here.'), actionLabel: context.tr('Start shopping'), onAction: () => context.go('/'))
                      : RefreshIndicator(
                          onRefresh: () => ref.read(ordersProvider.notifier).refresh(),
                          child: ListView.separated(
                            physics: const AlwaysScrollableScrollPhysics(),
                            padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24),
                            itemCount: list.length,
                            separatorBuilder: (_, _) => const SizedBox(height: 12),
                            itemBuilder: (_, i) => _OrderCard(list[i]),
                          ),
                        ),
                ),
        ),
      ]),
      ),
    );
  }
}

class _OrderCard extends StatelessWidget {
  const _OrderCard(this.order);
  final Order order;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return GestureDetector(
      onTap: () => context.push('/order/${order.id}'),
      // v4: a white card with a thin line (the dark theme keeps its filled card), the shop and status on the first line.
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: p.dark ? p.card : Colors.white, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: p.border)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
            Flexible(child: Text('#${order.number}', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 15))),
            const SizedBox(width: 8),
            StatusPill(stageLabel(context, order.stage), switch (order.stage) {
              OrderStage.delivered => StatusTone.done,
              OrderStage.cancelled || OrderStage.rejected => StatusTone.stopped,
              OrderStage.onTheWay => StatusTone.moving,
              _ => StatusTone.progress,
            }),
          ]),
          const SizedBox(height: 4),
          Text([if (order.storeName != null) order.storeName!, formatDate(order.placedAt)].join(' · '), maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
          const SizedBox(height: 14),
          Row(children: [
            for (final l in order.lines.take(4)) Padding(padding: const EdgeInsets.only(right: 6), child: OrderLineThumb(l, size: 44)),
            const Spacer(),
            Text(context.tr('{n} items · ₹{amt}', {'n': '${order.itemCount}', 'amt': amount(order.total)}), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
          ]),
        ]),
      ),
    );
  }
}
