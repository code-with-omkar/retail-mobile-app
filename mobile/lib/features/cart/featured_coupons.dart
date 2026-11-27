import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/l10n.dart';
import '../../core/prefs.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/providers.dart';
import '../../data/repositories/coupon_repository.dart';
import '../auth/auth_controller.dart';
import '../offers/card_extras.dart';
import '../offers/festival_card.dart';
import '../offers/offers_widgets.dart';
import 'cart_controller.dart';
import 'coupon_controller.dart';

/// The coupons the business shows in the app, for the store being shopped in. A failure just means none are listed.
final featuredCouponsProvider = FutureProvider.autoDispose<List<FeaturedCoupon>>((ref) async {
  final storeId = ref.watch(selectedStoreProvider).value?.id;
  // Asked again when the customer signs in or out: the server leaves out what this customer can no longer use.
  ref.watch(authProvider.select((a) => a.status));
  if (storeId == null) return const [];
  try {
    return await ref.watch(couponRepositoryProvider).featured(storeId);
  } catch (_) {
    return const [];
  }
});

String couponHeadline(BuildContext context, FeaturedCoupon c) => switch (c.kind) {
      'FreeDelivery' => context.tr('Free delivery'),
      'Flat' => context.tr('{amount} off', {'amount': rupees(c.value)}),
      _ => context.tr('{n}% off', {'n': amount(c.value)}),
    };

String couponConditions(BuildContext context, FeaturedCoupon c) => [
      if (c.kind == 'Percent' && c.maxDiscount != null) context.tr('Up to {amount}', {'amount': rupees(c.maxDiscount!)}),
      if (c.minSubtotal > 0) context.tr('Minimum order {amount}', {'amount': rupees(c.minSubtotal)}),
      if (c.firstOrderOnly) context.tr('First order only'),
    ].join(' · ');

/// The coupon's picture, else its design's motif, else a tag.
class _CouponArt extends StatelessWidget {
  const _CouponArt(this.coupon, {required this.size});
  final FeaturedCoupon coupon;
  final double size;

  @override
  Widget build(BuildContext context) {
    final theme = festivalThemeOf(coupon.design);
    final fallback = theme != null
        ? CustomPaint(size: Size.square(size), painter: FestivalMotifPainter(theme))
        : Container(
            width: size,
            height: size,
            decoration: const BoxDecoration(color: Colors.white, shape: BoxShape.circle),
            child: Icon(LucideIcons.tag, size: size * .5, color: Pal.ink),
          );
    if (coupon.imageUrl == null) return fallback;
    return SizedBox(width: size, height: size, child: OfferImage(coupon.imageUrl, height: size, radius: 14));
  }
}

class _Colors {
  _Colors(BuildContext context, FeaturedCoupon c) : theme = festivalThemeOf(c.design), pal = context.pal;
  final FestivalTheme? theme;
  final Pal pal;
  Color get bg => theme?.bg ?? (pal.dark ? pal.card : const Color(0xFFFFF4D6));
  Color get ink => theme?.ink ?? (pal.dark ? pal.onCard : Pal.ink);
  Color get muted => theme?.muted ?? (pal.dark ? pal.mutedOnCard : const Color(0xFF5E5A66));
  Color get button => theme?.ctaBg ?? Pal.yellow;
  Color get buttonInk => theme?.ctaInk ?? Pal.ink;
}

/// One coupon as a themed card with an Apply button.
class FeaturedCouponCard extends StatelessWidget {
  const FeaturedCouponCard(this.coupon, {super.key, required this.applied, this.onApply});
  final FeaturedCoupon coupon;
  final bool applied;
  final VoidCallback? onApply;

  @override
  Widget build(BuildContext context) {
    final c = _Colors(context, coupon);
    final conditions = couponConditions(context, coupon);
    return Ribbon(
      coupon.ribbon,
      child: Container(
        padding: EdgeInsets.fromLTRB(14, coupon.ribbon == null ? 14 : 24, 14, 14),
        decoration: BoxDecoration(color: c.bg, borderRadius: BorderRadius.circular(QC.rCard)),
        child: Row(children: [
          _CouponArt(coupon, size: 52),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(couponHeadline(context, coupon), style: TextStyle(color: c.ink, fontSize: 18, fontWeight: FontWeight.w900)),
              Text(context.tr('Use code {code}', {'code': coupon.code}), style: TextStyle(color: c.ink, fontSize: 13, fontWeight: FontWeight.w800)),
              if (conditions.isNotEmpty) Text(conditions, style: TextStyle(color: c.muted, fontSize: 11, fontWeight: FontWeight.w600)),
            ]),
          ),
          const SizedBox(width: 8),
          TextButton(
            onPressed: applied ? null : onApply,
            style: TextButton.styleFrom(
              backgroundColor: c.button,
              foregroundColor: c.buttonInk,
              disabledBackgroundColor: c.button.withValues(alpha: .55),
              disabledForegroundColor: c.buttonInk,
              minimumSize: const Size(72, QC.minTap),
              shape: const StadiumBorder(),
            ),
            child: Text(applied ? context.tr('Applied') : context.tr('Apply'), style: const TextStyle(fontWeight: FontWeight.w900)),
          ),
        ]),
      ),
    );
  }
}

