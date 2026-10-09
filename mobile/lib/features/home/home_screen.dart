import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/polling.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../address/address_controller.dart';
import '../auth/auth_controller.dart';
import '../festival/festival_widgets.dart';
import '../notifications/notifications_controller.dart';
import '../orders/orders_controller.dart';
import '../stores/store_widgets.dart';
import 'product_pager.dart';

enum _Mode { deals, all }

class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});
  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  static const _searchDebounce = Duration(milliseconds: 350);

  final _controller = TextEditingController();
  Timer? _debounce;
  String _search = '';
  _Mode? _mode;
  ProductPager? _pager;

  bool get _searching => _search.trim().isNotEmpty || _mode != null;

  @override
  void dispose() {
    _debounce?.cancel();
    _pager?.dispose();
    _controller.dispose();
    super.dispose();
  }

  /// Replaces the results pager for the current search / mode and loads its first page.
  void _resetPager() {
    _pager?.dispose();
    _pager = null;
    if (!_searching) return;
    final pager = ProductPager(ref.read(catalogRepositoryProvider), search: _search.trim().isEmpty ? null : _search.trim(), storeId: ref.read(storeIdResolverProvider))..addListener(() => mounted ? setState(() {}) : null);
    _pager = pager;
    pager.loadFirst();
  }

  void _onSearchChanged(String value) {
    setState(() => _search = value);
    _debounce?.cancel();
    _debounce = Timer(_searchDebounce, () => setState(_resetPager));
  }

  void _setMode(_Mode? mode) {
    _debounce?.cancel();
    setState(() {
      _mode = mode;
      _resetPager();
    });
  }

  String _greeting(BuildContext context) {
    final h = DateTime.now().hour;
    final key = h < 12 ? 'Good morning' : (h < 17 ? 'Good afternoon' : 'Good evening');
    final name = firstNameOf(ref.watch(authProvider)) ?? context.tr('friend');
    return '${context.tr(key)}, $name';
  }

  @override
  Widget build(BuildContext context) {
    // Another store means another catalogue: search results start over.
    ref.listen(selectedStoreProvider, (before, after) {
      if (before?.value?.id != after.value?.id && _searching) setState(_resetPager);
    });
    // The bell is checked every minute while Home is in front (not in the background, not on another tab).
    return Polling(
      interval: const Duration(seconds: 60),
      active: ref.watch(authProvider.select((a) => a.isSignedIn)) && TickerMode.valuesOf(context).enabled,
      onTick: ref.read(unreadCountProvider.notifier).poll,
      child: Stack(children: [
      SafeArea(
      bottom: false,
      child: CustomScrollView(slivers: [
        const SliverPadding(padding: EdgeInsets.fromLTRB(QC.gutter, 12, QC.gutter, 0), sliver: SliverToBoxAdapter(child: _Header())),
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(QC.gutter, 12, QC.gutter, 12),
          sliver: SliverToBoxAdapter(child: Text(_greeting(context), style: const TextStyle(color: Colors.white, fontSize: 30, fontWeight: FontWeight.w900, height: 1.1))),
        ),
        if (!_searching) const SliverPadding(padding: EdgeInsets.symmetric(horizontal: QC.gutter), sliver: SliverToBoxAdapter(child: FestivalBanner())),
        SliverPadding(padding: const EdgeInsets.symmetric(horizontal: QC.gutter), sliver: SliverToBoxAdapter(child: _searchBar(context))),
        const SliverPadding(padding: EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 0), sliver: SliverToBoxAdapter(child: StoreBar())),
        if (_searching) ..._results(context) else ..._browse(context),
      ]),
      ),
      // Falling lights or petals during a festival, over everything and never taking a tap.
      const Positioned.fill(child: FestivalParticles()),
    ]),
    );
  }

  List<Widget> _browse(BuildContext context) {
    final popular = ref.watch(popularProductsProvider);
    final maxOff = (popular.value ?? const <Product>[]).fold<int>(0, (m, x) => x.discountPct > m ? x.discountPct : m);
    return [
      const SliverToBoxAdapter(child: StoresRail()),
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 0),
        sliver: SliverToBoxAdapter(child: _HeroRow(maxOff: maxOff, onOffers: () => _setMode(maxOff > 0 ? _Mode.deals : _Mode.all))),
      ),
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 8),
        sliver: SliverToBoxAdapter(child: SectionHeader(context.tr('Shop by category'), action: context.tr('See all'), onAction: () => context.go('/categories'))),
      ),
      const SliverPadding(padding: EdgeInsets.symmetric(horizontal: QC.gutter), sliver: SliverToBoxAdapter(child: _CategoryRow())),
      SliverPadding(padding: const EdgeInsets.fromLTRB(QC.gutter, 14, QC.gutter, 8), sliver: SliverToBoxAdapter(child: SectionHeader(context.tr('Popular near you')))),
      SliverToBoxAdapter(
        child: SizedBox(
          height: 188,
          child: popular.when(
            loading: () => ListView.separated(
              scrollDirection: Axis.horizontal,
              physics: const NeverScrollableScrollPhysics(),
              padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
              itemCount: 3,
              separatorBuilder: (_, _) => const SizedBox(width: 12),
              itemBuilder: (_, _) => const SkeletonBox(width: 160, height: 188, radius: QC.rCard),
            ),
            error: (e, _) => Padding(padding: const EdgeInsets.symmetric(horizontal: QC.gutter), child: Align(alignment: Alignment.topCenter, child: ErrorState(error: e, compact: true, onRetry: () => ref.invalidate(popularProductsProvider)))),
            data: (items) => items.isEmpty
                ? Center(child: Text(context.tr('Nothing here yet'), style: const TextStyle(color: Pal.mutedOnGround)))
                : ListView.separated(
                    scrollDirection: Axis.horizontal,
                    padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
                    itemCount: items.length,
                    separatorBuilder: (_, _) => const SizedBox(width: 12),
                    itemBuilder: (_, i) => SizedBox(width: 160, child: ProductCard(items[i])),
                  ),
          ),
        ),
      ),
      const SliverToBoxAdapter(child: SizedBox(height: 24)),
    ];
  }

  List<Widget> _results(BuildContext context) {
    final pager = _pager;
    final deals = _mode == _Mode.deals;
    final items = pager == null ? const <Product>[] : (deals ? pager.items.where((p) => p.discountPct > 0).toList() : pager.items);
    final title = deals ? context.tr('Today’s deals') : (_mode == _Mode.all ? context.tr('All items') : context.tr('{n} results', {'n': '${pager?.totalCount ?? 0}'}));
    return [
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(QC.gutter, 24, QC.gutter, 12),
        sliver: SliverToBoxAdapter(child: SectionHeader(title, action: _mode != null ? context.tr('Clear') : null, onAction: () => _setMode(null))),
      ),
      if (pager == null || pager.isInitialLoading || (_debounce?.isActive ?? false))
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24),
          sliver: SliverGrid.builder(
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, mainAxisSpacing: 12, crossAxisSpacing: 12, mainAxisExtent: 188),
            itemCount: 4,
            itemBuilder: (_, _) => const SkeletonBox(height: 188, radius: QC.rCard),
          ),
        )
      else if (pager.error != null && pager.items.isEmpty)
        SliverFillRemaining(hasScrollBody: false, child: ErrorState(error: pager.error!, onRetry: pager.retry))
      else if (items.isEmpty && !pager.hasMore)
        SliverFillRemaining(hasScrollBody: false, child: EmptyState(icon: Icons.search_off, title: context.tr('Nothing found'), message: context.tr('Try a different search.')))
      else ...[
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 12),
          sliver: SliverGrid.builder(
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, mainAxisSpacing: 12, crossAxisSpacing: 12, mainAxisExtent: 188),
            itemCount: items.length,
            itemBuilder: (_, i) => ProductCard(items[i]),
          ),
        ),
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24),
          sliver: SliverToBoxAdapter(
            child: pager.error != null
                ? ErrorState(error: pager.error!, compact: true, onRetry: pager.retry)
                : pager.hasMore
                    ? PillButton(pager.loading ? context.tr('Loading…') : context.tr('Load more'), height: 56, onPressed: pager.loading ? null : pager.loadMore)
                    : const SizedBox.shrink(),
          ),
        ),
      ],
    ];
  }

  Widget _searchBar(BuildContext context) {
    final p = context.pal;
    final store = ref.watch(selectedStoreProvider).value;
    return Container(
      height: 60,
      padding: const EdgeInsets.only(left: 20, right: 6),
      decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rPill)),
      child: Row(children: [
        Icon(Icons.search, color: p.mutedOnCard),
        const SizedBox(width: 12),
        Expanded(
          child: TextField(
            controller: _controller,
            onChanged: _onSearchChanged,
            textInputAction: TextInputAction.search,
            style: TextStyle(color: p.onCard, fontSize: 16),
            decoration: InputDecoration(hintText: store == null ? context.tr('Search tomato, milk, atta…') : context.tr('Search in {store}', {'store': store.name}), border: InputBorder.none, enabledBorder: InputBorder.none, focusedBorder: InputBorder.none, filled: false, contentPadding: EdgeInsets.zero),
          ),
        ),
        if (_search.isNotEmpty)
          IconButton(
            tooltip: context.tr('Clear search'),
            icon: Icon(Icons.close, color: p.mutedOnCard),
            onPressed: () {
              _controller.clear();
              _onSearchChanged('');
            },
          ),
        Tooltip(
          message: context.tr('Voice search'),
          child: GestureDetector(
            onTap: () => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Voice search is coming soon')))),
            child: Container(width: 48, height: 48, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.mic_none_rounded, color: Pal.ink)),
          ),
        ),
      ]),
    );
  }
}

