import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../data/providers.dart';
import '../../data/repositories/ads_repository.dart';

/// The ads running now at one place, for the store being shopped in. Any failure just means no ad.
final adsProvider = FutureProvider.autoDispose.family<List<AppAd>, String>((
  ref,
  placement,
) async {
  final storeId = ref.watch(selectedStoreProvider).value?.id;
  try {
    return await ref
        .watch(adsRepositoryProvider)
        .forPlacement(placement, storeId: storeId);
  } catch (_) {
    return const [];
  }
});

/// The ads already counted as seen in this run of the app, so one ad is one view however often the screen is drawn.
final seenAdsProvider = NotifierProvider<SeenAdsNotifier, Set<String>>(
  SeenAdsNotifier.new,
);

class SeenAdsNotifier extends Notifier<Set<String>> {
  @override
  Set<String> build() => const {};

  void mark(String id) => state = {...state, id};
}

/// Opens a web page outside the app. Tests replace it.
abstract interface class UrlOpener {
  Future<bool> open(Uri uri);
}

class ExternalUrlOpener implements UrlOpener {
  const ExternalUrlOpener();

  @override
  Future<bool> open(Uri uri) async {
    try {
      return await launchUrl(uri, mode: LaunchMode.externalApplication);
    } catch (_) {
      return false;
    }
  }
}

final urlOpenerProvider = Provider<UrlOpener>(
  (ref) => const ExternalUrlOpener(),
);

/// A banner for [placement] (see [AdPlace]): the picture, a small "Ad" mark and the title over it. Several ads at one place can be swiped.
/// Nothing is drawn while loading, when there is none, or when it fails, so a screen without ads looks exactly as before.
class AdBanner extends ConsumerStatefulWidget {
  const AdBanner(
    this.placement, {
    super.key,
    this.padding = const EdgeInsets.fromLTRB(QC.gutter, 12, QC.gutter, 0),
  });
  final String placement;
  final EdgeInsets padding;

  @override
  ConsumerState<AdBanner> createState() => _AdBannerState();
}

class _AdBannerState extends ConsumerState<AdBanner> {
  int _page = 0;

  /// One view per ad per run of the app, counted when the ad is first on screen.
  void _seen(AppAd ad) {
    if (ref.read(seenAdsProvider).contains(ad.id)) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || ref.read(seenAdsProvider).contains(ad.id)) return;
      ref.read(seenAdsProvider.notifier).mark(ad.id);
      ref.read(adsRepositoryProvider).countView(ad.id).catchError((_) {});
    });
  }

  Future<void> _tap(AppAd ad) async {
    ref.read(adsRepositoryProvider).countTap(ad.id).catchError((_) {});
    final target = ad.targetValue;
    if (target == null || target.isEmpty) return;
    switch (ad.target) {
      case AdTarget.product:
        context.push('/product/$target');
      case AdTarget.category:
        context.push('/category/$target');
      case AdTarget.offer:
        context.push('/offer/$target');
      case AdTarget.url:
        final uri = Uri.tryParse(target);
        // Only a secure web address is opened, whatever the server sent.
        if (uri != null && uri.scheme == 'https')
          await ref.read(urlOpenerProvider).open(uri);
      case AdTarget.none:
        break;
    }
  }

  @override
  Widget build(BuildContext context) {
    final ads =
        ref.watch(adsProvider(widget.placement)).value ?? const <AppAd>[];
    if (ads.isEmpty) return const SizedBox.shrink();
    final page = _page.clamp(0, ads.length - 1);
    _seen(ads[page]);
    return Padding(
      padding: widget.padding,
      child: AspectRatio(
        aspectRatio: 3,
        child: Stack(
          children: [
            PageView.builder(
              itemCount: ads.length,
              onPageChanged: (i) => setState(() => _page = i),
              itemBuilder: (_, i) => _AdCard(ads[i], onTap: () => _tap(ads[i])),
            ),
            if (ads.length > 1)
              Positioned(
                bottom: 6,
                left: 0,
                right: 0,
                child: IgnorePointer(
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      for (var i = 0; i < ads.length; i++)
                        Container(
                          width: i == page ? 16 : 6,
                          height: 6,
                          margin: const EdgeInsets.symmetric(horizontal: 2),
                          decoration: BoxDecoration(
                            color: Colors.white.withValues(
                              alpha: i == page ? 1 : .55,
                            ),
                            borderRadius: BorderRadius.circular(QC.rPill),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _AdCard extends StatelessWidget {
  const _AdCard(this.ad, {required this.onTap});
  final AppAd ad;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Semantics(
      button: ad.target != AdTarget.none,
      label: '${context.tr('Ad')}: ${ad.title ?? ''}'.trim(),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 1),
          child: ClipRRect(
            borderRadius: BorderRadius.circular(QC.rCard),
            child: Stack(
              fit: StackFit.expand,
              children: [
                CachedNetworkImage(
                  imageUrl: ad.imageUrl,
                  fit: BoxFit.cover,
                  placeholder: (_, _) => ColoredBox(color: p.card),
                  errorWidget: (_, _, _) => ColoredBox(
                    color: p.card,
                    child: Icon(LucideIcons.megaphone, color: p.mutedOnCard),
                  ),
                ),
                if (ad.title != null)
                  Positioned(
                    left: 0,
                    right: 0,
                    bottom: 0,
                    child: Container(
                      padding: const EdgeInsets.fromLTRB(14, 18, 14, 10),
                      decoration: const BoxDecoration(
                        gradient: LinearGradient(
                          begin: Alignment.topCenter,
                          end: Alignment.bottomCenter,
                          colors: [Colors.transparent, Color(0xB3141118)],
                        ),
                      ),
                      child: ExcludeSemantics(
                        child: Text(
                          ad.title!,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            color: Colors.white,
                            fontSize: 15,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                      ),
                    ),
                  ),
                Positioned(
                  top: 8,
                  right: 8,
                  child: ExcludeSemantics(
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 7,
                        vertical: 2,
                      ),
                      decoration: BoxDecoration(
                        color: const Color(0xCCFFFFFF),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Text(
                        context.tr('Ad'),
                        style: const TextStyle(
                          color: Pal.ink,
                          fontSize: 10,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
