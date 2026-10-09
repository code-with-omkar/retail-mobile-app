import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/repositories/payment_repository.dart';
import '../address/address_controller.dart';
import '../cart/cart_controller.dart';
import '../cart/cart_problems.dart';
import '../orders/orders_controller.dart';
import '../payment/payment_controller.dart';

/// A fresh name for one attempt to order. The server returns the same order for the same name, which is what makes a retry or a
/// double tap safe, so the screen keeps it until the attempt has a definite answer.
String newIdempotencyKey() {
  final random = Random.secure();
  return List.generate(16, (_) => random.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
}

/// Why the last attempt to order did not produce an order, and so what to tell the customer.
enum _Failure {
  /// The cart changed (price, availability or stock): the cart card shows what and how to fix it.
  cartChanged,
  cartEmpty,
  outsideArea,

  /// Online payment was chosen but is switched off or unreachable: cash on delivery still works.
  paymentsOff,

  /// No answer: the order may or may not exist.
  unconfirmed,
  other,
}

class CheckoutScreen extends ConsumerStatefulWidget {
  const CheckoutScreen({super.key});
  @override
  ConsumerState<CheckoutScreen> createState() => _CheckoutScreenState();
}

class _CheckoutScreenState extends ConsumerState<CheckoutScreen> {
  String? _key;
  bool _busy = false;
  bool _online = false;
  _Failure? _failure;

  @override
  void initState() {
    super.initState();
    Future.microtask(() => mounted ? ref.read(cartProvider.notifier).reload() : null);
  }

  Future<void> _place() async {
    if (_busy) return;
    final place = ref.read(deliveryPlaceProvider);
    // The server checks all of this again; here the customer is stopped before they try.
    if (ref.read(cartTotalsProvider).isEmpty || place == null || ref.read(deliveryServiceableProvider) == false) return;
    setState(() {
      _busy = true;
      _failure = null;
    });
    final cart = ref.read(cartProvider.notifier);
    try {
      await cart.settled();
      final storeId = ref.read(serverCartProvider)?.storeId ?? (await ref.read(selectedStoreProvider.future))?.id;
      if (storeId == null) throw const ConflictException('No store', statusCode: 409, reason: 'OutsideServiceArea');
      _key ??= newIdempotencyKey();
      final online = _online && (ref.read(paymentOptionsProvider).value?.online ?? false);
      final order = await ref.read(orderRepositoryProvider).place(storeId: storeId, idempotencyKey: _key!, addressId: place.addressId, place: place.isSaved ? null : place, online: online);
      _key = null;
      ref.read(lastPlacedOrderProvider.notifier).set(order);
      ref.read(ordersProvider.notifier).placed(order);
      cart.orderPlaced();
      // An online order is not placed until it is paid: on to the payment, with the provider's screen opening at once.
      if (mounted) context.go(order.stage == OrderStage.awaitingPayment ? '/pay/${order.id}?auto=1' : '/order-success/${order.id}');
    } on ConflictException catch (e) {
      // A definite "no": the next attempt is a new one.
      _key = null;
      await _explain(e);
    } on ApiException catch (e) {
      // Offline, timed out or a server error: the order may exist, so the same key is kept for the retry.
      if (e is UnauthorizedException || e is ValidationException || e is NotFoundException) {
        _key = null;
        if (mounted) setState(() => _failure = _Failure.other);
      } else if (mounted) {
        setState(() => _failure = _Failure.unconfirmed);
      }
    } catch (_) {
      if (mounted) setState(() => _failure = _Failure.other);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _explain(ConflictException e) async {
    switch (e.reason) {
      case CheckoutReasons.priceChanged || CheckoutReasons.productUnavailable || CheckoutReasons.inventoryConflict:
        // The server's cart now says what changed; the card shows it with the way to fix each line.
        await ref.read(cartProvider.notifier).reload();
        if (mounted) setState(() => _failure = _Failure.cartChanged);
      case CheckoutReasons.cartEmpty:
        await ref.read(ordersProvider.notifier).refresh();
        await ref.read(cartProvider.notifier).reload();
        if (mounted) setState(() => _failure = _Failure.cartEmpty);
      case ServiceabilityReasons.outsideServiceArea:
        if (mounted) setState(() => _failure = _Failure.outsideArea);
      case PaymentReasons.unavailable:
        if (mounted) {
          setState(() {
            _online = false;
            _failure = _Failure.paymentsOff;
          });
        }
      default:
        if (mounted) setState(() => _failure = _Failure.other);
    }
  }

  Widget _failureCard(BuildContext context) {
    final f = _failure;
    if (f == null) return const SizedBox.shrink();
    final (String message, String? action, VoidCallback? onAction) = switch (f) {
      _Failure.cartChanged => ('Your cart changed while we were placing the order. Review the changes below, then place your order again.', null, null),
      _Failure.cartEmpty => ('Your cart is empty. If you already placed this order, you will find it in Your orders.', 'View your orders', () => context.go('/orders')),
      _Failure.outsideArea => ('We do not deliver to this address yet. Choose another address to place your order.', 'Choose another address', () => context.push('/addresses')),
      _Failure.unconfirmed => ('We could not confirm your order. Check Your orders before trying again; if it is not there, tap Place order again.', 'View your orders', () => context.go('/orders')),
      _Failure.paymentsOff => ('Online payment is not available right now. You can pay cash on delivery instead.', null, null),
      _Failure.other => ('We could not place your order. Please try again.', null, null),
    };
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Surface(
        color: Pal.pink,
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Icon(Icons.error_outline, color: Colors.white),
            const SizedBox(width: 12),
            Expanded(child: Text(context.tr(message), style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800, height: 1.3))),
          ]),
          if (action != null) Padding(padding: const EdgeInsets.only(top: 10), child: SoftPillButton(context.tr(action), height: 44, onPressed: onAction ?? () {})),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final t = ref.watch(cartTotalsProvider);
    final place = ref.watch(deliveryPlaceProvider);
    final blocked = place == null || ref.watch(deliveryServiceableProvider) == false || t.hasProblems || _busy;
    final eta = ref.watch(selectedStoreProvider).value?.estimatedMinutes;
    final offersOnline = ref.watch(paymentOptionsProvider).value?.online ?? false;
    final online = _online && offersOnline;
    final p = context.pal;
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Checkout')),
      body: t.isEmpty && _failure == null
          ? EmptyState(icon: Icons.shopping_bag_outlined, title: context.tr('Nothing to check out'), message: context.tr('Your cart is empty.'), actionLabel: context.tr('Back to shop'), onAction: () => context.go('/'))
          : ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
              _failureCard(context),
              const CartProblemsCard(),
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
              if (place != null && ref.watch(deliveryServiceableProvider) == false) ...[
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
              _MethodTile(
                icon: Icons.payments_outlined,
                title: context.tr('Cash on delivery'),
                subtitle: context.tr('Pay with cash when your order arrives.'),
                selected: !online,
                onTap: () => setState(() => _online = false),
              ),
              if (offersOnline) ...[
                const SizedBox(height: 12),
                _MethodTile(
                  icon: Icons.account_balance_wallet_outlined,
                  title: context.tr('Pay online'),
                  subtitle: context.tr('UPI, card or net banking. Your items are held for {n} minutes while you pay.', {'n': '${ref.watch(paymentOptionsProvider).value?.holdMinutes ?? 15}'}),
                  selected: online,
                  onTap: () => setState(() => _online = true),
                ),
              ],
              const SizedBox(height: 24),
              Text(context.tr('Order summary'), style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
              const SizedBox(height: 12),
              Surface(
                child: Column(children: [
                  for (final l in t.lines) BillRow('${l.quantity} × ${productName(context, l.product)} (${l.unitLabel})', rupees(l.total)),
                  Divider(color: p.border),
                  if (t.feesKnown) ...[
                    BillRow(context.tr('Delivery fee'), t.delivery == 0 ? context.tr('FREE') : rupees(t.delivery), accent: t.delivery == 0),
                    BillRow(context.tr('Handling fee'), rupees(t.handling)),
                    Divider(color: p.border),
                  ],
                  BillRow(context.tr('To pay'), rupees(t.total), bold: true),
                ]),
              ),
            ]),
      bottom: t.isEmpty ? null : SafeArea(child: Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 12), child: PillButton(_busy ? context.tr('Placing your order…') : '${context.tr(online ? 'Pay online' : 'Place order')} · ${rupees(t.total)}', arrow: !_busy, onPressed: blocked ? null : _place))),
    );
  }
}

/// One way of paying, as a card the customer taps to choose.
class _MethodTile extends StatelessWidget {
  const _MethodTile({required this.icon, required this.title, required this.subtitle, required this.selected, required this.onTap});
  final IconData icon;
  final String title, subtitle;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Semantics(
      button: true,
      selected: selected,
      child: InkWell(
        borderRadius: BorderRadius.circular(QC.rCard),
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: selected ? Pal.yellow : p.border, width: 2.5)),
          child: Row(children: [
            Icon(icon, color: p.onCard),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(title, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
                const SizedBox(height: 2),
                Text(subtitle, style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
              ]),
            ),
            Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? (p.dark ? Pal.yellow : Pal.ink) : p.mutedOnCard),
          ]),
        ),
      ),
    );
  }
}
