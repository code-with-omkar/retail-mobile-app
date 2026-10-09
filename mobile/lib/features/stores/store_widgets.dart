import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../address/address_controller.dart';
import '../address/delivery_actions.dart';
import '../cart/cart_controller.dart';
import 'stores_screen.dart';

/// "{km} km away", with one decimal.
String kmAway(BuildContext context, NearestStore s) => context.tr('{km} km away', {'km': s.distanceKm.toStringAsFixed(1)});

/// The store the customer is shopping from, always visible at the top of Home: name, distance, delivery time and a
/// Change button that opens the switch sheet. Also covers loading, a failed lookup and "nobody delivers here".
class StoreBar extends ConsumerWidget {
  const StoreBar({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // No place yet: ask where to deliver before anything else.
    final delivery = ref.watch(deliveryStateProvider);
    if (delivery is DeliveryNone) return const _ChooseLocationCard();
    if (delivery is DeliveryLoading) return const SkeletonBox(height: 104, radius: QC.rCard);
    final store = ref.watch(selectedStoreProvider);
    return store.when(
      loading: () => const SkeletonBox(height: 104, radius: QC.rCard),
      error: (_, _) => _Frame(
        onTap: () => ref.invalidate(serviceableStoresProvider),
        child: Row(children: [
          const Icon(Icons.cloud_off_outlined, color: Pal.ink),
          const SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(context.tr('Could not load'), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 17)),
            Text(context.tr('Retry'), style: const TextStyle(color: Pal.ink, fontSize: 13, decoration: TextDecoration.underline)),
          ])),
        ]),
      ),
      data: (s) => s == null
          ? _Frame(
              label: context.tr('Try another address'),
              onTap: () => context.push('/addresses'),
              child: Row(children: [
                const Icon(Icons.location_off_outlined, color: Pal.ink),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(context.tr('We do not deliver here yet'), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 17, height: 1.15)),
                    Text(context.tr('Try another address'), style: const TextStyle(color: Pal.ink, fontSize: 13, decoration: TextDecoration.underline)),
                  ]),
                ),
              ]),
            )
          : _Frame(
              label: context.tr('Change store'),
              onTap: () => showStoreSheet(context),
              child: LayoutBuilder(builder: (context, box) {
                // On a narrow phone the Change button shrinks to an icon so the store name keeps its room.
                final narrow = box.maxWidth < 300;
                return Row(children: [
                  const _StoreIcon(52),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                      Text(context.tr('SHOPPING FROM'), style: const TextStyle(color: Pal.ink, fontSize: 11, fontWeight: FontWeight.w900, letterSpacing: 1.4)),
                      Text(s.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.ink, fontSize: 18, fontWeight: FontWeight.w900, height: 1.2)),
                      Text('${kmAway(context, s)} · ${context.tr('arrives in {n} min', {'n': '${s.estimatedMinutes}'})}', maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.ink, fontSize: 13, fontWeight: FontWeight.w500)),
                    ]),
                  ),
                  const SizedBox(width: 8),
                  _ChangePill(narrow: narrow),
                ]);
              }),
            ),
    );
  }
}

/// A slimmer version of [StoreBar] for browsing screens, so the store stays visible while scrolling through its items.
/// [itemCount] is how many items the store carries in what is shown, when known.
class CompactStoreBar extends ConsumerWidget {
  const CompactStoreBar({super.key, this.itemCount, this.flush = false});
  final int? itemCount;

