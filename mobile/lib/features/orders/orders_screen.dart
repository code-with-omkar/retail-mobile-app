import 'package:lucide_icons_flutter/lucide_icons.dart';
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
import '../cart/cart_controller.dart';
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
  const m = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  final h = d.hour % 12 == 0 ? 12 : d.hour % 12;
  return '${d.day} ${m[d.month - 1]}, $h:${d.minute.toString().padLeft(2, '0')} ${d.hour >= 12 ? 'PM' : 'AM'}';
}

/// The photo of an ordered product when the app knows the product, otherwise a plain basket.
class OrderLineThumb extends ConsumerWidget {
  const OrderLineThumb(
    this.line, {
    super.key,
    this.size = 48,
    this.radius = 14,
  });
  final OrderLine line;
  final double size, radius;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final product =
        ref.watch(productStoreProvider).get(line.key) ??
        ref.watch(productStoreProvider).get(line.productId);
    if (product != null)
      return ProductThumb(product, size: size, radius: radius);
    final p = context.pal;
    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: Container(
        width: size,
        height: size,
        color: p.soft,
        child: Icon(LucideIcons.shoppingBasket, color: p.onSoft),
      ),
    );
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
      active:
          ref.watch(hasActiveOrderProvider) &&
          TickerMode.valuesOf(context).enabled,
      onTick: ref.read(ordersProvider.notifier).poll,
      child: SafeArea(
        bottom: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: EdgeInsets.fromLTRB(
                QC.gutter,
                20,
                QC.gutter,
                failing ? 6 : 16,
              ),
              child: Text(
                context.tr('Your orders'),
                style: QC.display(32, color: context.pal.onGround),
              ),
            ),
            if (failing)
              Padding(
                padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 10),
                child: Text(
                  context.tr('Could not refresh. Showing the last update.'),
                  style: TextStyle(
                    color: context.pal.mutedOnGround,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            Expanded(
              child: guest
                  ? EmptyState(
                      icon: LucideIcons.lock,
                      title: context.tr('Sign in to see your orders'),
                      message: context.tr(
                        'Your orders are saved to your account.',
                      ),
                      actionLabel: context.tr('Log in'),
                      onAction: () =>
                          context.push(withNext('/welcome', '/orders')),
                    )
                  : orders.when(
                      skipLoadingOnReload: true,
                      loading: () => ListView(
                        padding: const EdgeInsets.fromLTRB(
                          QC.gutter,
                          0,
                          QC.gutter,
                          24,
                        ),
                        children: [
                          for (var i = 0; i < 3; i++)
                            const Padding(
                              padding: EdgeInsets.only(bottom: 12),
                              child: SkeletonBox(height: 120, radius: 24),
                            ),
                        ],
                      ),
                      error: (e, _) => ErrorState(
                        error: e,
                        onRetry: () =>
                            ref.read(ordersProvider.notifier).refresh(),
                      ),
                      data: (list) => list.isEmpty
                          ? EmptyState(
                              icon: LucideIcons.receiptText,
                              title: context.tr('No orders yet'),
                              message: context.tr(
                                'Your orders will show up here.',
                              ),
                              actionLabel: context.tr('Start shopping'),
                              onAction: () => context.go('/'),
                            )
                          : RefreshIndicator(
                              onRefresh: () =>
                                  ref.read(ordersProvider.notifier).refresh(),
                              child: ListView.separated(
                                physics: const AlwaysScrollableScrollPhysics(),
                                padding: const EdgeInsets.fromLTRB(
                                  QC.gutter,
                                  0,
                                  QC.gutter,
                                  24,
                                ),
                                itemCount: list.length,
                                separatorBuilder: (_, _) =>
                                    const SizedBox(height: 12),
                                itemBuilder: (_, i) => _OrderCard(list[i]),
                              ),
                            ),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The colour of an order's state: blue while it is being got ready, yellow on the way, green delivered, red stopped.
({StatusTone tone, Color accent, Color tint, IconData icon}) _look(
  OrderStage stage,
) => switch (stage) {
  OrderStage.delivered => (
    tone: StatusTone.done,
    accent: const Color(0xFF14A44D),
    tint: const Color(0xFFE3F7EA),
    icon: LucideIcons.circleCheck,
  ),
  OrderStage.cancelled || OrderStage.rejected => (
    tone: StatusTone.stopped,
    accent: const Color(0xFFC42F1D),
    tint: const Color(0xFFFDECEA),
    icon: LucideIcons.circleX,
  ),
  OrderStage.onTheWay => (
    tone: StatusTone.moving,
    accent: const Color(0xFFF5A700),
    tint: const Color(0xFFFFF4D6),
    icon: LucideIcons.bike,
  ),
  OrderStage.packed => (
    tone: StatusTone.progress,
    accent: const Color(0xFF2455E6),
    tint: const Color(0xFFEAF0FF),
    icon: LucideIcons.package,
  ),
  _ => (
    tone: StatusTone.progress,
    accent: const Color(0xFF2455E6),
    tint: const Color(0xFFEAF0FF),
    icon: LucideIcons.clock,
  ),
};

/// Asks before a cart from another store is emptied, then puts the order's items in the cart and opens it. Used by the list and the order screen.
/// Whatever the store cannot supply now is listed on the cart screen.
Future<void> reorderOrder(BuildContext context, WidgetRef ref, Order order) async {
  final held = ref.read(serverCartProvider);
  if (held != null && held.storeId != order.storeId && held.lines.isNotEmpty) {
    final replace = await showDialog<bool>(
      context: context,
      builder: (dialog) => AlertDialog(
        title: Text(dialog.tr('Start a new cart?')),
        content: Text(dialog.tr('Your cart has items from another store. Ordering again from {store} empties it.', {'store': order.storeName ?? dialog.tr('this store')})),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialog).pop(false), child: Text(dialog.tr('Keep my cart'))),
          TextButton(onPressed: () => Navigator.of(dialog).pop(true), child: Text(dialog.tr('Order again'))),
        ],
      ),
    );
    if (replace != true || !context.mounted) return;
  }

  final added = await ref.read(cartProvider.notifier).addFromOrder(order);
  if (!context.mounted) return;
  if (!added && ref.read(cartNoticeProvider).isEmpty) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('None of these items can be ordered right now.'))));
  }
  context.push('/cart');
}

