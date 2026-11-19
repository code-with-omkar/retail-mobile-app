import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../stores/store_widgets.dart';
import 'product_pager.dart';

/// Categories tab (v4): the shop at the top, a rail of categories on the left and the chosen category's products, three across, on the right.
class CategoriesScreen extends ConsumerStatefulWidget {
  const CategoriesScreen({super.key});

  @override
  ConsumerState<CategoriesScreen> createState() => _CategoriesScreenState();
}

class _CategoriesScreenState extends ConsumerState<CategoriesScreen> {
  String? _selected;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final cats = ref.watch(categoriesProvider);
    return SafeArea(
      bottom: false,
      child: Column(children: [
        // Which store these categories are from, with a way to change it.
        const Padding(padding: EdgeInsets.fromLTRB(QC.gutter, 12, QC.gutter, 10), child: CompactStoreBar(flush: true)),
        Divider(height: 1, color: p.border),
        Expanded(
          child: cats.when(
            loading: () => const Padding(padding: EdgeInsets.all(QC.gutter), child: SkeletonBox(radius: QC.rCard)),
            error: (e, _) => ErrorState(error: e, onRetry: () => ref.invalidate(categoriesProvider)),
            data: (list) {
              if (list.isEmpty) {
                return EmptyState(icon: Icons.category_outlined, title: context.tr('Nothing here yet'), message: context.tr('Check back soon.'));
              }
              final current = list.where((c) => c.id == _selected).firstOrNull ?? list.first;
              return Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                _Rail(list, current.id, (id) => setState(() => _selected = id)),
                VerticalDivider(width: 1, thickness: 1, color: p.border),
                Expanded(child: CategoryProducts(current.id, key: ValueKey(current.id), title: categoryLabel(context, current))),
              ]);
            },
          ),
        ),
      ]),
    );
  }
}

/// The left rail: one tinted icon tile and a short label per category. The one in view has a yellow mark on its edge.
class _Rail extends StatelessWidget {
  const _Rail(this.list, this.currentId, this.onPick);
  final List<Category> list;
  final String currentId;
  final ValueChanged<String> onPick;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return SizedBox(
      width: 80,
      child: ListView.builder(
        padding: const EdgeInsets.only(top: 4, bottom: 24),
        itemCount: list.length,
        itemBuilder: (_, i) {
          final c = list[i];
          final on = c.id == currentId;
          return Semantics(
            button: true,
            selected: on,
            label: categoryLabel(context, c),
            child: GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: () => onPick(c.id),
              child: Container(
                color: on ? p.card : Colors.transparent,
                padding: const EdgeInsets.symmetric(vertical: 8, horizontal: 4),
                child: Stack(children: [
                  Positioned(left: -4, top: 0, bottom: 0, child: Container(width: 4, decoration: BoxDecoration(color: on ? Pal.yellow : Colors.transparent, borderRadius: const BorderRadius.horizontal(right: Radius.circular(4))))),
                  SizedBox(
                    width: double.infinity,
                    child: Column(children: [
                      Container(
                        width: 52,
                        height: 52,
                        decoration: BoxDecoration(color: p.tiles[i % 4], borderRadius: BorderRadius.circular(14), border: Border.all(color: on ? Pal.yellow : Colors.transparent, width: 2)),
                        child: Icon(c.icon, size: 26, color: Pal.ink),
                      ),
                      const SizedBox(height: 4),
                      ExcludeSemantics(
                        child: Text(categoryLabel(context, c), maxLines: 2, textAlign: TextAlign.center, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onGround, fontSize: 10, height: 1.2, fontWeight: on ? FontWeight.w800 : FontWeight.w600)),
                      ),
                    ]),
                  ),
                ]),
              ),
            ),
          );
        },
      ),
    );
  }
}

/// Products in one category, loaded page by page. A page of its own for a link to one category; the right half of the Categories tab.
class CategoryProductsScreen extends ConsumerWidget {
  const CategoryProductsScreen(this.id, {super.key});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final category = ref.watch(categoriesProvider).value?.where((c) => c.id == id).firstOrNull;
    return AppScaffold(
      appBar: appTopBar(context, category == null ? context.tr('Categories') : categoryLabel(context, category)),
      body: Column(children: [
        const CompactStoreBar(),
        Expanded(child: CategoryProducts(id, showCount: true)),
      ]),
    );
  }
}

