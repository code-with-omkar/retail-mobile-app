import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/seed.dart';
import 'cart_controller.dart';

class CartScreen extends ConsumerWidget {
  const CartScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final t = ref.watch(cartTotalsProvider);
    final p = context.pal;
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Your cart'), actions: [if (!t.isEmpty) TextButton(onPressed: () => ref.read(cartProvider.notifier).clear(), child: Text(context.tr('Clear')))]),
      body: t.isEmpty
          ? EmptyState(icon: Icons.shopping_bag_outlined, title: context.tr('Your cart is empty'), message: context.tr('Add fresh groceries to get started.'), actionLabel: context.tr('Start shopping'), onAction: () => context.go('/'))
          : ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
              _StoreLine(ref.watch(selectedStoreProvider).value),
              _Banner(t),
              const SizedBox(height: 16),
              Surface(
                padding: const EdgeInsets.symmetric(vertical: 6),
                child: Column(children: [
                  for (var i = 0; i < t.lines.length; i++) ...[
                    if (i > 0) Divider(color: p.border),
                    _LineTile(t.lines[i], ref),
                  ],
                ]),
              ),
              const SizedBox(height: 16),
              Surface(
                child: Column(children: [
                  BillRow(context.tr('Item total'), rupees(t.subtotal)),
                  BillRow(context.tr('Delivery fee'), t.delivery == 0 ? context.tr('FREE') : rupees(t.delivery), accent: t.delivery == 0),
                  BillRow(context.tr('Handling fee'), rupees(t.handling)),
                  Divider(color: p.border),
                  BillRow(context.tr('To pay'), rupees(t.total), bold: true),
                  if (t.savings > 0) ...[
                    const SizedBox(height: 8),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(color: Pal.green.withValues(alpha: p.dark ? .18 : .3), borderRadius: BorderRadius.circular(QC.rField)),
                      child: Text(context.tr('You are saving {amt} on this order', {'amt': rupees(t.savings)}), style: TextStyle(color: p.dark ? Pal.green : const Color(0xFF006B22), fontWeight: FontWeight.w800, fontSize: 13)),
                    ),
                  ],
                ]),
              ),
            ]),
      bottom: t.isEmpty
          ? null
          : SafeArea(child: Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 12), child: PillButton('${context.tr('Checkout')} · ${rupees(t.total)}', arrow: true, onPressed: () => context.push('/checkout')))),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner(this.t);
  final CartTotals t;

  @override
  Widget build(BuildContext context) {
    final unlocked = t.awayFromFreeDelivery == 0;
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(QC.rCard)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(unlocked ? context.tr('You unlocked free delivery!') : context.tr('Add {amt} more for free delivery', {'amt': rupees(t.awayFromFreeDelivery)}), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 15)),
        const SizedBox(height: 10),
        ClipRRect(borderRadius: BorderRadius.circular(6), child: LinearProgressIndicator(value: unlocked ? 1 : (t.subtotal / freeDeliveryThreshold).clamp(0, 1), minHeight: 8, backgroundColor: Pal.ink.withValues(alpha: .15), color: Pal.ink)),
      ]),
    );
  }
}

/// Which store the cart is from (one cart belongs to one store).
class _StoreLine extends StatelessWidget {
  const _StoreLine(this.store);
  final NearestStore? store;

  @override
  Widget build(BuildContext context) => store == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Row(children: [
            const Icon(Icons.storefront_outlined, size: 18, color: Pal.mutedOnGround),
            const SizedBox(width: 8),
            Expanded(child: Text(context.tr('From {store}', {'store': store!.name}), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.mutedOnGround, fontWeight: FontWeight.w800))),
          ]),
        );
}

class _LineTile extends StatelessWidget {
  const _LineTile(this.line, this.ref);
  final CartLine line;
  final WidgetRef ref;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final product = line.product;
    return Dismissible(
      key: ValueKey(line.key),
      direction: DismissDirection.endToStart,
      onDismissed: (_) => ref.read(cartProvider.notifier).delete(line.key),
      background: Container(alignment: Alignment.centerRight, padding: const EdgeInsets.only(right: 24), color: Pal.pink, child: const Icon(Icons.delete_outline, color: Colors.white)),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: () => context.push('/product/${product.id}'),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          child: Row(children: [
            ProductThumb(product, size: 64),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(productName(context, product), maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 15)),
                Text(line.unitLabel, style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                const SizedBox(height: 4),
                PriceText(line.unitPrice, mrp: line.unitMrp, size: 16),
              ]),
            ),
            QuantityControl(product, line: line),
          ]),
        ),
      ),
    );
  }
}
