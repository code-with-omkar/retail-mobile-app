import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../cart/cart_controller.dart';
import '../cart/cart_problems.dart';

/// Black pill navigation (Home, Categories, Orders, Profile) with the yellow cart pill beside it.
class AppShell extends ConsumerWidget {
  const AppShell(this.shell, {super.key});
  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final totals = ref.watch(cartTotalsProvider);
    final p = context.pal;
    final tabs = [
      (Icons.home_outlined, Icons.home_rounded, context.tr('Home')),
      (Icons.grid_view_rounded, Icons.grid_view_rounded, context.tr('Categories')),
      (Icons.receipt_long_outlined, Icons.receipt_long, context.tr('Orders')),
      (Icons.person_outline, Icons.person, context.tr('Profile')),
    ];
    return CartChoiceDialogHost(child: AppScaffold(
      body: shell,
      bottom: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 12),
          child: Row(children: [
            Expanded(
              flex: 5,
              child: Container(
                height: 72,
                padding: const EdgeInsets.all(6),
                decoration: BoxDecoration(color: p.nav, borderRadius: BorderRadius.circular(QC.rPill), border: p.dark ? Border.all(color: p.border) : null),
                child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                  for (var i = 0; i < tabs.length; i++)
                    Expanded(
                      child: Tooltip(
                        message: tabs[i].$3,
                        child: Semantics(
                          button: true,
                          selected: i == shell.currentIndex,
                          label: tabs[i].$3,
                          child: GestureDetector(
                            behavior: HitTestBehavior.opaque,
                            onTap: () => shell.goBranch(i, initialLocation: i == shell.currentIndex),
                            child: Center(
                              child: Container(
                                width: 52,
                                height: 60,
                                decoration: BoxDecoration(color: i == shell.currentIndex ? Pal.yellow : Colors.transparent, borderRadius: BorderRadius.circular(QC.rPill)),
                                child: Icon(i == shell.currentIndex ? tabs[i].$2 : tabs[i].$1, color: i == shell.currentIndex ? Pal.ink : Colors.white),
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                ]),
              ),
            ),
            const SizedBox(width: 12),
            _CartPill(totals),
          ]),
        ),
      ),
    ));
  }
}

class _CartPill extends StatelessWidget {
  const _CartPill(this.totals);
  final CartTotals totals;

  @override
  Widget build(BuildContext context) {
    final empty = totals.isEmpty;
    final bag = Container(width: 52, height: 52, decoration: const BoxDecoration(color: Pal.ink, shape: BoxShape.circle), child: const Icon(Icons.shopping_bag_outlined, color: Pal.yellow));
    return Tooltip(
      message: context.tr('Cart'),
      child: Semantics(
        button: true,
        label: context.tr('Cart'),
        child: GestureDetector(
          onTap: () => context.push('/cart'),
          child: Container(
            height: 72,
            padding: EdgeInsets.fromLTRB(empty ? 10 : 18, 0, 10, 0),
            decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(QC.rPill)),
            child: Row(mainAxisSize: MainAxisSize.min, children: [
              if (!empty) ...[
                Column(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(context.tr(totals.count == 1 ? '{n} item' : '{n} items', {'n': '${totals.count}'}), style: const TextStyle(color: Pal.ink, fontSize: 12, fontWeight: FontWeight.w800)),
                  Text(rupees(totals.subtotal), style: const TextStyle(color: Pal.ink, fontSize: 20, fontWeight: FontWeight.w900)),
                ]),
                const SizedBox(width: 12),
              ],
              bag,
            ]),
          ),
        ),
      ),
    );
  }
}
