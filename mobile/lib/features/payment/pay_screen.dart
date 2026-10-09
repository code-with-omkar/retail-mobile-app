import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/polling.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/models.dart';
import '../../data/payment_launcher.dart';
import '../../data/providers.dart';
import '../../data/repositories/payment_repository.dart';
import '../auth/auth_controller.dart';
import '../notifications/notifications_controller.dart';
import '../orders/orders_controller.dart';

/// What the last attempt on this screen ended with, so the customer knows what happened and what to do next.
enum _Note {
  /// The customer closed the payment screen without paying.
  dismissed,

  /// The payment did not go through (declined, timed out, no network).
  failed,

  /// The customer paid but the server has not been able to confirm it yet. It is checked again on its own; nothing is lost.
  confirming,

  /// Online payment is switched off or the provider cannot be reached right now.
  unavailable,
  error,
}

/// Pays for an online order: shows the amount and the time left, opens the provider's screen, tells the server the result and waits
/// until the order counts as paid. The order is held for a short while; after that it is cancelled and anything that was still paid is refunded.
class PayScreen extends ConsumerStatefulWidget {
  const PayScreen(this.orderId, {super.key, this.auto = false});
  final String orderId;

  /// Opens the provider's screen at once (straight after ordering) instead of waiting for a tap.
  final bool auto;

  @override
  ConsumerState<PayScreen> createState() => _PayScreenState();
}

class _PayScreenState extends ConsumerState<PayScreen> {
  bool _busy = false;
  bool _cancelling = false;
  bool _left = false;
  _Note? _note;
  String? _errorKey;
  Timer? _clock;