class CategoryProducts extends ConsumerStatefulWidget {
  const CategoryProducts(this.id, {super.key, this.title, this.showCount = false});
  final String id;

  /// Show how many items the category has, even without a title.
  final bool showCount;

  /// A heading with the item count above the grid; none on the page of its own (its app bar names the category).
  final String? title;

  @override
  ConsumerState<CategoryProducts> createState() => _CategoryProductsState();
}

class _CategoryProductsState extends ConsumerState<CategoryProducts> {
  late ProductPager _pager = _newPager();

  ProductPager _newPager() => ProductPager(
        ref.read(catalogRepositoryProvider),
        categoryId: widget.id,
        storeId: ref.read(storeIdResolverProvider),
      )..addListener(() => mounted ? setState(() {}) : null);

  @override
  void initState() {
    super.initState();
    _pager.loadFirst();
    // Another store means another catalogue: start over with that store's items.
    ref.listenManual(selectedStoreProvider, (before, after) {
      if (before?.value?.id == after.value?.id) return;
      _pager.dispose();
      setState(() => _pager = _newPager()..loadFirst());
    });
  }

  @override
  void dispose() {
    _pager.dispose();
    super.dispose();
  }

  static const _grid = SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 3, mainAxisSpacing: 12, crossAxisSpacing: 8, mainAxisExtent: 178);

  @override
  Widget build(BuildContext context) {
    final pager = _pager;
    final side = widget.title == null ? QC.gutter : 12.0; // beside the rail the grid has less room
    final heading = widget.title == null && !widget.showCount
        ? null
        : Padding(
            padding: EdgeInsets.fromLTRB(side, 12, side, 4),
            child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, crossAxisAlignment: CrossAxisAlignment.baseline, textBaseline: TextBaseline.alphabetic, children: [
              if (widget.title == null) const Spacer(),
              if (widget.title != null) Flexible(child: Text(widget.title!, maxLines: 1, overflow: TextOverflow.ellipsis, style: QC.display(17, color: context.pal.onGround))),
              if (!pager.isEmpty && pager.items.isNotEmpty) Text(context.tr('{n} items', {'n': '${pager.totalCount}'}), style: TextStyle(color: context.pal.mutedOnGround, fontSize: 11)),
            ]),
          );
    final Widget body = pager.isInitialLoading
        ? GridView.builder(
            padding: EdgeInsets.fromLTRB(side, 8, side, 24),
            gridDelegate: _grid,
            itemCount: 6,
            itemBuilder: (_, _) => const SkeletonBox(radius: QC.rCard),
          )
        : pager.error != null && pager.items.isEmpty
            ? ErrorState(error: pager.error!, onRetry: pager.retry)
            : pager.isEmpty
                ? EmptyState(icon: Icons.category_outlined, title: context.tr('Nothing here yet'), message: context.tr('Check back soon.'))
                : CustomScrollView(slivers: [
                    SliverPadding(
                      padding: EdgeInsets.fromLTRB(side, 8, side, 12),
                      sliver: SliverGrid.builder(gridDelegate: _grid, itemCount: pager.items.length, itemBuilder: (_, i) => ProductCard(pager.items[i])),
                    ),
                    SliverPadding(
                      padding: EdgeInsets.fromLTRB(side, 0, side, 24),
                      sliver: SliverToBoxAdapter(
                        child: pager.error != null
                            ? ErrorState(error: pager.error!, compact: true, onRetry: pager.retry)
                            : pager.hasMore
                                ? PillButton(pager.loading ? context.tr('Loading…') : context.tr('Load more'), height: 56, onPressed: pager.loading ? null : pager.loadMore)
                                : const SizedBox.shrink(),
                      ),
                    ),
                  ]);
    return Column(children: [?heading, Expanded(child: body)]);
  }
}
