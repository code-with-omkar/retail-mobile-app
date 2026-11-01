import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import 'coupon_controller.dart';

/// Where the customer types a coupon code, and sees it applied. Used in the cart.
class CouponBox extends ConsumerStatefulWidget {
  const CouponBox({super.key});

  @override
  ConsumerState<CouponBox> createState() => _CouponBoxState();
}

class _CouponBoxState extends ConsumerState<CouponBox> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _apply() {
    FocusScope.of(context).unfocus();
    ref.read(couponProvider.notifier).apply(_controller.text);
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final s = ref.watch(couponProvider);
    if (s.applied) {
      return Surface(
        child: Row(children: [
          const Icon(Icons.local_offer_outlined, color: Pal.green),
          const SizedBox(width: 10),
          Expanded(
            child: Text(context.tr('{code} applied', {'code': s.code ?? ''}), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 15)),
          ),
          TextButton(
            onPressed: () {
              _controller.clear();
              ref.read(couponProvider.notifier).clear();
            },
            child: Text(context.tr('Remove')),
          ),
        ]),
      );
    }
    final problem = s.error != null ? context.tr(s.error!) : s.preview?.message;
    return Surface(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Expanded(
            child: TextField(
              controller: _controller,
              textCapitalization: TextCapitalization.characters,
              maxLength: 20,
              textInputAction: TextInputAction.done,
              onSubmitted: (_) => _apply(),
              decoration: InputDecoration(labelText: context.tr('Have a coupon code?'), counterText: ''),
            ),
          ),
          const SizedBox(width: 10),
          SizedBox(
            height: QC.minTap,
            child: TextButton(onPressed: s.busy ? null : _apply, child: s.busy ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)) : Text(context.tr('Apply'))),
          ),
        ]),
        if (problem != null && problem.isNotEmpty) Padding(padding: const EdgeInsets.only(top: 6), child: Text(problem, style: const TextStyle(color: Pal.pink, fontWeight: FontWeight.w700, fontSize: 12))),
      ]),
    );
  }
}
