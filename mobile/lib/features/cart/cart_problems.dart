import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import 'cart_controller.dart';

/// Lines of the cart that checkout would refuse, each with the way to fix it: the price changed, the pack is gone, or the
/// store has fewer than asked for. Shown on the cart and on checkout, before the customer tries and after a refusal.
class CartProblemsCard extends ConsumerWidget {
  const CartProblemsCard({super.key});

  String _name(BuildContext context, CartLine l) => '${productName(context, l.product)} (${l.unitLabel})';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final t = ref.watch(cartTotalsProvider);
    if (!t.hasProblems) return const SizedBox.shrink();
    final cart = ref.read(cartProvider.notifier);
    Widget action(String label, VoidCallback onTap) => Padding(
          padding: const EdgeInsets.only(top: 8),
          child: SoftPillButton(context.tr(label), height: 44, onPressed: onTap),
        );
    Widget row(String text) => Padding(padding: const EdgeInsets.only(top: 6), child: Text(text, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700, height: 1.3)));
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Surface(
        color: Pal.pink,
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            const Icon(Icons.error_outline, color: Colors.white),
            const SizedBox(width: 10),
            Expanded(child: Text(context.tr('Some items need your attention'), style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w900, fontSize: 15))),
          ]),
          if (t.priceChanged.isNotEmpty) ...[
            for (final l in t.priceChanged) row(context.tr('{name}: price changed from {old} to {now}', {'name': _name(context, l), 'old': rupees(l.priceSnapshot!), 'now': rupees(l.currentPrice!)})),
            action('Update prices', cart.reprice),
          ],
          if (t.unavailable.isNotEmpty) ...[
            for (final l in t.unavailable) row(context.tr('{name} is no longer available', {'name': _name(context, l)})),
            action('Remove unavailable items', cart.removeUnavailable),
          ],
          if (t.notEnoughStock.isNotEmpty) ...[
            for (final l in t.notEnoughStock) row(context.tr('{name}: only {n} left', {'name': _name(context, l), 'n': '${l.available}'})),
            action('Use available quantity', cart.useAvailableQuantities),
          ],
        ]),
      ),
    );
  }
}

/// What the cart wants to tell the customer after a change did not go through, or after a merge left something out.
class CartNoticeCard extends ConsumerWidget {
  const CartNoticeCard({super.key});

  String _what(BuildContext context, CartNote n) {
    final name = n.name.isEmpty ? context.tr('An item') : '${n.name}${n.label.isEmpty ? '' : ' (${n.label})'}';
    return switch (n.kind) {
      CartNote.outOfStock => context.tr('{name} is out of stock', {'name': name}),
      CartNote.reduced => context.tr('Only {n} of {name} could be added', {'n': '${n.quantity}', 'name': name}),
      _ => context.tr('{name} is no longer available', {'name': name}),
    };
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notice = ref.watch(cartNoticeProvider);
    if (notice.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Surface(
        color: Pal.yellow,
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Icon(Icons.info_outline, color: Pal.ink),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              if (notice.problem != null) Text(context.tr(notice.problem!), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w800)),
              if (notice.notes.isNotEmpty) ...[
                Text(context.tr('Some items from your cart could not be added:'), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900)),
                for (final n in notice.notes) Text(_what(context, n), style: const TextStyle(color: Pal.ink)),
              ],
            ]),
          ),
          IconButton(tooltip: context.tr('Dismiss'), onPressed: ref.read(cartNoticeProvider.notifier).dismiss, icon: const Icon(Icons.close, color: Pal.ink)),
        ]),
      ),
    );
  }
}

/// Asks which cart to keep when signing in finds a different cart on the server (in another store).
class CartChoiceDialogHost extends ConsumerWidget {
  const CartChoiceDialogHost({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // A change that did not go through is said on whatever screen the customer is on: the cart may be empty again by now.
    ref.listen(cartNoticeProvider, (previous, next) {
      final messenger = ScaffoldMessenger.of(context);
      if (next.problem != null && next.problem != previous?.problem) {
        messenger.showSnackBar(SnackBar(content: Text(context.tr(next.problem!))));
      } else if (next.notes.length > (previous?.notes.length ?? 0)) {
        messenger.showSnackBar(SnackBar(content: Text(context.tr('Some items from your cart could not be added:')), action: SnackBarAction(label: context.tr('View cart'), onPressed: () => GoRouter.of(context).push('/cart'))));
      }
    });
    ref.listen(cartChoiceProvider, (previous, next) {
      if (next == null) return;
      showDialog<void>(
        context: context,
        barrierDismissible: false,
        builder: (dialog) => AlertDialog(
          title: Text(dialog.tr('You have two carts')),
          content: Text(dialog.tr('You have a saved cart from another store and a cart on this phone. A cart can only be from one store. Which one do you want to keep?')),
          actions: [
            TextButton(
              onPressed: () {
                Navigator.of(dialog).pop();
                ref.read(cartProvider.notifier).keepSavedCart();
              },
              child: Text(dialog.tr('Keep my saved cart')),
            ),
            TextButton(
              onPressed: () {
                Navigator.of(dialog).pop();
                ref.read(cartProvider.notifier).usePhoneCart();
              },
              child: Text(dialog.tr('Use the cart from this phone')),
            ),
          ],
        ),
      );
    });
    return child;
  }
}
