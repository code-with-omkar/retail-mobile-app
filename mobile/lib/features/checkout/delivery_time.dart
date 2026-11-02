import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';
import '../../data/repositories/delivery_slots_repository.dart';
import '../cart/cart_controller.dart';

/// What the store offers for the cart's store right now. When it cannot be read, the customer simply gets "as soon as possible"
/// (the server decides again when the order is placed).
final deliverySlotsProvider = FutureProvider.autoDispose<DeliverySlots>((ref) async {
  final storeId = ref.watch(serverCartProvider.select((cart) => cart?.storeId)) ?? (await ref.watch(selectedStoreProvider.future))?.id;
  if (storeId == null) return DeliverySlots.off;
  try {
    return await ref.watch(deliverySlotsRepositoryProvider).list(storeId);
  } on ApiException {
    return DeliverySlots.off;
  }
});

/// The window the customer chose (its start, UTC); null means as soon as possible.
class ChosenSlot extends Notifier<DateTime?> {
  @override
  DateTime? build() => null;

  void choose(DateTime? start) => state = start;
}

final chosenSlotProvider = NotifierProvider<ChosenSlot, DateTime?>(ChosenSlot.new);

/// Whether the customer still has to choose a time: scheduling is on, "as soon as possible" is off and nothing is chosen yet.
bool deliveryTimeMissing(DeliverySlots slots, DateTime? chosen) => slots.enabled && !slots.asapAllowed && chosen == null;

/// The words for a chosen window, for the green card and the order: "Today, 2 pm - 3 pm" (day translated).
String slotWords(BuildContext context, DateTime start, DateTime end) {
  final t = slotText(start, end, DateTime.now());
  return '${context.tr(t.day)}, ${t.time}';
}

/// Delivery time choice at checkout: "as soon as possible" and the windows the store offers, a day at a time. Hidden when the store does not schedule.
class DeliveryTimeSection extends ConsumerStatefulWidget {
  const DeliveryTimeSection({super.key});

  @override
  ConsumerState<DeliveryTimeSection> createState() => _DeliveryTimeSectionState();
}

class _DeliveryTimeSectionState extends ConsumerState<DeliveryTimeSection> {
  String? _day;

  @override
  Widget build(BuildContext context) {
    final slots = ref.watch(deliverySlotsProvider).value ?? DeliverySlots.off;
    if (!slots.enabled) return const SizedBox.shrink();
    final chosen = ref.watch(chosenSlotProvider);
    final p = context.pal;
    final byDay = slots.byDay;
    // A window that filled up or passed its cut-off while the customer was looking is dropped from the choice.
    if (chosen != null && !slots.windows.any((w) => w.start == chosen && w.available)) {
      Future.microtask(() => mounted ? ref.read(chosenSlotProvider.notifier).choose(null) : null);
    }
    final chosenDay = chosen == null ? null : slots.windows.where((w) => w.start == chosen).map((w) => w.day).firstOrNull;
    final day = byDay.containsKey(_day) ? _day! : (chosenDay ?? byDay.keys.firstOrNull);
    final windows = day == null ? const <DeliverySlot>[] : byDay[day]!;

    Widget chip({required String label, required bool selected, required VoidCallback? onTap}) => Semantics(
          button: true,
          selected: selected,
          enabled: onTap != null,
          label: label,
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: onTap,
            child: Container(
              constraints: const BoxConstraints(minHeight: QC.minTap, minWidth: QC.minTap),
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: selected ? Pal.yellow : (onTap == null ? p.card.withValues(alpha: .5) : p.card),
                borderRadius: BorderRadius.circular(QC.rPill),
                border: Border.all(color: selected ? Pal.yellow : p.border),
              ),
              child: Text(label, style: TextStyle(color: selected ? Pal.ink : (onTap == null ? p.mutedOnCard : p.onCard), fontWeight: FontWeight.w800, fontSize: 13)),
            ),
          ),
        );

    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      const SizedBox(height: 24),
      Text(context.tr('Delivery time'), style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
      const SizedBox(height: 12),
      if (slots.asapAllowed) ...[
        chip(label: context.tr('As soon as possible'), selected: chosen == null, onTap: () => ref.read(chosenSlotProvider.notifier).choose(null)),
        const SizedBox(height: 10),
      ] else if (chosen == null)
        Padding(padding: const EdgeInsets.only(bottom: 10), child: Text(context.tr('Choose a delivery time to continue.'), style: const TextStyle(color: Pal.mutedOnGround, fontWeight: FontWeight.w700))),
      if (byDay.isEmpty)
        Text(context.tr('No delivery times are open right now.'), style: const TextStyle(color: Pal.mutedOnGround))
      else ...[
        Wrap(spacing: 8, runSpacing: 8, children: [
          for (final entry in byDay.entries) chip(label: context.tr(entry.value.first.dayLabel), selected: entry.key == day, onTap: () => setState(() => _day = entry.key)),
        ]),
        const SizedBox(height: 10),
        Wrap(spacing: 8, runSpacing: 8, children: [
          for (final w in windows)
            chip(
              label: w.available ? w.timeLabel : '${w.timeLabel} · ${context.tr('Full')}',
              selected: chosen == w.start,
              onTap: w.available ? () => ref.read(chosenSlotProvider.notifier).choose(w.start) : null,
            ),
        ]),
      ],
    ]);
  }
}