  @override
  void initState() {
    super.initState();
    _clock = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {});
    });
    if (widget.auto) Future.microtask(() => mounted ? _pay() : null);
  }

  @override
  void dispose() {
    _clock?.cancel();
    super.dispose();
  }

  void _say(String message) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _refresh() => ref.read(ordersProvider.notifier).pollOne(widget.orderId).catchError((Object _) {});

  Future<void> _pay() async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _note = null;
      _errorKey = null;
    });
    try {
      final session = await ref.read(paymentRepositoryProvider).start(widget.orderId);
      if (!mounted) return;
      final user = ref.read(authProvider).user;
      final outcome = await ref.read(paymentLauncherProvider).pay(session, name: user?.fullName, phone: user?.phoneNumber, email: user?.email);
      if (!mounted) return;
      switch (outcome) {
        case PaymentPaid(:final proof):
          await _confirm(proof);
        case PaymentDismissed():
          setState(() => _note = _Note.dismissed);
        case PaymentFailed():
          setState(() => _note = _Note.failed);
      }
    } on ConflictException catch (e) {
      await _explain(e);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _note = _Note.error;
          _errorKey = e.userMessageKey;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _note = _Note.error);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Tells the server the customer paid. If it cannot confirm just now, the money is not lost: the provider also tells the server
  /// itself, and this screen keeps checking the order until it is paid.
  Future<void> _confirm(PaymentProof proof) async {
    try {
      final order = await ref.read(paymentRepositoryProvider).confirm(widget.orderId, proof);
      if (mounted) ref.read(ordersProvider.notifier).replace(order);
    } on ConflictException catch (e) {
      if (e.reason == PaymentReasons.providerUnavailable || e.reason == PaymentReasons.notCaptured) {
        if (mounted) setState(() => _note = _Note.confirming);
      } else {
        await _explain(e);
      }
    } on ApiException catch (_) {
      if (mounted) setState(() => _note = _Note.confirming);
    }
  }

  Future<void> _explain(ConflictException e) async {
    switch (e.reason) {
      case PaymentReasons.holdExpired || PaymentReasons.alreadyPaid || PaymentReasons.notAwaitingPayment:
        // The order has moved on (paid, or cancelled when the time ran out): the screen shows where it is now.
        await _refresh();
      case PaymentReasons.unavailable || PaymentReasons.providerUnavailable:
        if (mounted) setState(() => _note = _Note.unavailable);
      default:
        if (mounted) setState(() => _note = _Note.error);
    }
  }

  Future<void> _cancel() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialog) => AlertDialog(
        title: Text(dialog.tr('Cancel this order?')),
        content: Text(dialog.tr('You have not paid yet, so nothing is charged. The items go back to the store.')),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialog).pop(false), child: Text(dialog.tr('Keep order'))),
          TextButton(onPressed: () => Navigator.of(dialog).pop(true), child: Text(dialog.tr('Cancel order'))),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _cancelling = true);
    try {
      await ref.read(ordersProvider.notifier).cancel(widget.orderId);
      ref.read(unreadCountProvider.notifier).poll().catchError((Object _) {});
      if (mounted) _say(context.tr('Your order was cancelled.'));
    } on ConflictException catch (_) {
      await _refresh();
    } on ApiException catch (e) {
      if (mounted) _say(context.tr(e.userMessageKey));
    } finally {
      if (mounted) setState(() => _cancelling = false);
    }
  }

  String _noteText(BuildContext context) => switch (_note!) {
        _Note.dismissed => context.tr('Payment was not completed. You can try again.'),
        _Note.failed => context.tr('Your payment did not go through. No money was taken; if it was, it will be returned. Please try again.'),
        _Note.confirming => context.tr('We received your payment and are confirming it. This can take a minute. Please do not pay again.'),
        _Note.unavailable => context.tr('Online payment is not available right now. Please try again in a moment.'),
        _Note.error => context.tr(_errorKey ?? 'Something went wrong. Please try again.'),
      };

  String _timeLeft(Duration left) {
    final minutes = left.inMinutes;
    final seconds = left.inSeconds % 60;
    return '${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(orderProvider(widget.orderId));
    return async.when(
      loading: () => AppScaffold(appBar: appTopBar(context, context.tr('Payment'), backFallback: '/orders'), body: const Padding(padding: EdgeInsets.all(QC.gutter), child: SkeletonBox(height: 240, radius: 24))),
      error: (e, _) => AppScaffold(appBar: appTopBar(context, context.tr('Payment'), backFallback: '/orders'), body: ErrorState(error: e, onRetry: () => ref.invalidate(orderProvider(widget.orderId)))),
      data: (order) => _body(context, order),
    );
  }

  Widget _body(BuildContext context, Order order) {
    final p = context.pal;
    // Paid: on to the order confirmation, once.
    if (order.paymentState == PaymentState.paid && order.stage != OrderStage.awaitingPayment && order.stage != OrderStage.cancelled && order.stage != OrderStage.rejected) {
      if (!_left) {
        _left = true;
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!mounted) return;
          ref.read(lastPlacedOrderProvider.notifier).set(order);
          ref.read(unreadCountProvider.notifier).poll().catchError((Object _) {});
          context.go('/order-success/${order.id}');
        });
      }
      return AppScaffold(appBar: appTopBar(context, context.tr('Payment'), backFallback: '/orders'), body: const Center(child: CircularProgressIndicator()));
    }

    final awaiting = order.stage == OrderStage.awaitingPayment;
    final left = order.paymentExpiresAt?.difference(ref.watch(orderClockProvider)()) ?? Duration.zero;
    final timeUp = awaiting && left <= Duration.zero;
    return Polling(
      interval: const Duration(seconds: 5),
      active: awaiting && TickerMode.valuesOf(context).enabled,
      onTick: () => ref.read(ordersProvider.notifier).pollOne(order.id),
      child: AppScaffold(
        appBar: appTopBar(context, context.tr('Payment'), backFallback: '/orders'),
        body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
          if (!awaiting) ...[
            Surface(
              color: Pal.pink,
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Icon(Icons.cancel_outlined, color: Colors.white),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    order.stage == OrderStage.cancelled
                        ? context.tr('This order was cancelled because it was not paid in time. If any money was taken, it will be returned to you.')
                        : context.tr('This order can no longer be paid.'),
                    style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800, height: 1.3),
                  ),
                ),
              ]),
            ),
            const SizedBox(height: 16),
            PillButton(context.tr('View your orders'), height: 60, onPressed: () => context.go('/orders')),
            const SizedBox(height: 12),
            SoftPillButton(context.tr('Continue shopping'), height: 60, onPressed: () => context.go('/')),
          ] else ...[
            Surface(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('${context.tr('Order')} #${order.number}', style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
                const SizedBox(height: 4),
                Text(rupees(order.total), style: TextStyle(color: p.onCard, fontSize: 34, fontWeight: FontWeight.w900)),
                const SizedBox(height: 4),
                Text(context.tr('Pay online with UPI, card or net banking.'), style: TextStyle(color: p.mutedOnCard)),
              ]),
            ),
            const SizedBox(height: 12),
            Surface(
              color: timeUp ? Pal.pink : Pal.green,
              child: Row(children: [
                Icon(timeUp ? Icons.timer_off_outlined : Icons.timer_outlined, color: timeUp ? Colors.white : Pal.ink),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    timeUp ? context.tr('Time is up. This order is being cancelled.') : context.tr('Your items are held for {time}', {'time': _timeLeft(left)}),
                    style: TextStyle(color: timeUp ? Colors.white : Pal.ink, fontWeight: FontWeight.w900),
                  ),
                ),
              ]),
            ),
            if (_note != null) ...[
              const SizedBox(height: 12),
              Surface(
                color: _note == _Note.confirming ? Pal.yellow : Pal.pink,
                child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Icon(_note == _Note.confirming ? Icons.hourglass_top : Icons.error_outline, color: _note == _Note.confirming ? Pal.ink : Colors.white),
                  const SizedBox(width: 12),
                  Expanded(child: Text(_noteText(context), style: TextStyle(color: _note == _Note.confirming ? Pal.ink : Colors.white, fontWeight: FontWeight.w800, height: 1.3))),
                ]),
              ),
            ],
            const SizedBox(height: 20),
            PillButton(
              _busy ? context.tr('Please wait…') : (_note == null || _note == _Note.confirming ? '${context.tr('Pay now')} · ${rupees(order.total)}' : '${context.tr('Try again')} · ${rupees(order.total)}'),
              arrow: !_busy,
              onPressed: _busy || timeUp || _note == _Note.confirming ? null : _pay,
            ),
            const SizedBox(height: 12),
            SoftPillButton(_cancelling ? context.tr('Cancelling…') : context.tr('Cancel order'), height: 60, onPressed: () {
              if (!_cancelling && !_busy && _note != _Note.confirming) _cancel();
            }),
          ],
        ]),
      ),
    );
  }
}