/// The coupons listed in the cart, side by side: swipe for more. Hidden when there are none.
class FeaturedCoupons extends ConsumerWidget {
  const FeaturedCoupons({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final coupons = ref.watch(featuredCouponsProvider).value ?? const <FeaturedCoupon>[];
    if (coupons.isEmpty) return const SizedBox.shrink();
    final state = ref.watch(couponProvider);
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      SectionHeader(context.tr('Coupons for you')),
      const SizedBox(height: 8),
      LayoutBuilder(builder: (context, box) {
        // One coupon fills the row; with more, the next one shows at the edge so it is clear there is more to swipe to.
        final width = coupons.length == 1 ? box.maxWidth : box.maxWidth * .86;
        return SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            for (final (i, coupon) in coupons.indexed)
              Padding(
                padding: EdgeInsets.only(left: i == 0 ? 0 : 10),
                child: SizedBox(
                  width: width,
                  child: FeaturedCouponCard(
                    coupon,
                    applied: state.applied && state.code == coupon.code,
                    onApply: state.busy ? null : () => ref.read(couponProvider.notifier).apply(coupon.code),
                  ),
                ),
              ),
          ]),
        );
      }),
      const SizedBox(height: 10),
    ]);
  }
}

/// Shows a coupon that asked to pop up, once per coupon for each customer on this phone. Called when the coupons have loaded; completes
/// when the pop-up is closed.
Future<void> showCouponPopupIfAny(BuildContext context, WidgetRef ref, List<FeaturedCoupon> coupons) async {
  final auth = ref.read(authProvider);
  final prefs = ref.read(sharedPrefsProvider);
  final key = 'coupon_popups_seen_${auth.user?.id ?? 'guest'}';
  final seen = prefs.getStringList(key) ?? const <String>[];
  final next = coupons.where((c) => c.showAsPopup && !seen.contains(c.id)).firstOrNull;
  if (next == null) return;
  await prefs.setStringList(key, [...seen, next.id]);
  if (!context.mounted) return;
  final hasItems = !ref.read(cartTotalsProvider).isEmpty;
  final take = await showDialog<bool>(
    context: context,
    builder: (dialogContext) {
      final c = _Colors(dialogContext, next);
      final conditions = couponConditions(dialogContext, next);
      return Dialog(
        backgroundColor: Colors.transparent,
        insetPadding: const EdgeInsets.all(28),
        child: Ribbon(
          next.ribbon,
          child: Container(
            decoration: BoxDecoration(color: c.bg, borderRadius: BorderRadius.circular(QC.rCard)),
            child: Stack(children: [
              SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(20, 44, 20, 20),
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  Center(child: _CouponArt(next, size: 120)),
                  const SizedBox(height: 14),
                  Text(couponHeadline(dialogContext, next), textAlign: TextAlign.center, style: TextStyle(color: c.ink, fontSize: 28, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 4),
                  Text(dialogContext.tr('Use code {code}', {'code': next.code}), textAlign: TextAlign.center, style: TextStyle(color: c.ink, fontSize: 16, fontWeight: FontWeight.w800)),
                  if (conditions.isNotEmpty) ...[
                    const SizedBox(height: 4),
                    Text(conditions, textAlign: TextAlign.center, style: TextStyle(color: c.muted, fontSize: 12)),
                  ],
                  const SizedBox(height: 16),
                  TextButton(
                    onPressed: () => Navigator.of(dialogContext).pop(true),
                    style: TextButton.styleFrom(backgroundColor: c.button, foregroundColor: c.buttonInk, minimumSize: const Size.fromHeight(QC.minTap), shape: const StadiumBorder()),
                    child: Text(hasItems ? dialogContext.tr('Apply') : dialogContext.tr('Shop now'), style: const TextStyle(fontWeight: FontWeight.w900)),
                  ),
                ]),
              ),
              Positioned(
                top: 0,
                left: 0,
                child: IconButton(
                  tooltip: dialogContext.tr('Close'),
                  onPressed: () => Navigator.of(dialogContext).pop(false),
                  icon: Icon(LucideIcons.x, color: c.ink),
                ),
              ),
            ]),
          ),
        ),
      );
    },
  );
  if (take != true || !context.mounted) return;
  if (ref.read(cartTotalsProvider).isEmpty) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Use {code} in your cart.', {'code': next.code}))));
    return;
  }
  await ref.read(couponProvider.notifier).apply(next.code);
  if (!context.mounted) return;
  final state = ref.read(couponProvider);
  if (state.applied) playCelebration(context, state.preview?.animation);
  context.push('/cart');
}
