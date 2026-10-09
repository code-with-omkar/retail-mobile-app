import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/providers.dart';
import '../stores/store_widgets.dart';
import 'product_pager.dart';

/// Categories tab: big colourful tiles, one per category.
class CategoriesScreen extends ConsumerWidget {
  const CategoriesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final cats = ref.watch(categoriesProvider);
    return SafeArea(
      bottom: false,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(QC.gutter, 20, QC.gutter, 24),
        children: [
          Text(
            context.tr('Categories'),
            style: const TextStyle(
              color: Colors.white,
              fontSize: 32,
              fontWeight: FontWeight.w900,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            context.tr('Everything your local shop has'),
            style: const TextStyle(color: Pal.mutedOnGround),
          ),
          const SizedBox(height: 14),
          // Which store these categories are from, with a way to change it.
          const CompactStoreBar(flush: true),
          const SizedBox(height: 6),
          cats.when(
            loading: () => GridView.count(
              crossAxisCount: 2,
              mainAxisSpacing: 12,
              crossAxisSpacing: 12,
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              childAspectRatio: 1.25,
              children: [
                for (var i = 0; i < 4; i++) const SkeletonBox(radius: QC.rCard),
              ],
            ),
            error: (e, _) => ErrorState(
              error: e,
              onRetry: () => ref.invalidate(categoriesProvider),
            ),
            data: (list) => list.isEmpty
                ? EmptyState(
                    icon: Icons.category_outlined,
                    title: context.tr('Nothing here yet'),
                    message: context.tr('Check back soon.'),
                  )
                : GridView.count(
                    crossAxisCount: 2,
                    mainAxisSpacing: 12,
                    crossAxisSpacing: 12,
                    shrinkWrap: true,
                    physics: const NeverScrollableScrollPhysics(),
                    childAspectRatio: 1.25,
                    children: [
                      for (var i = 0; i < list.length; i++)
                        GestureDetector(
                          onTap: () => context.push('/category/${list[i].id}'),
                          child: Container(
                            padding: const EdgeInsets.all(18),
                            decoration: BoxDecoration(
                              color: p.tiles[i % 4],
                              borderRadius: BorderRadius.circular(QC.rCard),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Icon(list[i].icon, size: 36, color: Pal.ink),
                                const Spacer(),
                                Text(
                                  categoryLabel(context, list[i]),
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    color: Pal.ink,
                                    fontSize: 20,
                                    fontWeight: FontWeight.w900,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}

/// Products in one category, loaded page by page.
class CategoryProductsScreen extends ConsumerStatefulWidget {
  const CategoryProductsScreen(this.id, {super.key});
  final String id;

  @override
  ConsumerState<CategoryProductsScreen> createState() =>
      _CategoryProductsScreenState();
}

class _CategoryProductsScreenState
    extends ConsumerState<CategoryProductsScreen> {
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

  @override
  Widget build(BuildContext context) {
    final category = ref
        .watch(categoriesProvider)
        .value
        ?.where((c) => c.id == widget.id)
        .firstOrNull;
    final pager = _pager;
    return AppScaffold(
      appBar: appTopBar(
        context,
        category == null
            ? context.tr('Categories')
            : categoryLabel(context, category),
      ),
      body: Column(
        children: [
          CompactStoreBar(
            itemCount: pager.items.isEmpty ? null : pager.totalCount,
          ),
          Expanded(child: _body(context, pager)),
        ],
      ),
    );
  }

  Widget _body(BuildContext context, ProductPager pager) {
    return pager.isInitialLoading
        ? GridView.builder(
            padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 24),
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 2,
              mainAxisSpacing: 12,
              crossAxisSpacing: 12,
              mainAxisExtent: 188,
            ),
            itemCount: 4,
            itemBuilder: (_, _) => const SkeletonBox(radius: QC.rCard),
          )
        : pager.error != null && pager.items.isEmpty
        ? ErrorState(error: pager.error!, onRetry: pager.retry)
        : pager.isEmpty
        ? EmptyState(
            icon: Icons.category_outlined,
            title: context.tr('Nothing here yet'),
            message: context.tr('Check back soon.'),
          )
        : CustomScrollView(
            slivers: [
              SliverPadding(
                padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 12),
                sliver: SliverGrid.builder(
                  gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                    crossAxisCount: 2,
                    mainAxisSpacing: 12,
                    crossAxisSpacing: 12,
                    mainAxisExtent: 188,
                  ),
                  itemCount: pager.items.length,
                  itemBuilder: (_, i) => ProductCard(pager.items[i]),
                ),
              ),
              SliverPadding(
                padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24),
                sliver: SliverToBoxAdapter(
                  child: pager.error != null
                      ? ErrorState(
                          error: pager.error!,
                          compact: true,
                          onRetry: pager.retry,
                        )
                      : pager.hasMore
                      ? PillButton(
                          pager.loading
                              ? context.tr('Loading…')
                              : context.tr('Load more'),
                          height: 56,
                          onPressed: pager.loading ? null : pager.loadMore,
                        )
                      : const SizedBox.shrink(),
                ),
              ),
            ],
          );
  }
}
