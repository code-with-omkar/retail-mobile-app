import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/providers.dart';
import '../address/address_controller.dart';
import '../cart/cart_controller.dart';
import '../orders/orders_controller.dart';

const _paymentMethods = [('Cash on delivery', Icons.payments_outlined), ('UPI', Icons.qr_code_2), ('Card', Icons.credit_card)];

class CheckoutScreen extends ConsumerStatefulWidget {
  const CheckoutScreen({super.key});
  @override
  ConsumerState<CheckoutScreen> createState() => _CheckoutScreenState();
}

class _CheckoutScreenState extends ConsumerState<CheckoutScreen> {
  String payment = _paymentMethods.first.$1;

  void _place() {
    final t = ref.read(cartTotalsProvider);
    final place = ref.read(deliveryPlaceProvider);
    // The server checks this again; here the customer is stopped before they try.
    if (t.isEmpty || place == null || ref.read(deliveryServiceableProvider) == false) return;
    final id = ref.read(ordersProvider.notifier).place(t.lines, t.total, payment, place.line);
    ref.read(cartProvider.notifier).clear();
    context.go('/order-success/$id');
  }

  @override
  Widget build(BuildContext context) {
    final t = ref.watch(cartTotalsProvider);
    final place = ref.watch(deliveryPlaceProvider);
    final blocked = place == null || ref.watch(deliveryServiceableProvider) == false;
    final eta = ref.watch(selectedStoreProvider).value?.estimatedMinutes;
    final p = context.pal;
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Checkout')),
      body: t.isEmpty
          ? EmptyState(icon: Icons.shopping_bag_outlined, title: context.tr('Nothing to check out'), message: context.tr('Your cart is empty.'), actionLabel: context.tr('Back to shop'), onAction: () => context.go('/'))
          : ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
              Surface(
                child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Container(width: 44, height: 44, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.location_on_outlined, color: Pal.ink)),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(place == null ? context.tr('Choose a delivery address') : context.tr('Delivering to {label}', {'label': context.tr(place.label)}), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
                      const SizedBox(height: 2),
                      Text(place?.line ?? context.tr('We need an address to deliver to.'), style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
                    ]),
                  ),
                  TextButton(onPressed: () => context.push('/addresses'), child: Text(context.tr(place == null ? 'Choose' : 'Change'), style: TextStyle(color: p.dark ? Pal.yellow : const Color(0xFF5A00C8)))),
                ]),
              ),
              if (place != null && blocked) ...[
                const SizedBox(height: 12),
                Surface(
                  color: Pal.pink,
                  child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    const Icon(Icons.location_off_outlined, color: Colors.white),
                    const SizedBox(width: 12),
                    Expanded(child: Text(context.tr('We do not deliver to this address yet. Choose another address to place your order.'), style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800))),
                  ]),
                ),
              ],
              const SizedBox(height: 12),
              Surface(
                color: Pal.green,
                child: Row(children: [
                  const Icon(Icons.bolt, color: Pal.ink),
                  const SizedBox(width: 12),
                  Expanded(child: Text(eta == null ? context.tr('Arriving soon') : context.tr('Arriving in about {n} minutes', {'n': '$eta'}), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900))),
                ]),
              ),
              const SizedBox(height: 24),
              Text(context.tr('Payment method'), style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
              const SizedBox(height: 12),
              for (final m in _paymentMethods) ...[
                GestureDetector(
                  onTap: () => setState(() => payment = m.$1),
                  child: Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: payment == m.$1 ? Pal.yellow : Colors.transparent, width: 2.5)),
                    child: Row(children: [
                      Icon(m.$2, color: p.onCard),
                      const SizedBox(width: 12),
                      Expanded(child: Text(context.tr(m.$1), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800))),
                      Icon(payment == m.$1 ? Icons.radio_button_checked : Icons.radio_button_off, color: payment == m.$1 ? (p.dark ? Pal.yellow : Pal.ink) : p.mutedOnCard),
                    ]),
                  ),
                ),
                const SizedBox(height: 10),
              ],
              const SizedBox(height: 14),
              Text(context.tr('Order summary'), style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
              const SizedBox(height: 12),
              Surface(
                child: Column(children: [
                  for (final l in t.lines) BillRow('${l.quantity} × ${productName(context, l.product)} (${l.unitLabel})', rupees(l.total)),
                  Divider(color: p.border),
                  BillRow(context.tr('Delivery fee'), t.delivery == 0 ? context.tr('FREE') : rupees(t.delivery), accent: t.delivery == 0),
                  BillRow(context.tr('Handling fee'), rupees(t.handling)),
                  Divider(color: p.border),
                  BillRow(context.tr('To pay'), rupees(t.total), bold: true),
                ]),
              ),
              const SizedBox(height: 12),
              Text(context.tr('Payment is a placeholder in this build; no charge is made.'), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 12)),
            ]),
      bottom: t.isEmpty ? null : SafeArea(child: Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 12), child: PillButton('${context.tr('Place order')} · ${rupees(t.total)}', arrow: true, onPressed: blocked ? null : _place))),
    );
  }
}