  /// No side padding, for a list that already has the screen gutter.
  final bool flush;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = ref.watch(selectedStoreProvider).value;
    if (s == null) return const SizedBox.shrink();
    final detail = itemCount == null
        ? context.tr('{n} min', {'n': '${s.estimatedMinutes}'})
        : '${context.tr('{n} min', {'n': '${s.estimatedMinutes}'})} · ${context.tr('{n} items', {'n': '$itemCount'})}';
    return Padding(
      padding: flush ? EdgeInsets.zero : const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 12),
      child: _Frame(
        label: context.tr('Change store'),
        radius: QC.rPill,
        padding: const EdgeInsets.all(8),
        onTap: () => showStoreSheet(context),
        child: LayoutBuilder(builder: (context, box) {
          final narrow = box.maxWidth < 260;
          return Row(children: [
            const _StoreIcon(40),
            const SizedBox(width: 10),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                Text(s.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.ink, fontSize: 15, fontWeight: FontWeight.w900)),
                Text(detail, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.ink, fontSize: 12, fontWeight: FontWeight.w500)),
              ]),
            ),
            const SizedBox(width: 8),
            _ChangePill(narrow: narrow, compact: true),
          ]);
        }),
      ),
    );
  }
}

/// First run, or after the place was cleared: where should we deliver?
class _ChooseLocationCard extends ConsumerWidget {
  const _ChooseLocationCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) => Container(
        width: double.infinity,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(QC.rCard)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            const _StoreIcon(44),
            const SizedBox(width: 12),
            Expanded(child: Text(context.tr('Where should we deliver?'), style: const TextStyle(color: Pal.ink, fontSize: 18, fontWeight: FontWeight.w900, height: 1.2))),
          ]),
          const SizedBox(height: 8),
          Text(context.tr('Add your location to see the stores that deliver to you.'), style: const TextStyle(color: Pal.ink, fontSize: 13, height: 1.3)),
          const SizedBox(height: 12),
          _InkButton(context.tr('Use my current location'), Icons.my_location, () => useCurrentLocation(context, ref)),
          const SizedBox(height: 8),
          _InkButton(context.tr('Enter an address'), Icons.edit_location_alt_outlined, () => context.push('/addresses/new')),
        ]),
      );
}

class _InkButton extends StatelessWidget {
  const _InkButton(this.label, this.icon, this.onTap);
  final String label;
  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: label,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          child: Container(
            height: QC.minTap,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            decoration: BoxDecoration(color: Pal.ink, borderRadius: BorderRadius.circular(QC.rPill)),
            child: Row(children: [
              Icon(icon, color: Pal.yellow, size: 20),
              const SizedBox(width: 10),
              Expanded(child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800, fontSize: 14))),
            ]),
          ),
        ),
      );
}

class _Frame extends StatelessWidget {
  const _Frame({required this.child, required this.onTap, this.label, this.radius = QC.rCard, this.padding = const EdgeInsets.all(16)});
  final Widget child;
  final VoidCallback onTap;
  final String? label;
  final double radius;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: label,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          child: Container(
            width: double.infinity,
            padding: padding,
            decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(radius)),
            child: child,
          ),
        ),
      );
}

class _StoreIcon extends StatelessWidget {
  const _StoreIcon(this.size);
  final double size;

  @override
  Widget build(BuildContext context) => Container(width: size, height: size, decoration: const BoxDecoration(color: Pal.ink, shape: BoxShape.circle), child: Icon(Icons.storefront_outlined, color: Pal.yellow, size: size * .5));
}

class _ChangePill extends StatelessWidget {
  const _ChangePill({required this.narrow, this.compact = false});
  final bool narrow, compact;

  @override
  Widget build(BuildContext context) => Container(
        height: compact ? 40 : 40,
        padding: EdgeInsets.symmetric(horizontal: narrow ? 10 : 14),
        decoration: BoxDecoration(color: Pal.ink, borderRadius: BorderRadius.circular(QC.rPill)),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          if (!narrow) Text(context.tr('Change'), style: const TextStyle(color: Colors.white, fontSize: 14, fontWeight: FontWeight.w700)),
          Icon(narrow ? Icons.swap_horiz_rounded : Icons.expand_more_rounded, color: Colors.white, size: 18),
        ]),
      );
}

