import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../address/address_controller.dart';
import '../cart/cart_controller.dart';

/// Makes [store] the store whose catalogue the app shows. One cart belongs to one store, so with items in the cart the
/// customer is asked first and the cart is emptied when they agree. Returns true when [store] is now the selected store.
Future<bool> chooseStore(BuildContext context, WidgetRef ref, NearestStore store) async {
  final current = ref.read(selectedStoreProvider).value;
  if (current?.id == store.id) return true;
  if (ref.read(cartProvider).isNotEmpty) {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(context.tr('Switch to {store}?', {'store': store.name})),
        content: Text(context.tr('Your cart has items from {store}. Switching store will empty your cart.', {'store': current?.name ?? context.tr('your current store')})),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(false), child: Text(context.tr('Keep shopping'))),
          TextButton(onPressed: () => Navigator.of(context).pop(true), child: Text(context.tr('Switch store'))),
        ],
      ),
    );
    if (confirmed != true) return false;
    ref.read(cartProvider.notifier).clear();
  }
  ref.read(selectedStoreIdProvider.notifier).select(store.id);
  return true;
}

enum StoreSort { nearest, fastest, name }

/// The stores in the order the customer asked for. Nearest is the order the API returns; ties keep that order.
List<NearestStore> sortStores(List<NearestStore> stores, StoreSort sort) {
  final copy = [...stores];
  switch (sort) {
    case StoreSort.nearest:
      copy.sort((a, b) => a.distanceKm.compareTo(b.distanceKm));
    case StoreSort.fastest:
      copy.sort((a, b) {
        final byTime = a.estimatedMinutes.compareTo(b.estimatedMinutes);
        return byTime != 0 ? byTime : a.distanceKm.compareTo(b.distanceKm);
      });
    case StoreSort.name:
      copy.sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  }
  return copy;
}

/// Lists every store that delivers to the delivery address and lets the customer pick one. Picking marks a store;
/// the button at the bottom makes the switch.
class StoresScreen extends ConsumerStatefulWidget {
  const StoresScreen({super.key});

  @override
  ConsumerState<StoresScreen> createState() => _StoresScreenState();
}

class _StoresScreenState extends ConsumerState<StoresScreen> {
  StoreSort _sort = StoreSort.nearest;
  String? _pending;

  @override
  Widget build(BuildContext context) {
    final stores = ref.watch(serviceableStoresProvider);
    final selected = ref.watch(selectedStoreProvider).value;
    final place = ref.watch(deliveryPlaceProvider);
    final area = place == null ? '' : '${context.tr(place.label)} · ${place.area}';
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Choose a store')),
      body: stores.when(
        loading: () => ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 24), children: [for (var i = 0; i < 3; i++) const Padding(padding: EdgeInsets.only(bottom: 12), child: SkeletonBox(height: 150, radius: QC.rCard))]),
        error: (e, _) => ErrorState(error: e, onRetry: () => ref.invalidate(serviceableStoresProvider)),
        data: (raw) {
          if (raw.isEmpty) {
            return EmptyState(icon: Icons.location_off_outlined, title: context.tr('We do not deliver here yet'), message: context.tr('No store delivers to this address. Try another address.'));
          }
          final list = sortStores(raw, _sort);
          final nearestId = sortStores(raw, StoreSort.nearest).first.id;
          final pendingId = _pending ?? selected?.id;
          final target = raw.where((s) => s.id == pendingId).firstOrNull;
          return Column(children: [
            Expanded(
              child: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 16), children: [
                Padding(
                  padding: const EdgeInsets.only(bottom: 14),
                  child: Text(context.tr('{n} stores deliver to {address}. Pick one to see what it has in stock.', {'n': '${raw.length}', 'address': area}), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 15, height: 1.3)),
                ),
                Wrap(spacing: 8, runSpacing: 8, children: [
                  _SortChip(context.tr('Nearest'), Icons.place_outlined, _sort == StoreSort.nearest, () => setState(() => _sort = StoreSort.nearest)),
                  _SortChip(context.tr('Fastest'), Icons.bolt_outlined, _sort == StoreSort.fastest, () => setState(() => _sort = StoreSort.fastest)),
                  _SortChip(context.tr('A to Z'), null, _sort == StoreSort.name, () => setState(() => _sort = StoreSort.name)),
                ]),
                const SizedBox(height: 14),
                for (final s in list)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: _StoreCard(s, nearest: s.id == nearestId, current: s.id == selected?.id, pending: s.id == pendingId, onTap: () => setState(() => _pending = s.id)),
                  ),
              ]),
            ),
            if (target != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 20),
                child: PillButton(
                  context.tr('Shop from {store}', {'store': target.name}),
                  onPressed: () async {
                    final done = await chooseStore(context, ref, target);
                    if (done && context.mounted) goBack(context);
                  },
                ),
              ),
          ]);
        },
      ),
    );
  }

}

