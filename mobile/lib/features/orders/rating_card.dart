import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';
import '../../data/repositories/rating_repository.dart';

final orderRatingProvider = FutureProvider.autoDispose.family<OrderRatingState, String>((ref, orderId) => ref.watch(ratingRepositoryProvider).state(orderId));

/// "How was your order?" under a delivered order: stars for the store (and the delivery when a partner brought it), an optional comment,
/// and afterwards what was given, which can be changed for 14 days. Hidden when nothing can be rated and nothing was given.
class RatingCard extends ConsumerStatefulWidget {
  const RatingCard(this.orderId, {super.key, this.storeName});
  final String orderId;
  final String? storeName;

  @override
  ConsumerState<RatingCard> createState() => _RatingCardState();
}

class _RatingCardState extends ConsumerState<RatingCard> {
  int _store = 0;
  int _delivery = 0;
  final _comment = TextEditingController();
  bool _editing = false;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  void _startEditing(OrderRating rating) => setState(() {
        _editing = true;
        _store = rating.storeStars;
        _delivery = rating.deliveryStars ?? 0;
        _comment.text = rating.comment ?? '';
        _error = null;
      });

  Future<void> _send(OrderRatingState state) async {
    if (_busy || _store == 0) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(ratingRepositoryProvider).save(widget.orderId, storeStars: _store, deliveryStars: state.canRateDelivery && _delivery > 0 ? _delivery : null, comment: _comment.text);
      ref.invalidate(orderRatingProvider(widget.orderId));
      if (!mounted) return;
      setState(() => _editing = false);
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(context.tr('Thank you for your rating.'))));
    } catch (e) {
      if (mounted) {
        setState(() => _error = switch (e) {
              ConflictException() => context.tr('This order can no longer be rated.'),
              ValidationException() => context.tr('Please check your rating. A comment cannot contain < or >.'),
              ApiException() => context.tr(e.userMessageKey),
              _ => context.tr('Something went wrong. Please try again.'),
            });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final state = ref.watch(orderRatingProvider(widget.orderId)).value;
    if (state == null || (!state.canRate && state.rating == null)) return const SizedBox.shrink();
    final rating = state.rating;
    final showForm = state.canRate && (rating == null || _editing);
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Surface(
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (!showForm && rating != null) ...[
            Text(context.tr('Your rating'), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
            const SizedBox(height: 4),
            _Stars(rating.storeStars, key: const Key('given-store')),
            if (rating.deliveryStars != null) ...[
              const SizedBox(height: 4),
              Row(children: [_Stars(rating.deliveryStars!, size: 18, key: const Key('given-delivery')), const SizedBox(width: 8), Text(context.tr('delivery'), style: TextStyle(color: p.mutedOnCard, fontSize: 12))]),
            ],
            if (rating.comment != null) ...[const SizedBox(height: 8), Text(rating.comment!, style: TextStyle(color: p.onCard, height: 1.3))],
            if (rating.canEdit) Align(alignment: Alignment.centerLeft, child: TextButton(onPressed: () => _startEditing(rating), child: Text(context.tr('Change rating')))),
          ] else ...[
            Text(context.tr(widget.storeName == null ? 'How was your order?' : 'How was {store}?', {if (widget.storeName != null) 'store': widget.storeName!}), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 16)),
            const SizedBox(height: 6),
            _Picker(label: context.tr('Rate the order'), value: _store, enabled: !_busy, keyPrefix: 'store', onChanged: (v) => setState(() => _store = v)),
            if (state.canRateDelivery) ...[
              const SizedBox(height: 10),
              Text(context.tr('How was the delivery?'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
              _Picker(label: context.tr('Rate the delivery'), value: _delivery, enabled: !_busy, keyPrefix: 'delivery', onChanged: (v) => setState(() => _delivery = v)),
            ],
            const SizedBox(height: 10),
            TextField(
              key: const Key('rating-comment'),
              controller: _comment,
              enabled: !_busy,
              maxLength: 500,
              maxLines: 3,
              minLines: 2,
              decoration: InputDecoration(hintText: context.tr('Add a comment (optional)')),
              style: TextStyle(color: p.onCard),
            ),
            if (_error != null) ErrorBannerCompact(_error!),
            const SizedBox(height: 8),
            PillButton(context.tr(_busy ? 'Please wait…' : 'Send rating'), height: 52, onPressed: _store == 0 || _busy ? null : () => _send(state)),
            if (_editing) Align(alignment: Alignment.centerLeft, child: TextButton(onPressed: _busy ? null : () => setState(() => _editing = false), child: Text(context.tr('Cancel')))),
          ],
        ]),
      ),
    );
  }
}

/// Five tappable stars. Each is a button with a name a screen reader can say.
class _Picker extends StatelessWidget {
  const _Picker({required this.label, required this.value, required this.enabled, required this.keyPrefix, required this.onChanged});
  final String label, keyPrefix;
  final int value;
  final bool enabled;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) => Row(children: [
        for (var n = 1; n <= 5; n++)
          IconButton(
            key: Key('$keyPrefix-star-$n'),
            tooltip: '$label: $n',
            visualDensity: VisualDensity.compact,
            onPressed: enabled ? () => onChanged(n) : null,
            icon: Icon(n <= value ? Icons.star_rounded : Icons.star_border_rounded, color: n <= value ? const Color(0xFFE8A400) : context.pal.mutedOnCard, size: 32),
          ),
      ]);
}

class _Stars extends StatelessWidget {
  const _Stars(this.value, {super.key, this.size = 24});
  final int value;
  final double size;

  @override
  Widget build(BuildContext context) => Semantics(
        label: '$value / 5',
        child: Row(mainAxisSize: MainAxisSize.min, children: [for (var n = 1; n <= 5; n++) Icon(n <= value ? Icons.star_rounded : Icons.star_border_rounded, color: const Color(0xFFE8A400), size: size)]),
      );
}

/// A short error line inside a card (the full-width banner is for the dark ground).
class ErrorBannerCompact extends StatelessWidget {
  const ErrorBannerCompact(this.message, {super.key});
  final String message;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 4),
        child: Semantics(liveRegion: true, child: Text(message, style: const TextStyle(color: Pal.pink, fontWeight: FontWeight.w800))),
      );
}