/// The stores that deliver here, as a row of cards: tap one to switch (the cart rule applies), "See all" opens the full
/// list. Hidden when there is nothing to choose between.
class StoresRail extends ConsumerWidget {
  const StoresRail({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final stores = ref.watch(serviceableStoresProvider).value ?? const <NearestStore>[];
    if (stores.length < 2) return const SizedBox.shrink();
    final selectedId = ref.watch(selectedStoreProvider).value?.id;
    final p = context.pal;
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(QC.gutter, 18, QC.gutter, 8),
        child: SectionHeader(context.tr('Stores that deliver to you'), action: context.tr('See all {n}', {'n': '${stores.length}'}), onAction: () => context.push('/stores')),
      ),
      SizedBox(
        height: 84,
        child: ListView.separated(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
          itemCount: stores.length,
          separatorBuilder: (_, _) => const SizedBox(width: 12),
          itemBuilder: (context, i) {
            final store = stores[i];
            final selected = store.id == selectedId;
            return Semantics(
              button: true,
              selected: selected,
              label: store.name,
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => chooseStore(context, ref, store),
                child: Container(
                  width: 214,
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                  decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: selected ? Pal.yellow : Colors.transparent, width: 3)),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisAlignment: MainAxisAlignment.center, children: [
                    Row(children: [
                      Expanded(child: Text(store.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 15))),
                      if (selected) const Icon(Icons.check_circle, size: 18, color: Pal.yellow),
                    ]),
                    const SizedBox(height: 4),
                    Row(children: [
                      Flexible(child: Text('${store.distanceKm.toStringAsFixed(1)} km · ${context.tr('{n} min', {'n': '${store.estimatedMinutes}'})}', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 12, fontWeight: FontWeight.w700))),
                      if (i == 0) ...[const SizedBox(width: 6), const _Tag()],
                    ]),
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

class _Tag extends StatelessWidget {
  const _Tag();

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
        decoration: BoxDecoration(color: Pal.green, borderRadius: BorderRadius.circular(QC.rPill)),
        child: Text(context.tr('Nearest'), style: const TextStyle(color: Pal.ink, fontSize: 11, fontWeight: FontWeight.w900)),
      );
}

/// Opens the switch sheet: every store that delivers here, the cart warning when it applies, and one clear button that
/// says exactly what will happen.
Future<void> showStoreSheet(BuildContext context) => showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => const _StoreSheet(),
    );

class _StoreSheet extends ConsumerStatefulWidget {
  const _StoreSheet();

  @override
  ConsumerState<_StoreSheet> createState() => _StoreSheetState();
}

class _StoreSheetState extends ConsumerState<_StoreSheet> {
  String? _pending;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final stores = ref.watch(serviceableStoresProvider).value ?? const <NearestStore>[];
    final current = ref.watch(selectedStoreProvider).value;
    final cartCount = ref.watch(cartProvider).values.fold<int>(0, (sum, qty) => sum + qty);
    final pendingId = _pending ?? current?.id;
    final target = stores.where((s) => s.id == pendingId).firstOrNull;
    final switching = target != null && target.id != current?.id;
    final place = ref.watch(deliveryPlaceProvider);
    final area = place == null ? '' : '${context.tr(place.label)} · ${place.area}';
    final bg = p.dark ? p.card : const Color(0xFFF4F1FF);
    final maxHeight = MediaQuery.of(context).size.height * .85;
    return ConstrainedBox(
      constraints: BoxConstraints(maxHeight: maxHeight),
      child: Container(
        decoration: BoxDecoration(color: bg, borderRadius: const BorderRadius.vertical(top: Radius.circular(32))),
        padding: EdgeInsets.fromLTRB(20, 12, 20, 16 + MediaQuery.of(context).padding.bottom),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Center(child: Container(width: 44, height: 5, decoration: BoxDecoration(color: p.mutedOnCard.withValues(alpha: .5), borderRadius: BorderRadius.circular(3)))),
          const SizedBox(height: 14),
          Row(children: [
            Expanded(child: Text(context.tr('Choose your store'), style: TextStyle(color: p.onCard, fontSize: 24, fontWeight: FontWeight.w900))),
            Semantics(
              button: true,
              label: context.tr('Close'),
              child: GestureDetector(onTap: () => Navigator.of(context).pop(), child: SizedBox(width: QC.minTap, height: QC.minTap, child: Icon(Icons.close, color: p.onCard))),
            ),
          ]),
          Text(context.tr('{n} stores deliver to {address}', {'n': '${stores.length}', 'address': area}), style: TextStyle(color: p.mutedOnCard, fontSize: 14)),
          if (switching && cartCount > 0)
            Container(
              margin: const EdgeInsets.only(top: 12),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(color: const Color(0xFFFFF3B0), borderRadius: BorderRadius.circular(18)),
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Icon(Icons.shopping_bag_outlined, color: Pal.ink, size: 22),
                const SizedBox(width: 10),
                Expanded(child: Text(context.tr('You have {n} items from {store}. Switching store will empty your cart.', {'n': '$cartCount', 'store': current?.name ?? context.tr('your current store')}), style: const TextStyle(color: Pal.ink, fontSize: 14, height: 1.3, fontWeight: FontWeight.w500))),
              ]),
            ),
          const SizedBox(height: 12),
          Flexible(
            child: ListView(shrinkWrap: true, children: [
              for (final s in stores)
                Padding(
                  padding: const EdgeInsets.only(bottom: 10),
                  child: _SheetRow(s, current: s.id == current?.id, pending: s.id == pendingId, onTap: () => setState(() => _pending = s.id)),
                ),
            ]),
          ),
          const SizedBox(height: 4),
          Center(child: TextButton(onPressed: () { Navigator.of(context).pop(); context.push('/stores'); }, child: Text(context.tr('See addresses and delivery range')))),
          if (switching)
            PillButton(
              cartCount > 0 ? context.tr('Switch to {store} and empty cart', {'store': target.name}) : context.tr('Switch to {store}', {'store': target.name}),
              onPressed: () async {
                // The warning is already on screen, so the choice is made here without a second dialog.
                if (cartCount > 0) ref.read(cartProvider.notifier).clear();
                ref.read(selectedStoreIdProvider.notifier).select(target.id);
                Navigator.of(context).pop();
              },
            ),
          SoftPillButton(context.tr('Keep shopping at {store}', {'store': current?.name ?? context.tr('your current store')}), height: 52, onPressed: () => Navigator.of(context).pop()),
        ]),
      ),
    );
  }
}

