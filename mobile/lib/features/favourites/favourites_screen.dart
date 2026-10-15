import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import 'favourites_controller.dart';

/// The products the customer saved to come back to.
class FavouritesScreen extends ConsumerWidget {
  const FavouritesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final saved = ref.watch(savedProductsProvider);
    final ids = ref.watch(favouritesProvider);
    return AppScaffold(
      appBar: appTopBar(context, context.tr('Saved items')),
      body: saved.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorState(error: e, onRetry: () => ref.invalidate(savedProductsProvider)),
        data: (products) => products.isEmpty
            ? EmptyState(
                icon: Icons.favorite_border,
                title: context.tr(ids.loaded ? 'Nothing saved yet' : 'Loading…'),
                message: context.tr('Tap the heart on a product to keep it here.'),
                actionLabel: context.tr('Browse products'),
                onAction: () => context.go('/'),
              )
            : GridView.builder(
                padding: const EdgeInsets.fromLTRB(QC.gutter, 8, QC.gutter, 24),
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, mainAxisSpacing: 12, crossAxisSpacing: 12, mainAxisExtent: 188),
                itemCount: products.length,
                itemBuilder: (_, i) => ProductCard(products[i]),
              ),
      ),
    );
  }
}