class _Header extends ConsumerWidget {
  const _Header();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final place = ref.watch(deliveryPlaceProvider);
    final title = place == null ? context.tr('Choose location') : '${context.tr(place.label)} · ${place.area}';
    return Row(children: [
      Expanded(
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => context.push('/addresses'),
          child: Row(children: [
            Container(width: 48, height: 48, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.location_on_outlined, color: Pal.ink)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(context.tr('DELIVER TO'), style: const TextStyle(fontSize: 12, letterSpacing: 1.6, color: Pal.mutedOnGround, fontWeight: FontWeight.w800)),
                Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16, color: Colors.white)),
              ]),
            ),
          ]),
        ),
      ),
      const SizedBox(width: 12),
      CircleIconButton(
        icon: Icons.notifications_none_rounded,
        tooltip: context.tr('Notifications'),
        badgeCount: ref.watch(unreadCountProvider).value ?? 0,
        onPressed: () => context.push('/notifications'),
      ),
    ]);
  }
}

class _HeroRow extends ConsumerWidget {
  const _HeroRow({required this.maxOff, required this.onOffers});
  final int maxOff;
  final VoidCallback onOffers;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final last = ref.watch(ordersProvider).value?.firstOrNull;
    return SizedBox(
      height: 76,
      child: Row(children: [
        Expanded(
          child: maxOff > 0
              ? _Tile(color: Pal.pink, onTap: onOffers, title: context.tr('Today’s deals'), subtitle: context.tr('Up to {n}% off', {'n': '$maxOff'}), fg: Pal.ink)
              : _Tile(color: Pal.pink, onTap: onOffers, title: context.tr('All items'), subtitle: context.tr('Browse the full catalogue'), fg: Pal.ink),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: _Tile(
                color: p.soft,
                fg: p.onSoft,
                onTap: () => last == null ? context.go('/categories') : context.push('/order/${last.id}'),
                title: context.tr('Buy again'),
                subtitle: last == null ? context.tr('Your past orders') : context.tr('{n} items · ₹{amt}', {'n': '${last.itemCount}', 'amt': amount(last.total)}),
              ),
        ),
      ]),
    );
  }
}