class _SheetRow extends StatelessWidget {
  const _SheetRow(this.store, {required this.current, required this.pending, required this.onTap});
  final NearestStore store;
  final bool current, pending;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final selectedNew = pending && !current;
    return Semantics(
      button: true,
      selected: pending,
      label: store.name,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
          decoration: BoxDecoration(color: p.dark ? p.border : Colors.white, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: selectedNew ? Pal.yellow : Colors.transparent, width: 3)),
          child: Row(children: [
            Container(width: 44, height: 44, decoration: BoxDecoration(color: selectedNew ? Pal.yellow : p.chip, shape: BoxShape.circle), child: const Icon(Icons.storefront_outlined, color: Pal.ink, size: 22)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(store.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 16)),
                Text('${store.distanceKm.toStringAsFixed(1)} km · ${context.tr('{n} min', {'n': '${store.estimatedMinutes}'})}${current ? ' · ${context.tr('Current store')}' : (selectedNew ? ' · ${context.tr('Selected')}' : '')}', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
              ]),
            ),
            if (pending)
              Container(width: 24, height: 24, decoration: BoxDecoration(color: current ? Pal.ink : Pal.yellow, shape: BoxShape.circle, border: current ? null : Border.all(color: Pal.ink, width: 2)), child: Icon(Icons.check, size: 14, color: current ? Pal.yellow : Pal.ink))
            else
              Container(width: 24, height: 24, decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: p.mutedOnCard, width: 2))),
          ]),
        ),
      ),
    );
  }
}
