import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../cart/cart_controller.dart';
import 'orders_controller.dart';
import 'orders_screen.dart';

class OrderSuccessScreen extends ConsumerWidget {
  const OrderSuccessScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) => AppScaffold(
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(children: [
              const Spacer(),
              Container(width: 120, height: 120, decoration: const BoxDecoration(color: Pal.green, shape: BoxShape.circle), child: const Icon(Icons.check_rounded, size: 72, color: Pal.ink)),
              const SizedBox(height: 28),
              Text(context.tr('Order placed!'), style: const TextStyle(color: Colors.white, fontSize: 34, fontWeight: FontWeight.w900)),
              const SizedBox(height: 8),
              Consumer(builder: (context, ref, _) {
                final eta = ref.watch(selectedStoreProvider).value?.estimatedMinutes;
                return Text(eta == null ? context.tr('Order #{id} is being prepared.', {'id': id}) : context.tr('Order #{id} is being prepared.\nExpected in about {n} minutes.', {'id': id, 'n': '$eta'}), textAlign: TextAlign.center, style: const TextStyle(color: Pal.mutedOnGround, height: 1.4));
              }),
              const Spacer(),
              PillButton(context.tr('Track order'), arrow: true, onPressed: () {
                // Orders underneath, so Back from tracking lands on the orders list instead of leaving the app.
                context.go('/orders');
                context.push('/order/$id');
              }),
              const SizedBox(height: 12),
              SoftPillButton(context.tr('Continue shopping'), height: 60, onPressed: () => context.go('/')),
            ]),
          ),
        ),
      );
}

class OrderDetailScreen extends ConsumerWidget {
  const OrderDetailScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final order = ref.watch(ordersProvider).where((o) => o.id == id).firstOrNull;
    final p = context.pal;
    if (order == null) {
      return AppScaffold(appBar: appTopBar(context, ''), body: EmptyState(icon: Icons.receipt_long_outlined, title: context.tr('Order not found'), message: context.tr('We could not find this order.'), actionLabel: context.tr('View orders'), onAction: () => context.go('/orders')));
    }
    return AppScaffold(
      appBar: appTopBar(context, '${context.tr('Order')} #${order.id}', backFallback: '/orders'),
      body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
        Surface(child: _Timeline(order.stage)),
        const SizedBox(height: 16),
        Surface(
          child: Column(children: [
            for (final l in order.lines)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 6),
                child: Row(children: [
                  ProductThumb(l.product, size: 48, radius: 14),
                  const SizedBox(width: 12),
                  Expanded(child: Text('${productName(context, l.product)}\n${l.unitLabel} × ${l.quantity}', style: TextStyle(color: p.onCard, height: 1.3, fontWeight: FontWeight.w600))),
                  Text(rupees(l.total), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
                ]),
              ),
            Divider(color: p.border),
            BillRow(context.tr('Paid via'), context.tr(order.payment)),
            BillRow(context.tr('Total'), rupees(order.total), bold: true),
          ]),
        ),
        const SizedBox(height: 16),
        Surface(
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Container(width: 44, height: 44, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.location_on_outlined, color: Pal.ink)),
            const SizedBox(width: 12),
            Expanded(child: Text(order.address, style: TextStyle(color: p.mutedOnCard))),
          ]),
        ),
        const SizedBox(height: 16),
        PillButton(
          context.tr('Reorder'),
          height: 60,
          onPressed: () {
            final cart = ref.read(cartProvider.notifier);
            for (final l in order.lines) {
              if (!l.product.inStock) continue;
              ref.read(productStoreProvider).put(l.product);
              cart.add(l.key, l.quantity);
            }
            context.push('/cart');
          },
        ),
      ]),
    );
  }
}

class _Timeline extends StatelessWidget {
  const _Timeline(this.current);
  final OrderStage current;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final on = p.dark ? Pal.yellow : Pal.ink;
    return Column(children: [
      for (final s in OrderStage.values)
        Builder(builder: (_) {
          final reached = s.index <= current.index;
          final last = s == OrderStage.values.last;
          return IntrinsicHeight(
            child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              SizedBox(
                width: 28,
                child: Column(children: [
                  Container(
                    width: 24,
                    height: 24,
                    decoration: BoxDecoration(shape: BoxShape.circle, color: reached ? on : Colors.transparent, border: Border.all(color: reached ? on : p.mutedOnCard, width: 2)),
                    child: reached ? Icon(Icons.check, size: 15, color: p.dark ? Pal.ink : Pal.yellow) : null,
                  ),
                  if (!last) Expanded(child: Container(width: 3, color: s.index < current.index ? on : p.border)),
                ]),
              ),
              const SizedBox(width: 14),
              Padding(
                padding: const EdgeInsets.only(bottom: 20),
                child: Text(stageLabel(context, s), style: TextStyle(fontWeight: s == current ? FontWeight.w900 : FontWeight.w500, fontSize: s == current ? 17 : 15, color: reached ? p.onCard : p.mutedOnCard)),
              ),
            ]),
          );
        }),
    ]);
  }
}