class _Tile extends StatelessWidget {
  const _Tile({required this.color, required this.fg, required this.title, required this.subtitle, required this.onTap});
  final Color color, fg;
  final String title, subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => GestureDetector(
        onTap: onTap,
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.symmetric(horizontal: 18),
          decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(QC.rCard)),
          child: Column(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: fg, fontSize: 17, fontWeight: FontWeight.w900)),
            const SizedBox(height: 2),
            Text(subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: fg, fontSize: 12)),
          ]),
        ),
      );
}

class _CategoryRow extends ConsumerWidget {
  const _CategoryRow();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final cats = ref.watch(categoriesProvider);
    return cats.when(
      loading: () => Row(children: [for (var i = 0; i < 4; i++) ...[if (i > 0) const SizedBox(width: 10), const Expanded(child: SkeletonBox(height: 76, radius: QC.rCard))]]),
      error: (e, _) => ErrorState(error: e, compact: true, onRetry: () => ref.invalidate(categoriesProvider)),
      data: (list) {
        final shown = list.take(4).toList();
        return Row(children: [
          for (var i = 0; i < 4; i++) ...[
            if (i > 0) const SizedBox(width: 10),
            Expanded(
              child: i >= shown.length
                  ? const SizedBox(height: 76)
                  : GestureDetector(
                      onTap: () => context.push('/category/${shown[i].id}'),
                      child: Container(
                        height: 76,
                        decoration: BoxDecoration(color: p.tiles[i], borderRadius: BorderRadius.circular(QC.rCard)),
                        child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                          Icon(shown[i].icon, size: 28, color: Pal.ink),
                          const SizedBox(height: 6),
                          Text(categoryLabel(context, shown[i]), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w800, fontSize: 13)),
                        ]),
                      ),
                    ),
            ),
          ],
        ]);
      },
    );
  }
}
