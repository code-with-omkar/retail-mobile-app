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
import 'offers_widgets.dart';

/// One offer: its picture, message, what it takes off and until when, and a way into the store.
class OfferScreen extends ConsumerWidget {
  const OfferScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final offer = ref.watch(offerProvider(id));
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Offer')),
      body: offer.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorState(error: e, onRetry: () => ref.invalidate(offerProvider(id))),
        data: (value) => value == null
            ? EmptyState(icon: Icons.local_offer_outlined, title: context.tr('This offer is no longer available.'), message: context.tr('Look at the offers on the home screen.'), actionLabel: context.tr('Go to home'), onAction: () => context.go('/'))
            : _Body(value),
      ),
    );
  }
}

class _Body extends ConsumerWidget {
  const _Body(this.offer);
  final Offer offer;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final marathi = context.isMarathi;
    final body = offer.bodyIn(marathi);
    final ends = offer.endsAt;
    final serviceable = ref.watch(serviceableStoresProvider).value ?? const <NearestStore>[];
    final selected = ref.watch(selectedStoreProvider).value;
    // The stores of this offer that deliver here; an offer naming no store is for every store.
    final here = offer.stores.isEmpty ? serviceable : serviceable.where((s) => offer.stores.any((o) => o.id == s.id)).toList();
    final selectedHasIt = selected != null && (offer.stores.isEmpty || offer.stores.any((s) => s.id == selected.id));
    return ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 24), children: [
      OfferImage(offer.imageUrl, height: 190),
      const SizedBox(height: 14),
      Text(offer.titleIn(marathi), style: const TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.w900, height: 1.15)),
      const SizedBox(height: 8),
      Wrap(spacing: 8, runSpacing: 6, children: [
        if (offer.discountPercent > 0) _Chip(context.tr('{n}% off', {'n': '${offer.discountPercent}'}), Pal.yellow, Pal.ink),
        if (offer.discountPercent > 0 && ends != null)
          _Chip(offer.running ? context.tr('Ends {date}', {'date': _date(ends)}) : context.tr('Offer has ended'), offer.running ? Pal.green : Colors.white24, offer.running ? Pal.ink : Colors.white),
      ]),
      if (body != null) ...[
        const SizedBox(height: 12),
        Surface(child: Text(body, style: TextStyle(color: p.onCard, fontSize: 15, height: 1.35))),
      ],
      if (offer.stores.isNotEmpty) ...[
        const SizedBox(height: 14),
        Text('${context.tr('From')} ${offer.stores.map((s) => s.name).join(', ')}', style: const TextStyle(color: Pal.mutedOnGround, fontWeight: FontWeight.w700)),
      ],
      if (offer.productIds.isNotEmpty && selectedHasIt) ...[
        const SizedBox(height: 18),
        Text(context.tr('Products in this offer'), style: const TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w900)),
        const SizedBox(height: 8),
        _Products(offer.productIds),
      ],
      const SizedBox(height: 18),
      if (selectedHasIt)
        PillButton(context.tr('Shop at {store}', {'store': selected.name}), onPressed: () => context.go('/'))
      else if (here.isEmpty)
        Text(context.tr('This offer is not for a store that delivers to you.'), style: const TextStyle(color: Pal.mutedOnGround))
      else
        for (final store in here.take(4)) ...[
          PillButton(context.tr('Shop at {store}', {'store': store.name}), onPressed: () async {
            if (await chooseStore(context, ref, store) && context.mounted) context.go('/');
          }),
          const SizedBox(height: 8),
        ],
    ]);
  }

  static String _date(DateTime at) {
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return '${at.day} ${months[at.month - 1]}';
  }
}

class _Chip extends StatelessWidget {
  const _Chip(this.label, this.background, this.foreground);
  final String label;
  final Color background, foreground;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(QC.rPill)),
        child: Text(label, style: TextStyle(color: foreground, fontWeight: FontWeight.w900, fontSize: 13)),
      );
}

/// The offer's products with the prices of the store being shopped in (the discount is already in them).
class _Products extends ConsumerWidget {
  const _Products(this.ids);
  final List<String> ids;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final products = ref.watch(offerProductsProvider(ids.take(12).join(','))).value ?? const <Product>[];
    if (products.isEmpty) return const SizedBox.shrink();
    return SizedBox(
      height: 168,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: products.length,
        separatorBuilder: (_, _) => const SizedBox(width: 12),
        itemBuilder: (_, i) => SizedBox(width: 140, child: ProductCard(products[i])),
      ),
    );
  }
}
