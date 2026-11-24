import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/core/l10n.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/ads_repository.dart';
import 'package:quickcart_customer/features/ads/ad_banner.dart';

class _FakeAds implements AdsRepository {
  _FakeAds(this.ads);
  List<AppAd> ads;
  Object? failWith;
  final views = <String>[];
  final taps = <String>[];
  final asked = <(String, String?)>[];

  @override
  Future<List<AppAd>> forPlacement(String placement, {String? storeId}) async {
    asked.add((placement, storeId));
    if (failWith != null) throw failWith!;
    return ads.where((a) => a.placement == placement).toList();
  }

  @override
  Future<void> countView(String id) async => views.add(id);

  @override
  Future<void> countTap(String id) async => taps.add(id);
}

class _FakeOpener implements UrlOpener {
  final opened = <Uri>[];

  @override
  Future<bool> open(Uri uri) async {
    opened.add(uri);
    return true;
  }
}

AppAd _ad(String id, {String place = AdPlace.home, String? title = 'Diwali sale', AdTarget target = AdTarget.none, String? value}) =>
    AppAd(id: id, placement: place, imageUrl: 'http://img.test/$id.png', title: title, target: target, targetValue: value);

Future<(_FakeAds, _FakeOpener, List<String>)> _pump(WidgetTester tester, List<AppAd> ads, {String place = AdPlace.home, Locale locale = const Locale('en')}) async {
  final repo = _FakeAds(ads);
  final opener = _FakeOpener();
  final opened = <String>[];
  final router = GoRouter(routes: [
    GoRoute(path: '/', builder: (_, _) => Scaffold(body: Center(child: SizedBox(width: 360, child: AdBanner(place))))),
    for (final path in ['/product/:id', '/category/:id', '/offer/:id'])
      GoRoute(
        path: path,
        builder: (_, s) {
          opened.add(s.uri.toString());
          return const Scaffold(body: Text('opened'));
        },
      ),
  ]);
  await tester.pumpWidget(ProviderScope(
    overrides: [adsRepositoryProvider.overrideWithValue(repo), urlOpenerProvider.overrideWithValue(opener)],
    child: MaterialApp.router(
      routerConfig: router,
      theme: QC.theme(Brightness.light),
      locale: locale,
      supportedLocales: AppStrings.supported,
      localizationsDelegates: const [AppStrings.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
    ),
  ));
  await tester.pump();
  await tester.pump();
  return (repo, opener, opened);
}

void main() {
  group('reading an ad from the API', () {
    test('a path from our server becomes a whole link, the target kind is read, and a blank title is no title', () {
      final ad = AppAd.fromJson({'id': 'a1', 'placement': 'cart', 'title': '  ', 'imageUrl': '/media/offers/a.png', 'targetType': 'Category', 'target': 'c1'}, imageBase: 'http://host');
      expect((ad.id, ad.placement, ad.imageUrl, ad.title, ad.target, ad.targetValue), ('a1', 'cart', 'http://host/media/offers/a.png', null, AdTarget.category, 'c1'));
      expect(adTargetOf('Product'), AdTarget.product);
      expect(adTargetOf('Offer'), AdTarget.offer);
      expect(adTargetOf('Url'), AdTarget.url);
      expect(adTargetOf('Telephone'), AdTarget.none);
      expect(adTargetOf(null), AdTarget.none);
    });

    test('the place keys are the seven the server accepts', () {
      expect([AdPlace.home, AdPlace.homeMiddle, AdPlace.categories, AdPlace.search, AdPlace.cart, AdPlace.order, AdPlace.product], ['home', 'home_middle', 'categories', 'search', 'cart', 'order', 'product']);
    });

    test('the offline demo has no ads and counting does nothing', () async {
      final local = LocalAdsRepository();
      expect(await local.forPlacement('home'), isEmpty);
      await local.countView('x');
      await local.countTap('x');
    });
  });

  group('the banner', () {
    testWidgets('shows the title and a small Ad mark for the ads at its place only', (tester) async {
      final (repo, _, _) = await _pump(tester, [_ad('a1'), _ad('a2', place: AdPlace.cart, title: 'Cart ad')]);

      expect(find.text('Diwali sale'), findsOneWidget);
      expect(find.text('Cart ad'), findsNothing);
      expect(find.text('Ad'), findsOneWidget);
      expect(repo.asked.first.$1, 'home');
    });

    testWidgets('draws nothing when there is no ad, while loading, or when loading fails', (tester) async {
      await _pump(tester, const []);
      expect(find.byType(AspectRatio), findsNothing);

      await tester.pumpWidget(const SizedBox());
      final repo = _FakeAds([_ad('a1')])..failWith = Exception('offline');
      await tester.pumpWidget(ProviderScope(overrides: [adsRepositoryProvider.overrideWithValue(repo)], child: MaterialApp(theme: QC.theme(Brightness.light), home: const Scaffold(body: AdBanner(AdPlace.home)))));
      await tester.pump();
      await tester.pump();
      expect(find.byType(AspectRatio), findsNothing);
      expect(tester.takeException(), isNull);
    });

    testWidgets('counts one view when the ad is first seen, and not again when drawn again', (tester) async {
      final (repo, _, _) = await _pump(tester, [_ad('a1')]);
      expect(repo.views, ['a1']);

      await tester.pump(const Duration(seconds: 1));
      await tester.pump();
      expect(repo.views, ['a1']);
    });

    testWidgets('a tap counts, then opens a product, a category or an offer', (tester) async {
      for (final (target, value, path) in [(AdTarget.product, 'p1', '/product/p1'), (AdTarget.category, 'c1', '/category/c1'), (AdTarget.offer, 'o1', '/offer/o1')]) {
        final (repo, _, opened) = await _pump(tester, [_ad('a1', target: target, value: value)]);
        await tester.tap(find.text('Diwali sale'));
        await tester.pumpAndSettle();
        expect(repo.taps, ['a1'], reason: '$target counted');
        expect(opened, [path]);
        await tester.pumpWidget(const SizedBox());
      }
    });

    testWidgets('a web page is opened outside the app, but only a secure one', (tester) async {
      var (repo, opener, _) = await _pump(tester, [_ad('a1', target: AdTarget.url, value: 'https://example.com/sale')]);
      await tester.tap(find.text('Diwali sale'));
      await tester.pumpAndSettle();
      expect(opener.opened.map((u) => u.toString()), ['https://example.com/sale']);
      expect(repo.taps, ['a1']);

      await tester.pumpWidget(const SizedBox());
      (repo, opener, _) = await _pump(tester, [_ad('a2', target: AdTarget.url, value: 'http://example.com/plain'), _ad('a3', target: AdTarget.url, value: 'javascript:alert(1)')]);
      await tester.tap(find.byType(AdBanner));
      await tester.pumpAndSettle();
      expect(opener.opened, isEmpty, reason: 'plain http and script links are never opened');
    });

    testWidgets('an ad that opens nothing still counts the tap and goes nowhere', (tester) async {
      final (repo, opener, opened) = await _pump(tester, [_ad('a1')]);
      await tester.tap(find.text('Diwali sale'));
      await tester.pumpAndSettle();
      expect(repo.taps, ['a1']);
      expect(opened, isEmpty);
      expect(opener.opened, isEmpty);
    });

    testWidgets('several ads can be swiped, and the one in view is the one counted', (tester) async {
      final (repo, _, _) = await _pump(tester, [_ad('a1', title: 'First'), _ad('a2', title: 'Second')]);
      expect(find.text('First'), findsOneWidget);
      expect(repo.views, ['a1']);

      await tester.drag(find.byType(PageView), const Offset(-400, 0));
      await tester.pumpAndSettle();
      expect(find.text('Second'), findsOneWidget);
      expect(repo.views, ['a1', 'a2']);
    });

    testWidgets('has a screen reader label with the title, and the Ad mark in Marathi', (tester) async {
      final handle = tester.ensureSemantics();
      await _pump(tester, [_ad('a1', target: AdTarget.product, value: 'p1')]);
      expect(find.bySemanticsLabel('Ad: Diwali sale'), findsOneWidget);
      handle.dispose();

      await tester.pumpWidget(const SizedBox());
      await _pump(tester, [_ad('a1')], locale: const Locale('mr'));
      expect(find.text('जाहिरात'), findsOneWidget);
    });
  });
}
