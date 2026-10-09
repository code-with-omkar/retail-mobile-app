import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_widgets.dart';
import 'address_controller.dart';
import 'delivery_actions.dart';

/// Where to deliver: the signed-in customer's saved addresses (choose, add, edit, make default, delete), or for a guest the place
/// kept on this device with a way to save addresses by signing in.
class AddressListScreen extends ConsumerWidget {
  const AddressListScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final signedIn = ref.watch(authProvider).isSignedIn;
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Delivery addresses')),
      body: signedIn ? const _SavedList() : const _GuestView(),
    );
  }
}

class _GuestView extends ConsumerWidget {
  const _GuestView();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final place = ref.watch(devicePlaceProvider);
    return ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
      if (place != null) ...[
        Text(context.tr('Delivering to'), style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
        const SizedBox(height: 12),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: Pal.yellow, width: 2.5)),
          child: Row(children: [
            Icon(Icons.my_location, color: p.onCard),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(context.tr(place.label), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
                Text(place.line, maxLines: 3, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
              ]),
            ),
            Icon(Icons.check_circle, color: p.dark ? Pal.yellow : Pal.ink),
          ]),
        ),
        const SizedBox(height: 20),
      ],
      PillButton(context.tr('Use my current location'), arrow: true, onPressed: () => useCurrentLocation(context, ref)),
      const SizedBox(height: 12),
      SoftPillButton(context.tr('Enter an address'), height: 60, onPressed: () => context.push('/addresses/new')),
      const SizedBox(height: 24),
      Surface(
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(Icons.bookmark_border, color: p.onCard),
            const SizedBox(width: 10),
            Expanded(child: Text(context.tr('Save addresses to your account'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900))),
          ]),
          const SizedBox(height: 6),
          Text(context.tr('Sign in to keep your addresses, set a default and use them at checkout.'), style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
          const SizedBox(height: 12),
          SoftPillButton(context.tr('Sign in / Register'), height: 52, onPressed: () => context.push(withNext('/welcome', '/addresses'))),
        ]),
      ),
    ]);
  }
}

class _SavedList extends ConsumerWidget {
  const _SavedList();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final saved = ref.watch(savedAddressesProvider);
    final selectedId = ref.watch(deliveryPlaceProvider)?.addressId;
    return saved.when(
      loading: () => ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 24), children: [for (var i = 0; i < 3; i++) const Padding(padding: EdgeInsets.only(bottom: 12), child: SkeletonBox(height: 104, radius: QC.rCard))]),
      error: (e, _) => ErrorState(error: e, onRetry: () => ref.invalidate(savedAddressesProvider)),
      data: (list) => ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
        if (list.isEmpty)
          Padding(padding: const EdgeInsets.only(bottom: 16), child: Text(context.tr('You have no saved addresses yet. Add one so we can find the stores that deliver to you.'), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 15, height: 1.3))),
        for (final address in list)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: _AddressCard(
              address,
              selected: address.id == selectedId,
              onTap: () async {
                if (await chooseSavedAddress(context, ref, address) && context.mounted) goBack(context);
              },
            ),
          ),
        const SizedBox(height: 4),
        PillButton(context.tr('Add new address'), arrow: true, onPressed: () => context.push('/addresses/new')),
        const SizedBox(height: 12),
        SoftPillButton(context.tr('Use my current location'), height: 60, onPressed: () => useCurrentLocation(context, ref)),
      ]),
    );
  }
}

class _AddressCard extends ConsumerWidget {
  const _AddressCard(this.address, {required this.selected, required this.onTap});
  final SavedAddress address;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    return Semantics(
      button: true,
      selected: selected,
      label: '${context.tr(address.label)}, ${address.fullLine}',
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.fromLTRB(16, 14, 4, 14),
          decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rCard), border: Border.all(color: selected ? Pal.yellow : Colors.transparent, width: 3)),
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Padding(padding: const EdgeInsets.only(top: 2), child: Icon(_iconFor(address.label), color: p.onCard)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  Flexible(child: Text(context.tr(address.label), maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 17))),
                  if (address.isDefault) ...[const SizedBox(width: 8), _Badge(context.tr('Default'), Pal.green, Pal.ink)],
                  if (selected) ...[const SizedBox(width: 8), Icon(Icons.check_circle, size: 18, color: p.dark ? Pal.yellow : Pal.ink)],
                ]),
                const SizedBox(height: 2),
                Text(address.fullLine, maxLines: 3, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 13, height: 1.3)),
                if (address.landmark.isNotEmpty) Text(address.landmark, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                const SizedBox(height: 4),
                Text('${address.receiverName} · ${address.receiverPhone}', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontSize: 12, fontWeight: FontWeight.w700)),
                if (!address.serviceable) ...[const SizedBox(height: 8), _Badge(context.tr('We do not deliver here yet'), Pal.pink, Colors.white)],
              ]),
            ),
            PopupMenuButton<_Action>(
              tooltip: context.tr('More'),
              icon: Icon(Icons.more_vert, color: p.onCard),
              onSelected: (action) => _run(context, ref, action),
              itemBuilder: (context) => [
                PopupMenuItem(value: _Action.edit, child: Text(context.tr('Edit'))),
                if (!address.isDefault) PopupMenuItem(value: _Action.makeDefault, child: Text(context.tr('Make default'))),
                PopupMenuItem(value: _Action.delete, child: Text(context.tr('Delete'))),
              ],
            ),
          ]),
        ),
      ),
    );
  }

  Future<void> _run(BuildContext context, WidgetRef ref, _Action action) async {
    switch (action) {
      case _Action.edit:
        await context.push('/addresses/${address.id}/edit', extra: AddressEditorArgs(existing: address));
      case _Action.makeDefault:
        try {
          await ref.read(savedAddressesProvider.notifier).makeDefault(address.id);
        } catch (e) {
          if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(addressErrorText(context, e))));
        }
      case _Action.delete:
        final sure = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: Text(context.tr('Delete this address?')),
            content: Text(address.fullLine),
            actions: [
              TextButton(onPressed: () => Navigator.of(context).pop(false), child: Text(context.tr('Cancel'))),
              TextButton(onPressed: () => Navigator.of(context).pop(true), child: Text(context.tr('Delete'))),
            ],
          ),
        );
        if (sure != true) return;
        try {
          await ref.read(savedAddressesProvider.notifier).remove(address.id);
        } catch (e) {
          if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(addressErrorText(context, e))));
        }
    }
  }

  static IconData _iconFor(String label) => switch (label) {
        'Home' => Icons.home_outlined,
        'Work' => Icons.work_outline,
        _ => Icons.place_outlined,
      };
}

enum _Action { edit, makeDefault, delete }

class _Badge extends StatelessWidget {
  const _Badge(this.text, this.background, this.foreground);
  final String text;
  final Color background, foreground;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
        decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(QC.rPill)),
        child: Text(text, style: TextStyle(color: foreground, fontSize: 11, fontWeight: FontWeight.w900)),
      );
}
