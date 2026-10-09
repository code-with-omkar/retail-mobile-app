import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/polling.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/models.dart';
import '../../data/phone_launcher.dart';
import '../../data/providers.dart';
import '../cart/cart_controller.dart';
import '../notifications/notifications_controller.dart';
import 'orders_controller.dart';
import 'orders_screen.dart';

class OrderSuccessScreen extends ConsumerWidget {
  const OrderSuccessScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final order = ref.watch(lastPlacedOrderProvider);
    final mine = order != null && order.id == id ? order : null;
    final number = mine?.number ?? id;
    return AppScaffold(
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
              // The estimate the customer was given for this order, not whatever the store's estimate is now.
              final eta = mine?.estimatedMinutes ?? ref.watch(selectedStoreProvider).value?.estimatedMinutes;
              return Text(
                eta == null ? context.tr('Order #{id} is being prepared.', {'id': number}) : context.tr('Order #{id} is being prepared.\nExpected in about {n} minutes.', {'id': number, 'n': '$eta'}),
                textAlign: TextAlign.center,
                style: const TextStyle(color: Pal.mutedOnGround, height: 1.4),
              );
            }),
            if (mine != null) ...[
              const SizedBox(height: 6),
              Text(context.tr('Pay {amt} in cash on delivery.', {'amt': rupees(mine.total)}), textAlign: TextAlign.center, style: const TextStyle(color: Pal.mutedOnGround, fontWeight: FontWeight.w800)),
            ],
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
}

class OrderDetailScreen extends ConsumerWidget {
  const OrderDetailScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(orderProvider(id));
    return async.when(
      loading: () => AppScaffold(appBar: appTopBar(context, '', backFallback: '/orders'), body: const Padding(padding: EdgeInsets.all(QC.gutter), child: SkeletonBox(height: 240, radius: 24))),
      error: (e, _) => AppScaffold(
        appBar: appTopBar(context, '', backFallback: '/orders'),
        body: ErrorState(error: e, onRetry: () => ref.invalidate(orderProvider(id))),
      ),
      data: (order) => _Detail(order),
    );
  }
}

class _Detail extends ConsumerStatefulWidget {
  const _Detail(this.order);
  final Order order;

  @override
  ConsumerState<_Detail> createState() => _DetailState();
}

class _DetailState extends ConsumerState<_Detail> {
  bool _cancelling = false;

  Order get order => widget.order;

  void _say(String message) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _call() async {
    final ok = await ref.read(phoneLauncherProvider).dial(order.storePhone!);
    if (!ok && mounted) _say(context.tr('Could not open the phone app.'));
  }