class _SortChip extends StatelessWidget {
  const _SortChip(this.label, this.icon, this.selected, this.onTap);
  final String label;
  final IconData? icon;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        selected: selected,
        label: label,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          child: Container(
            height: QC.minTap - 4,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            decoration: BoxDecoration(color: selected ? Pal.yellow : Colors.transparent, borderRadius: BorderRadius.circular(QC.rPill), border: Border.all(color: selected ? Pal.yellow : Colors.white54, width: 2)),
            child: Row(mainAxisSize: MainAxisSize.min, children: [
              if (icon != null) ...[Icon(icon, size: 16, color: selected ? Pal.ink : Colors.white), const SizedBox(width: 6)],
              Text(label, style: TextStyle(color: selected ? Pal.ink : Colors.white, fontSize: 14, fontWeight: selected ? FontWeight.w900 : FontWeight.w700)),
            ]),
          ),
        ),
      );
}

class _StoreCard extends StatelessWidget {
  const _StoreCard(this.store, {required this.nearest, required this.current, required this.pending, required this.onTap});
  final NearestStore store;
  final bool nearest, current, pending;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    // How far into its delivery range this address is; hidden when the store does not report a range.
    final range = store.serviceRadiusKm > 0 ? (store.distanceKm / store.serviceRadiusKm).clamp(0.0, 1.0) : null;
    return Semantics(
      button: true,
      selected: pending,
      label: store.name,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
          decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: pending ? Pal.yellow : Colors.transparent, width: 3)),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Container(width: 48, height: 48, decoration: BoxDecoration(color: pending ? Pal.yellow : (p.dark ? p.border : Colors.white), shape: BoxShape.circle), child: const Icon(Icons.storefront_outlined, color: Pal.ink)),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(store.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 17)),
                  if (store.address.isNotEmpty) Text(store.address, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
                ]),
              ),
              if (pending)
                Container(width: 26, height: 26, decoration: const BoxDecoration(color: Pal.ink, shape: BoxShape.circle), child: const Icon(Icons.check, size: 15, color: Pal.yellow))
              else
                Container(width: 26, height: 26, decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: p.mutedOnCard, width: 2))),
            ]),
            const SizedBox(height: 12),
            Wrap(spacing: 6, runSpacing: 6, children: [
              _Pill(context.tr('{km} km', {'km': store.distanceKm.toStringAsFixed(1)})),
              _Pill(context.tr('{n} min', {'n': '${store.estimatedMinutes}'})),
              if (nearest) _Pill(context.tr('Nearest'), color: Pal.green, bold: true),
              if (current) _Pill(context.tr('Selected'), color: Pal.ink, textColor: Pal.yellow, bold: true),
            ]),
            if (range != null) ...[
              const SizedBox(height: 12),
              ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(value: range.toDouble(), minHeight: 8, backgroundColor: p.dark ? p.border : Colors.white, valueColor: const AlwaysStoppedAnimation(Color(0xFF6A0FE8))),
              ),
              const SizedBox(height: 5),
              Text(context.tr('{km} km away · delivers within {r} km', {'km': store.distanceKm.toStringAsFixed(1), 'r': store.serviceRadiusKm.toStringAsFixed(0)}), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
            ],
          ]),
        ),
      ),
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill(this.text, {this.color, this.textColor, this.bold = false});
  final String text;
  final Color? color, textColor;
  final bool bold;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
        decoration: BoxDecoration(color: color ?? (context.pal.dark ? context.pal.border : Colors.white), borderRadius: BorderRadius.circular(QC.rPill)),
        child: Text(text, style: TextStyle(color: textColor ?? (color == null ? context.pal.onCard : Pal.ink), fontSize: 12, fontWeight: bold ? FontWeight.w900 : FontWeight.w700)),
      );
}
