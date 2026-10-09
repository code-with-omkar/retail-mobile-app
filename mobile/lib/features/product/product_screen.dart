import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../stores/store_widgets.dart';
import '../cart/cart_controller.dart';

/// Loads the product, then shows [_ProductView]. Loading and failure keep the back button available.
class ProductScreen extends ConsumerWidget {
  const ProductScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) => ref.watch(productProvider(id)).when(
        data: (product) => _ProductView(product, key: ValueKey(product.id)),
        loading: () => AppScaffold(
          appBar: appTopBar(context, ''),
          body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24), children: const [
            SkeletonBox(height: 300, radius: 36),
            SizedBox(height: 20),
            SkeletonBox(width: 160, height: 36),
            SizedBox(height: 12),
            SkeletonBox(width: 220, height: 20),
            SizedBox(height: 12),
            SkeletonBox(height: 56, radius: 999),
          ]),
        ),
        error: (e, _) => AppScaffold(appBar: appTopBar(context, ''), body: ErrorState(error: e, onRetry: () => ref.invalidate(productProvider(id)))),
      );
}

class _ProductView extends ConsumerStatefulWidget {
  const _ProductView(this.product, {super.key});
  final Product product;

  @override
  ConsumerState<_ProductView> createState() => _ProductViewState();
}

class _ProductViewState extends ConsumerState<_ProductView> {
  late final Product _p = widget.product;
  late PackOption? _pack = _initialPack();
  int _qty = 1;
  bool _fav = false;

  /// The default pack, or the first pack in stock when the default is sold out.
  PackOption? _initialPack() {
    final packs = _p.packs;
    if (packs.isEmpty) return null;
    final fallback = _p.defaultPack!;
    return fallback.inStock ? fallback : (packs.where((pack) => pack.inStock).firstOrNull ?? fallback);
  }

  // Stock, price and savings follow the chosen pack; a product without packs uses its own.
  bool get _inStock => _pack?.inStock ?? _p.inStock;
  bool get _lowStock => _pack?.lowStock ?? _p.lowStock;

  void _add() {
    ref.read(cartProvider.notifier).add(cartKey(_p, _pack), _qty);
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(context.tr('Added to cart'))));
    setState(() => _qty = 1);
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final price = _pack?.price ?? _p.price, mrp = _pack?.mrp ?? _p.mrp;
    final mr = context.isMarathi;
    final related = ref.watch(relatedProductsProvider((categoryId: _p.categoryId, excludeId: _p.id))).value ?? const <Product>[];
    return AppScaffold(
      body: Column(children: [
        Expanded(
          child: ListView(padding: EdgeInsets.zero, children: [
            SizedBox(
              height: 360,
              child: ClipRRect(
                borderRadius: const BorderRadius.vertical(bottom: Radius.circular(40)),
                child: Stack(fit: StackFit.expand, children: [
                  ProductVisual(_p),
                  SafeArea(
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(QC.gutter, 12, QC.gutter, 0),
                      child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, crossAxisAlignment: CrossAxisAlignment.start, children: [
                        CircleIconButton(icon: Icons.chevron_left, tooltip: context.tr('Back'), onPressed: () => context.canPop() ? context.pop() : context.go('/')),
                        CircleIconButton(icon: _fav ? Icons.favorite : Icons.favorite_border, color: Pal.pink, tooltip: context.tr('Favourite'), onPressed: () => setState(() => _fav = !_fav)),
                      ]),
                    ),
                  ),
                  if (_p.discountPct > 0) Positioned(right: 20, bottom: 24, child: DiscountSticker(_p.discountPct, large: true)),
                ]),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(QC.gutter, 20, QC.gutter, 24),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Wrap(spacing: 8, runSpacing: 8, children: [
                  _inStock
                      ? Chip2(context.tr('In stock'), dot: Pal.green, background: p.dark ? const Color(0xFF0B0820) : Pal.ink, foreground: Pal.green)
                      : Chip2(context.tr('Out of stock'), background: Pal.pink, foreground: Colors.white),
                  if (_inStock && _lowStock) Chip2(context.tr('Only a few left'), background: Pal.pink, foreground: Colors.white),
                  Chip2(context.tr('Fresh today')),
                ]),
                if (!_inStock) Align(alignment: Alignment.centerLeft, child: TextButton(onPressed: () => showStoreSheet(context), child: Text(context.tr('Check other stores')))),
                const SizedBox(height: 16),
                Text.rich(
                  TextSpan(children: [
                    TextSpan(text: mr ? _p.nameMr : _p.name, style: const TextStyle(color: Colors.white)),
                    // The second-language name is shown only when it differs (the API has no Marathi names yet).
                    if (_p.nameMr != _p.name) TextSpan(text: '  ${mr ? _p.name : _p.nameMr}', style: const TextStyle(color: Color(0xFFC9B8FF), fontSize: 32)),
                  ]),
                  style: const TextStyle(fontSize: 40, fontWeight: FontWeight.w900, height: 1.1),
                ),
                const SizedBox(height: 6),
                if (ref.watch(selectedStoreProvider).value case final store?) ...[
                  Text(context.tr('Sold by {store}', {'store': store.name}), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 16)),
                  const SizedBox(height: 10),
                ],
                Wrap(crossAxisAlignment: WrapCrossAlignment.end, spacing: 14, children: [
                  Text(rupees(price), style: const TextStyle(color: Colors.white, fontSize: 48, fontWeight: FontWeight.w900, height: 1.1)),
                  if (mrp > price) Padding(padding: const EdgeInsets.only(bottom: 8), child: Text(rupees(mrp), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 20, decoration: TextDecoration.lineThrough))),
                  if (mrp > price) Padding(padding: const EdgeInsets.only(bottom: 8), child: Text(context.tr('You save ₹{n}', {'n': amount(mrp - price)}), style: const TextStyle(color: Pal.green, fontSize: 18, fontWeight: FontWeight.w800))),
                ]),
                if (_p.packs.length > 1) ...[const SizedBox(height: 16), _PackSelector(_p.packs, _pack!, (pack) => setState(() => _pack = pack))] else ...[const SizedBox(height: 4), Text(_p.packs.length == 1 ? _p.packs.single.label : _p.unit, style: const TextStyle(color: Pal.mutedOnGround))],
                const SizedBox(height: 20),
                Text(productDescription(context, _p), style: const TextStyle(color: Colors.white, height: 1.45, fontSize: 15)),
                if (related.isNotEmpty) ...[
                  const SizedBox(height: 28),
                  SectionHeader(context.tr('You might also like')),
                  const SizedBox(height: 12),
                  SizedBox(
                    height: 188,
                    child: ListView.separated(
                      scrollDirection: Axis.horizontal,
                      clipBehavior: Clip.none,
                      itemCount: related.length,
                      separatorBuilder: (_, _) => const SizedBox(width: 12),
                      itemBuilder: (_, i) => SizedBox(width: 160, child: ProductCard(related[i])),
                    ),
                  ),
                ],
              ]),
            ),
          ]),
        ),
        SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 12),
            child: Row(children: [
              if (_inStock) ...[
                Container(
                  height: 64,
                  padding: const EdgeInsets.all(6),
                  decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rPill)),
                  child: Row(mainAxisSize: MainAxisSize.min, children: [
                    _RoundStep(Icons.remove, p.dark ? p.minus : Pal.ink, p.dark ? Pal.ink : Colors.white, context.tr('Decrease quantity'), _qty > 1 ? () => setState(() => _qty--) : null),
                    SizedBox(width: 44, child: Text('$_qty', textAlign: TextAlign.center, style: TextStyle(color: p.onCard, fontSize: 20, fontWeight: FontWeight.w900))),
                    _RoundStep(Icons.add, Pal.yellow, Pal.ink, context.tr('Increase quantity'), () => setState(() => _qty++)),
                  ]),
                ),
                const SizedBox(width: 12),
              ],
              Expanded(child: PillButton(_inStock ? '${context.tr('Add')} · ${rupees(price * _qty)}' : context.tr('Out of stock'), onPressed: _inStock ? _add : null, height: 64)),
            ]),
          ),
        ),
      ]),
    );
  }
}

