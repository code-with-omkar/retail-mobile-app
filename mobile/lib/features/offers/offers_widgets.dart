import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/repositories/offers_repository.dart';
import '../stores/stores_screen.dart';
import 'offers_controller.dart';

/// The picture of an offer, or a plain coloured block when it has none or it cannot be loaded.
class OfferImage extends StatelessWidget {
  const OfferImage(this.url, {super.key, this.height = 150, this.radius = QC.rCard});
  final String? url;
  final double height, radius;

  @override
  Widget build(BuildContext context) {
    final fallback = Container(height: height, color: Pal.pink, alignment: Alignment.center, child: const Icon(Icons.local_offer, color: Colors.white, size: 40));
    return ClipRRect(
      borderRadius: BorderRadius.circular(radius),
      child: url == null
          ? fallback
          : Image.network(url!, height: height, width: double.infinity, fit: BoxFit.cover, errorBuilder: (_, _, _) => fallback),
    );
  }
}

/// The offers running at the store, as banners you swipe through. Hidden when there are none.
class OffersRail extends ConsumerWidget {
  const OffersRail({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final offers = ref.watch(activeOffersProvider).value ?? const <Offer>[];
    if (offers.isEmpty) return const SizedBox.shrink();
    final marathi = context.isMarathi;
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 8), child: SectionHeader(context.tr('Offers for you'))),
      SizedBox(
        height: 150,
        child: ListView.separated(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
          itemCount: offers.length,
          separatorBuilder: (_, _) => const SizedBox(width: 12),
          itemBuilder: (context, i) {
            final offer = offers[i];
            final width = (MediaQuery.sizeOf(context).width - 2 * QC.gutter) * (offers.length == 1 ? 1 : 0.86);
            return Semantics(
              button: true,
              label: offer.titleIn(marathi),
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => context.push('/offer/${offer.id}'),
                child: SizedBox(
                  width: width,
                  child: Stack(fit: StackFit.expand, children: [
                    OfferImage(offer.imageUrl),
                    Positioned.fill(
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(QC.rCard),
                          gradient: const LinearGradient(begin: Alignment.topCenter, end: Alignment.bottomCenter, colors: [Colors.transparent, Color(0xCC000000)]),
                        ),
                      ),
                    ),
                    Positioned(
                      left: 14,
                      right: 14,
                      bottom: 12,
                      child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
                        Expanded(child: Text(offer.titleIn(marathi), maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w900, height: 1.15))),
                        if (offer.discountPercent > 0)
                          Container(
                            margin: const EdgeInsets.only(left: 8),
                            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                            decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(QC.rPill)),
                            child: Text(context.tr('{n}% off', {'n': '${offer.discountPercent}'}), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 13)),
                          ),
                      ]),
                    ),
                  ]),
                ),
              ),
            );
          },
        ),
      ),
    ]);
  }
}

/// Up to four stores the customer bought from last. Tap one to shop there (the cart rule applies).
class YourStoresRail extends ConsumerWidget {
  const YourStoresRail({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final recent = ref.watch(recentStoresProvider).value ?? const <RecentStore>[];
    if (recent.isEmpty) return const SizedBox.shrink();
    final serviceable = ref.watch(serviceableStoresProvider).value ?? const <NearestStore>[];
    final selectedId = ref.watch(selectedStoreProvider).value?.id;
    final p = context.pal;
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 8), child: SectionHeader(context.tr('Your stores'))),
      SizedBox(
        height: 84,
        child: ListView.separated(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
          itemCount: recent.length,
          separatorBuilder: (_, _) => const SizedBox(width: 10),
          itemBuilder: (context, i) {
            final store = recent[i];
            final selected = store.id == selectedId;
            final delivers = serviceable.where((s) => s.id == store.id).firstOrNull;
            return Semantics(
              button: true,
              selected: selected,
              label: store.name,
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () async {
                  if (delivers == null) {
                    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('This store does not deliver to your address.'))));
                    return;
                  }
                  await chooseStore(context, ref, delivers);
                },
                child: Container(
                  width: 188,
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                  decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: selected ? Pal.yellow : Colors.transparent, width: 2)),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisAlignment: MainAxisAlignment.center, children: [
                    Row(children: [
                      Icon(Icons.storefront, size: 18, color: delivers == null ? p.mutedOnCard : Pal.pink),
                      const SizedBox(width: 6),
                      Expanded(child: Text(store.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 14))),
                      if (selected) const Icon(Icons.check_circle, size: 18, color: Pal.yellow),
                    ]),
                    const SizedBox(height: 4),
                    Text(
                      delivers == null ? context.tr('Not delivering to you') : '${delivers.distanceKm.toStringAsFixed(1)} km · ${context.tr('{n} min', {'n': '${delivers.estimatedMinutes}'})}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(color: p.mutedOnCard, fontSize: 12, fontWeight: FontWeight.w700),
                    ),
                  ]),
                ),
              ),
            );
          },
        ),
      ),
    ]);
  }
}

/// What the customer bought at this store before, to put back in the cart quickly. Hidden when there is nothing yet.
class BoughtBeforeRail extends ConsumerWidget {
  const BoughtBeforeRail({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final products = ref.watch(boughtBeforeProvider).value ?? const <Product>[];
    if (products.isEmpty) return const SizedBox.shrink();
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Padding(padding: const EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 8), child: SectionHeader(context.tr('Bought before'))),
      SizedBox(
        height: 168,
        child: ListView.separated(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
          itemCount: products.length,
          separatorBuilder: (_, _) => const SizedBox(width: 12),
          itemBuilder: (_, i) => SizedBox(width: 140, child: ProductCard(products[i])),
        ),
      ),
    ]);
  }
}

/// Shows an offer that asked to be shown when the app opens, once per run of the app. Called when the offers have loaded.
Future<void> showLoadOfferIfAny(BuildContext context, WidgetRef ref, List<Offer> offers) async {
  final shown = ref.read(shownLoadOffersProvider);
  final next = offers.where((o) => o.showOnLoad && !shown.contains(o.id)).firstOrNull;
  if (next == null) return;
  ref.read(shownLoadOffersProvider.notifier).mark(next.id);
  final marathi = context.isMarathi;
  final open = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.all(24),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Container(
          decoration: BoxDecoration(color: dialogContext.pal.card, borderRadius: BorderRadius.circular(QC.rCard)),
          clipBehavior: Clip.antiAlias,
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            OfferImage(next.imageUrl, height: 190, radius: 0),
            Padding(
              padding: const EdgeInsets.all(16),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                Text(next.titleIn(marathi), style: TextStyle(color: dialogContext.pal.onCard, fontSize: 20, fontWeight: FontWeight.w900)),
                if (next.discountPercent > 0) ...[
                  const SizedBox(height: 4),
                  Text(dialogContext.tr('{n}% off', {'n': '${next.discountPercent}'}), style: const TextStyle(color: Pal.pink, fontWeight: FontWeight.w900)),
                ],
                if (next.bodyIn(marathi) != null) ...[
                  const SizedBox(height: 6),
                  Text(next.bodyIn(marathi)!, style: TextStyle(color: dialogContext.pal.mutedOnCard)),
                ],
                const SizedBox(height: 14),
                PillButton(dialogContext.tr('See the offer'), onPressed: () => Navigator.of(dialogContext).pop(true)),
              ]),
            ),
          ]),
        ),
        const SizedBox(height: 8),
        TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: Text(dialogContext.tr('Close'), style: const TextStyle(color: Colors.white))),
      ]),
    ),
  );
  if (open == true && context.mounted) context.push('/offer/${next.id}');
}