  Future<void> _cancel() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialog) => AlertDialog(
        title: Text(dialog.tr('Cancel this order?')),
        content: Text(dialog.tr('The store has not accepted it yet. If you cancel, nothing is charged and the items go back to the store.')),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialog).pop(false), child: Text(dialog.tr('Keep order'))),
          TextButton(onPressed: () => Navigator.of(dialog).pop(true), child: Text(dialog.tr('Cancel order'))),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _cancelling = true);
    final orders = ref.read(ordersProvider.notifier);
    try {
      await orders.cancel(order.id);
      if (mounted) _say(context.tr('Your order was cancelled.'));
      // The server wrote a notification about it: the bell should show it.
      ref.read(unreadCountProvider.notifier).poll().catchError((Object _) {});
    } on ConflictException catch (_) {
      // Too late: the store accepted it a moment ago. Show where it is now and say what to do.
      await orders.pollOne(order.id).catchError((Object _) {});
      if (mounted) _say(context.tr('The store has already accepted your order, so it can no longer be cancelled here. Please call the store.'));
    } on ApiException catch (e) {
      if (mounted) _say(context.tr(e.userMessageKey));
    } finally {
      if (mounted) setState(() => _cancelling = false);
    }
  }

  /// "Arriving in about N minutes" counted from when the order was placed; "any moment now" once that time has passed.
  String? _arrival(BuildContext context, DateTime now) {
    final minutes = order.estimatedMinutes;
    if (!order.stage.isActive || minutes == null) return null;
    final left = order.placedAt.add(Duration(minutes: minutes)).difference(now).inMinutes;
    return left <= 0 ? context.tr('Arriving any moment now') : context.tr('Arriving in about {n} minutes', {'n': '$left'});
  }

  Future<void> _reorder() async {
    final cart = ref.read(cartProvider.notifier);
    final store = ref.read(productStoreProvider);
    final catalog = ref.read(catalogRepositoryProvider);
    final storeId = (await ref.read(selectedStoreProvider.future))?.id;
    var skipped = 0;
    for (final l in order.lines) {
      var product = store.get(l.key) ?? store.get(l.productId);
      try {
        product ??= await catalog.product(l.productId, storeId: storeId);
      } catch (_) {
        product = null;
      }
      final pack = product?.packs.where((p) => p.id == l.variantId).firstOrNull;
      final inStock = pack?.inStock ?? product?.inStock ?? false;
      if (product == null || !inStock || (l.variantId != null && pack == null)) {
        skipped++;
        continue;
      }
      cart.add(cartKey(product, pack), l.quantity);
    }
    if (!mounted) return;
    if (skipped > 0) _say(context.tr('Some items are not available and were left out.'));
    context.push('/cart');
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final now = ref.watch(orderClockProvider)();
    final arrival = _arrival(context, now);
    final failing = ref.watch(ordersSyncFailingProvider);
    // While the order is still on its way it is checked every 15 seconds (never in the background); a finished order is not checked at all.
    return Polling(
      interval: const Duration(seconds: 15),
      active: order.stage.isActive && TickerMode.valuesOf(context).enabled,
      onTick: () => ref.read(ordersProvider.notifier).pollOne(order.id),
      child: AppScaffold(
        appBar: appTopBar(context, '${context.tr('Order')} #${order.number}', backFallback: '/orders'),
        body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
          if (arrival != null) ...[
            Surface(
              color: Pal.green,
              child: Row(children: [
                const Icon(Icons.bolt, color: Pal.ink),
                const SizedBox(width: 12),
                Expanded(child: Text(arrival, style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900))),
              ]),
            ),
            const SizedBox(height: 12),
          ],
          if (order.stage == OrderStage.cancelled || order.stage == OrderStage.rejected) ...[
            Surface(
              color: Pal.pink,
              child: Row(children: [
                Icon(order.stage == OrderStage.cancelled ? Icons.cancel_outlined : Icons.block, color: Colors.white),
                const SizedBox(width: 12),
                Expanded(child: Text(order.stage == OrderStage.cancelled ? context.tr('This order was cancelled.') : context.tr('The store declined this order.'), style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w900))),
              ]),
            ),
            const SizedBox(height: 12),
          ],
          Surface(child: _Timeline(order.stage)),
          if (failing && order.stage.isActive)
            Padding(padding: const EdgeInsets.only(top: 8), child: Text(context.tr('Could not refresh. Showing the last update.'), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 12, fontWeight: FontWeight.w700))),
          const SizedBox(height: 16),
          if (order.storeName != null) ...[
            Surface(
              child: Row(children: [
                Container(width: 44, height: 44, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.storefront_outlined, color: Pal.ink)),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(context.tr('Sold by'), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                    Text(order.storeName!, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
                  ]),
                ),
                if (order.storePhone != null) TextButton.icon(onPressed: _call, icon: const Icon(Icons.call_outlined), label: Text(context.tr('Call store'))),
              ]),
            ),
            const SizedBox(height: 16),
          ],
          Surface(
            child: Column(children: [
              for (final l in order.lines)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 6),
                  child: Row(children: [
                    OrderLineThumb(l),
                    const SizedBox(width: 12),
                    Expanded(child: Text('${l.name}\n${l.label.isEmpty ? '' : '${l.label} × '}${l.quantity}', style: TextStyle(color: p.onCard, height: 1.3, fontWeight: FontWeight.w600))),
                    Text(rupees(l.total), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
                  ]),
                ),
              Divider(color: p.border),
              BillRow(context.tr('Item total'), rupees(order.subtotal)),
              BillRow(context.tr('Delivery fee'), order.deliveryFee == 0 ? context.tr('FREE') : rupees(order.deliveryFee), accent: order.deliveryFee == 0),
              BillRow(context.tr('Handling fee'), rupees(order.handlingFee)),
              Divider(color: p.border),
              BillRow(context.tr('Payment'), context.tr(order.payment == 'CashOnDelivery' ? 'Cash on delivery' : order.payment)),
              BillRow(context.tr('Total'), rupees(order.total), bold: true),
            ]),
          ),
          const SizedBox(height: 16),
          Surface(
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Container(width: 44, height: 44, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.location_on_outlined, color: Pal.ink)),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(order.address, style: TextStyle(color: p.mutedOnCard)),
                  if (order.receiverName != null) ...[
                    const SizedBox(height: 6),
                    Text('${order.receiverName}${order.receiverPhone == null ? '' : ' · ${order.receiverPhone}'}', style: TextStyle(color: p.onCard, fontWeight: FontWeight.w700)),
                  ],
                ]),
              ),
            ]),
          ),
          const SizedBox(height: 16),
          // Only while the store has not accepted it.
          if (order.stage == OrderStage.placed) ...[
            SoftPillButton(_cancelling ? context.tr('Cancelling…') : context.tr('Cancel order'), height: 60, onPressed: () {
              if (!_cancelling) _cancel();
            }),
            const SizedBox(height: 12),
          ],
          PillButton(context.tr('Reorder'), height: 60, onPressed: _reorder),
        ]),
      ),
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
    // A declined or cancelled order stops after "placed".
    final ended = current == OrderStage.rejected || current == OrderStage.cancelled;
    final stages = ended ? [OrderStage.placed, current] : [OrderStage.placed, OrderStage.packed, OrderStage.onTheWay, OrderStage.delivered];
    final currentAt = stages.indexOf(current);
    return Column(children: [
      for (var i = 0; i < stages.length; i++)
        Builder(builder: (_) {
          final s = stages[i];
          final reached = i <= currentAt;
          final last = i == stages.length - 1;
          final bad = s == OrderStage.rejected || s == OrderStage.cancelled;
          final dot = bad ? Pal.pink : on;
          return IntrinsicHeight(
            child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              SizedBox(
                width: 28,
                child: Column(children: [
                  Container(
                    width: 24,
                    height: 24,
                    decoration: BoxDecoration(shape: BoxShape.circle, color: reached ? dot : Colors.transparent, border: Border.all(color: reached ? dot : p.mutedOnCard, width: 2)),
                    child: reached ? Icon(bad ? Icons.close : Icons.check, size: 15, color: bad ? Colors.white : (p.dark ? Pal.ink : Pal.yellow)) : null,
                  ),
                  if (!last) Expanded(child: Container(width: 3, color: i < currentAt ? on : p.border)),
                ]),
              ),
              const SizedBox(width: 14),
              Padding(
                padding: const EdgeInsets.only(bottom: 20),
                child: Text(stageLabel(context, s), style: TextStyle(fontWeight: i == currentAt ? FontWeight.w900 : FontWeight.w500, fontSize: i == currentAt ? 17 : 15, color: reached ? p.onCard : p.mutedOnCard)),
              ),
            ]),
          );
        }),
    ]);
  }
}