/// A thin dashed line, as between the parts of a printed bill.
class _DashedLine extends StatelessWidget {
  const _DashedLine(this.color);
  final Color color;

  @override
  Widget build(BuildContext context) => SizedBox(height: 1, width: double.infinity, child: CustomPaint(painter: _DashPainter(color)));
}

class _DashPainter extends CustomPainter {
  _DashPainter(this.color);
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 1;
    for (var x = 0.0; x < size.width; x += 8) {
      canvas.drawLine(Offset(x, 0), Offset(x + 4, 0), paint);
    }
  }

  @override
  bool shouldRepaint(_DashPainter old) => old.color != color;
}

/// The order list card, laid out like a delivery app's: the shop and the state at the top, the items and the date, then the total with one
/// action (Track while it is on its way, Reorder once it is over), and for a delivered order the shop's rating and what the customer gave.
class _OrderCard extends ConsumerStatefulWidget {
  const _OrderCard(this.order);
  final Order order;

  @override
  ConsumerState<_OrderCard> createState() => _OrderCardState();
}

class _OrderCardState extends ConsumerState<_OrderCard> {
  bool _busy = false;

  Future<void> _reorder() async {
    if (_busy) return;
    setState(() => _busy = true);
    await reorderOrder(context, ref, widget.order);
    if (mounted) setState(() => _busy = false);
  }

  String _items(BuildContext context) {
    final lines = widget.order.lines;
    final shown = lines.take(3).map((l) => '${l.quantity} × ${l.name}').join(', ');
    return lines.length > 3 ? '$shown, ${context.tr('+{n} more', {'n': '${lines.length - 3}'})}' : shown;
  }