class _RoundStep extends StatelessWidget {
  const _RoundStep(this.icon, this.bg, this.fg, this.label, this.onTap);
  final IconData icon;
  final Color bg, fg;
  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: label,
        child: GestureDetector(
          onTap: onTap,
          child: Container(width: 52, height: 52, decoration: BoxDecoration(color: bg, shape: BoxShape.circle), child: Icon(icon, color: fg)),
        ),
      );
}

/// Pack sizes of a product. A pack that is out of stock at this store is dimmed and cannot be chosen.
class _PackSelector extends StatelessWidget {
  const _PackSelector(this.packs, this.selected, this.onChanged);
  final List<PackOption> packs;
  final PackOption selected;
  final ValueChanged<PackOption> onChanged;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    Widget pill(PackOption pack) => Semantics(
          button: true,
          selected: pack == selected,
          enabled: pack.inStock,
          label: pack.inStock ? pack.label : '${pack.label}, ${context.tr('Out of stock')}',
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: pack.inStock ? () => onChanged(pack) : null,
            child: Container(
              alignment: Alignment.center,
              padding: const EdgeInsets.symmetric(horizontal: 12),
              decoration: BoxDecoration(color: pack == selected ? Pal.yellow : Colors.transparent, borderRadius: BorderRadius.circular(QC.rPill)),
              child: Opacity(
                opacity: pack.inStock ? 1 : .4,
                child: Text(pack.label, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: pack == selected ? Pal.ink : p.onCard, fontWeight: FontWeight.w800, fontSize: 16, decoration: pack.inStock ? null : TextDecoration.lineThrough)),
              ),
            ),
          ),
        );
    return Container(
      height: 64,
      padding: const EdgeInsets.all(6),
      decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rPill), border: p.dark ? Border.all(color: p.border) : null),
      // Up to four packs share the width; more scroll sideways.
      child: packs.length <= 4
          ? Row(children: [for (final pack in packs) Expanded(child: pill(pack))])
          : ListView(scrollDirection: Axis.horizontal, children: [for (final pack in packs) SizedBox(width: 92, child: pill(pack))]),
    );
  }
}