  @override
  Widget build(BuildContext context) {
    final order = widget.order;
    final p = context.pal;
    final look = _look(order.stage);
    final delivered = order.stage == OrderStage.delivered;
    final live = order.stage.isActive;
    final shopRating = order.storeRating;
    final showRatingRow = delivered || shopRating != null;
    final line = p.dark ? p.border : const Color(0xFFD9D9E0);
    return Semantics(
      button: true,
      child: GestureDetector(
        onTap: () => context.push('/order/${order.id}'),
        child: Container(
          decoration: BoxDecoration(color: p.dark ? p.card : Colors.white, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: p.border)),
          padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(color: p.dark ? look.accent.withValues(alpha: .18) : look.tint, borderRadius: BorderRadius.circular(12)),
                    child: Icon(LucideIcons.store, size: 22, color: look.accent),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(child: Text(order.storeName ?? '#${order.number}', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 15))),
                            Icon(LucideIcons.chevronRight, size: 18, color: p.mutedOnCard),
                          ],
                        ),
                        const SizedBox(height: 2),
                        Row(
                          children: [
                            if (order.storeName != null) Text('#${order.number}', maxLines: 1, style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                            const Spacer(),
                            Icon(look.icon, size: 15, color: look.accent),
                            const SizedBox(width: 4),
                            Flexible(child: Text(stageLabel(context, order.stage), maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: look.accent, fontWeight: FontWeight.w800, fontSize: 12))),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              _DashedLine(line),
              const SizedBox(height: 12),
              Text(_items(context), maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontSize: 13, height: 1.35, fontWeight: FontWeight.w600)),
              const SizedBox(height: 4),
              Text(formatDate(order.placedAt), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
              const SizedBox(height: 12),
              _DashedLine(line),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(context.tr(order.itemCount == 1 ? '{n} item' : '{n} items', {'n': '${order.itemCount}'}), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                        Text(rupees(order.total), style: QC.display(20, color: p.onCard)),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  if (live)
                    _CardButton(key: const Key('track-order'), label: context.tr('Track order'), primary: true, onTap: () => context.push('/order/${order.id}'))
                  else
                    _CardButton(key: const Key('reorder-order'), label: context.tr(_busy ? 'Please wait…' : 'Reorder'), primary: false, onTap: _busy ? null : _reorder),
                ],
              ),
              if (showRatingRow) ...[
                const SizedBox(height: 10),
                // A Wrap, so on a narrow phone the stars the customer gave drop to their own line instead of overflowing.
                Wrap(
                  alignment: WrapAlignment.spaceBetween,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: 12,
                  runSpacing: 6,
                  children: [
                    if (shopRating != null)
                      Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Icon(Icons.star_rounded, size: 18, color: Color(0xFFF5A700)),
                          const SizedBox(width: 3),
                          Text(shopRating.toStringAsFixed(1), key: const Key('shop-rating'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 13)),
                          if (order.storeRatingCount != null) Text(' (${order.storeRatingCount})', style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                        ],
                      ),
                    if (delivered) _YourRating(order),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// A small pill button on an order card: yellow for the main action, outlined for the quieter one. At least 48 tall to tap.
class _CardButton extends StatelessWidget {
  const _CardButton({super.key, required this.label, required this.primary, required this.onTap});
  final String label;
  final bool primary;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Semantics(
      button: true,
      label: label,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 48),
          child: Center(
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 9),
              decoration: BoxDecoration(
                color: primary ? Pal.yellow : Colors.transparent,
                borderRadius: BorderRadius.circular(QC.rPill),
                border: primary ? null : Border.all(color: p.dark ? Colors.white54 : Pal.ink, width: 1.5),
              ),
              child: ExcludeSemantics(
                child: Text(label, style: TextStyle(color: primary ? Pal.ink : p.onCard, fontWeight: FontWeight.w800, fontSize: 13)),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// What this customer gave a delivered order (stars for the shop, and for the delivery when given), or a yellow button to rate it.
class _YourRating extends StatelessWidget {
  const _YourRating(this.order);
  final Order order;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final mine = order.myStoreStars;
    if (mine == null) {
      return Container(
        key: const Key('rate-order'),
        height: 30,
        padding: const EdgeInsets.symmetric(horizontal: 14),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: Pal.yellow,
          borderRadius: BorderRadius.circular(QC.rPill),
        ),
        child: Text(
          context.tr('Rate this order'),
          style: const TextStyle(
            color: Pal.ink,
            fontWeight: FontWeight.w800,
            fontSize: 12,
          ),
        ),
      );
    }
    Widget stars(int n) => Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 1; i <= 5; i++)
          Icon(
            i <= n ? Icons.star_rounded : Icons.star_outline_rounded,
            size: 15,
            color: i <= n ? const Color(0xFFF5A700) : p.mutedOnCard,
          ),
      ],
    );
    return Semantics(
      label: '${context.tr('You rated')} $mine',
      child: ExcludeSemantics(
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              context.tr('You rated'),
              style: TextStyle(color: p.mutedOnCard, fontSize: 11),
            ),
            const SizedBox(width: 6),
            Container(key: const Key('my-store-stars'), child: stars(mine)),
            if (order.myDeliveryStars != null) ...[
              const SizedBox(width: 8),
              Icon(LucideIcons.bike, size: 16, color: p.mutedOnCard),
              Container(
                key: const Key('my-delivery-stars'),
                child: stars(order.myDeliveryStars!),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
