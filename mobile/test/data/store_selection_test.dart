import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/auth_fakes.dart';
import '../support/fake_adapter.dart';

const _s1 = '30000000-0000-0000-0000-000000000001';
const _s2 = '30000000-0000-0000-0000-000000000002';

Map<String, dynamic> _store(String id, String name, double km, int min, {String address = '12 Marine Drive'}) =>
    {'id': id, 'name': name, 'address': address, 'distanceKm': km, 'estimatedMinutes': min, 'serviceRadiusKm': 8};

void main() {
  group('ApiCatalogRepository stores and store catalogue', () {
    late FakeAdapter adapter;
    late ApiCatalogRepository repo;
    setUp(() {
      adapter = FakeAdapter();
      repo = ApiCatalogRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));
    });

    test('stores sends the location and maps name, address, distance and estimate', () async {
      adapter.replyJson({
        'success': true,
        'data': [_store(_s1, 'Harbor Point Dark Store', 1.2, 11), _store(_s2, 'Cedar Market Hub', 4.6, 20, address: '44 Cedar Avenue')]
      });

      final stores = await repo.stores(latitude: 19.06, longitude: 72.83);

      expect(adapter.requests.single.path, '/api/catalog/stores');
      expect(adapter.requests.single.queryParameters, {'latitude': 19.06, 'longitude': 72.83});
      expect(stores.map((s) => s.name), ['Harbor Point Dark Store', 'Cedar Market Hub']);
      expect((stores[1].address, stores[1].distanceKm, stores[1].estimatedMinutes), ('44 Cedar Avenue', 4.6, 20));
    });

    test('an empty list means nobody delivers there', () async {
      adapter.replyJson({'success': true, 'data': []});
      expect(await repo.stores(latitude: 28.6, longitude: 77.2), isEmpty);
    });

    test('carriedOnly is only sent together with a store', () async {
      adapter
        ..replyJson({'success': true, 'data': []})
        ..replyJson({'success': true, 'data': {'items': [], 'page': 1, 'pageSize': 20, 'totalCount': 0, 'hasMore': false}})
        ..replyJson({'success': true, 'data': {'items': [], 'page': 1, 'pageSize': 20, 'totalCount': 0, 'hasMore': false}});

      await repo.products(storeId: _s1, carriedOnly: true);
      expect(adapter.requests.last.queryParameters, containsPair('carriedOnly', true));
      await repo.products(carriedOnly: true);
      expect(adapter.requests.last.queryParameters.containsKey('carriedOnly'), isFalse);
    });

    test('categories are asked for per store and kept per store', () async {
      Map<String, dynamic> body(List<String> names) => {'success': true, 'data': [for (final n in names) {'id': '$n-id', 'name': n, 'parentCategoryId': null, 'isActive': true}]};
      adapter
        ..replyJson(body(['Vegetables', 'Dairy']))
        ..replyJson(body(['Vegetables']));

      final harbor = await repo.categories(storeId: _s1);
      final cedar = await repo.categories(storeId: _s2);
      final harborAgain = await repo.categories(storeId: _s1);

      expect(harbor.map((c) => c.label), ['Vegetables', 'Dairy']);
      expect(cedar.map((c) => c.label), ['Vegetables']);
      expect(harborAgain, same(harbor));
      expect(adapter.requests.map((r) => r.queryParameters['storeId']), [_s1, _s2]);
    });
  });

  group('selected store', () {
    Future<ProviderContainer> container({String? saved, List<NearestStore> stores = const []}) async {
      SharedPreferences.setMockInitialValues({'selected_store_id': ?saved});
      final c = ProviderContainer(retry: noAutomaticRetry, overrides: [
        sharedPrefsProvider.overrideWithValue(await SharedPreferences.getInstance()),
        appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test')),
        serviceableStoresProvider.overrideWith((ref) async => stores),
      ]);
      addTearDown(c.dispose);
      return c;
    }

    const near = NearestStore(id: 'a', name: 'Near', distanceKm: 1, estimatedMinutes: 10);
    const far = NearestStore(id: 'b', name: 'Far', distanceKm: 5, estimatedMinutes: 20);

    test('the nearest store is used until the customer chooses another', () async {
      final c = await container(stores: [near, far]);
      expect((await c.read(selectedStoreProvider.future))?.id, 'a');
    });

    test('a saved choice that still delivers here is kept', () async {
      final c = await container(saved: 'b', stores: [near, far]);
      expect((await c.read(selectedStoreProvider.future))?.id, 'b');
    });

    test('a saved store that no longer delivers here falls back to the nearest, without forgetting the choice', () async {
      final c = await container(saved: 'gone', stores: [near, far]);
      expect((await c.read(selectedStoreProvider.future))?.id, 'a');
      expect(c.read(selectedStoreIdProvider), 'gone');
    });

    test('nobody delivering here means no selected store', () async {
      final c = await container(saved: 'a');
      expect(await c.read(selectedStoreProvider.future), isNull);
    });

    test('choosing a store is remembered across launches', () async {
      final c = await container(stores: [near, far]);
      c.read(selectedStoreIdProvider.notifier).select('b');
      expect((await SharedPreferences.getInstance()).getString('selected_store_id'), 'b');
      expect((await c.read(selectedStoreProvider.future))?.id, 'b');
    });
  });

  group('store screen', () {
    Future<ProviderContainer> pump(WidgetTester tester, {Map<String, Object> prefs = const {}, List<Override> overrides = const []}) async {
      SharedPreferences.setMockInitialValues(prefs);
      final sp = await SharedPreferences.getInstance();
      tester.view.physicalSize = const Size(390 * 3, 844 * 3);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      final auth = AuthHarness();
      await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
        sharedPrefsProvider.overrideWithValue(sp),
        sessionStoreProvider.overrideWithValue(MemorySessionStore()),
        authRepositoryProvider.overrideWithValue(auth.repo),
        appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid', useSeedData: true)),
        ...overrides,
      ], child: const QuickCartApp()));
      await tester.pumpAndSettle();
      return ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
    }

    Future<void> openSheet(WidgetTester tester) async {
      await tester.tap(find.text('Change'));
      await tester.pumpAndSettle();
    }

    Finder inSheet(String text) => find.descendant(of: find.byType(BottomSheet), matching: find.text(text));

    testWidgets('Home always shows which store you shop from, and the stores that deliver here', (tester) async {
      await pump(tester);

      expect(find.text('SHOPPING FROM'), findsOneWidget);
      expect(find.textContaining('arrives in'), findsOneWidget);
      expect(find.text('Search in Sharma Fresh Mart'), findsOneWidget);
      expect(find.text('Stores that deliver to you'), findsOneWidget);
      expect(find.text('See all 2'), findsOneWidget);
      expect(find.text('Sharma Fresh Mart'), findsNWidgets(2), reason: 'the bar and the rail');
      expect(find.text('Green Basket Express'), findsOneWidget);
      expect(find.text('Nearest'), findsOneWidget);
    });

    testWidgets('tapping a store in the rail switches to it, remembers it, and the bar follows', (tester) async {
      await pump(tester);

      await tester.tap(find.text('Green Basket Express'));
      await tester.pumpAndSettle();

      expect(find.text('Green Basket Express'), findsNWidgets(2));
      expect(find.text('Search in Green Basket Express'), findsOneWidget);
      expect((await SharedPreferences.getInstance()).getString('selected_store_id'), 'seed-store-2');
    });

    testWidgets('Change opens the sheet; choosing a store then the button switches', (tester) async {
      final c = await pump(tester);
      await openSheet(tester);
      expect(find.text('Choose your store'), findsOneWidget);
      expect(find.text('Current store', findRichText: true), findsNothing);
      expect(find.textContaining('Current store'), findsOneWidget);
      expect(find.textContaining('Switch to'), findsNothing, reason: 'nothing to switch to until another store is picked');

      await tester.tap(inSheet('Green Basket Express'));
      await tester.pumpAndSettle();
      expect(find.text('Switch to Green Basket Express'), findsOneWidget);
      expect(find.textContaining('empty your cart'), findsNothing, reason: 'no warning with an empty cart');
      await tester.tap(find.text('Switch to Green Basket Express'));
      await tester.pumpAndSettle();

      expect(find.byType(BottomSheet), findsNothing);
      expect(c.read(selectedStoreIdProvider), 'seed-store-2');
      expect(find.text('Search in Green Basket Express'), findsOneWidget);
    });

    testWidgets('with items in the cart the sheet warns, names what happens, and empties the cart on confirm', (tester) async {
      final c = await pump(tester);
      c.read(cartProvider.notifier).add('p1');
      await openSheet(tester);

      await tester.tap(inSheet('Green Basket Express'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Switching store will empty your cart'), findsOneWidget);
      expect(find.text('Switch to Green Basket Express and empty cart'), findsOneWidget);

      await tester.tap(find.text('Switch to Green Basket Express and empty cart'));
      await tester.pumpAndSettle();

      expect(c.read(cartProvider), isEmpty);
      expect(c.read(selectedStoreIdProvider), 'seed-store-2');
    });

    testWidgets('Keep shopping closes the sheet and changes nothing', (tester) async {
      final c = await pump(tester);
      c.read(cartProvider.notifier).add('p1');
      await openSheet(tester);
      await tester.tap(inSheet('Green Basket Express'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Keep shopping at Sharma Fresh Mart'));
      await tester.pumpAndSettle();

      expect(find.byType(BottomSheet), findsNothing);
      expect(c.read(cartProvider), {'p1': 1});
      expect(c.read(selectedStoreIdProvider), isNull);
    });

    testWidgets('a rail tap with items in the cart asks first; keeping shopping changes nothing', (tester) async {
      final c = await pump(tester);
      c.read(cartProvider.notifier).add('p1');

      await tester.tap(find.text('Green Basket Express'));
      await tester.pumpAndSettle();
      expect(find.text('Switch to Green Basket Express?'), findsOneWidget);
      await tester.tap(find.text('Keep shopping'));
      await tester.pumpAndSettle();

      expect(c.read(cartProvider), {'p1': 1});
      expect(c.read(selectedStoreIdProvider), isNull);
    });

    testWidgets('confirming the rail dialog empties the cart and selects the store', (tester) async {
      final c = await pump(tester);
      c.read(cartProvider.notifier).add('p1');

      await tester.tap(find.text('Green Basket Express'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Switch store'));
      await tester.pumpAndSettle();

      expect(c.read(cartProvider), isEmpty);
      expect(c.read(selectedStoreIdProvider), 'seed-store-2');
    });

    testWidgets('picking the store that is already selected does not ask, even with a full cart', (tester) async {
      final c = await pump(tester);
      c.read(cartProvider.notifier).add('p1');

      await tester.tap(find.descendant(of: find.byType(ListView).first, matching: find.text('Sharma Fresh Mart')).first);
      await tester.pumpAndSettle();

      expect(find.textContaining('Switch to'), findsNothing);
      expect(c.read(cartProvider), {'p1': 1});
    });

    testWidgets('See all opens the list with addresses, delivery range, and a button that makes the switch', (tester) async {
      final c = await pump(tester);
      await tester.tap(find.text('See all 2'));
      await tester.pumpAndSettle();

      expect(find.text('Choose a store'), findsOneWidget);
      expect(find.text('12 Marine Drive'), findsOneWidget);
      expect(find.text('44 Cedar Avenue'), findsOneWidget);
      expect(find.text('1.2 km away · delivers within 8 km'), findsOneWidget);
      expect(find.byType(LinearProgressIndicator), findsNWidgets(2));
      expect(find.text('Shop from Sharma Fresh Mart'), findsOneWidget, reason: 'the current store is marked and offered');

      await tester.tap(find.text('Green Basket Express'));
      await tester.pumpAndSettle();
      expect(c.read(selectedStoreIdProvider), isNull, reason: 'picking only marks the store');
      await tester.tap(find.text('Shop from Green Basket Express'));
      await tester.pumpAndSettle();

      expect(c.read(selectedStoreIdProvider), 'seed-store-2');
      expect(find.text('Choose a store'), findsNothing);
    });

    testWidgets('the list can be ordered by speed or name', (tester) async {
      await pump(tester, overrides: [
        serviceableStoresProvider.overrideWith((ref) async => const [
              NearestStore(id: 'a', name: 'Zed Mart', distanceKm: 1, estimatedMinutes: 20, address: 'A'),
              NearestStore(id: 'b', name: 'Alpha Hub', distanceKm: 2, estimatedMinutes: 10, address: 'B'),
              NearestStore(id: 'c', name: 'Mid Store', distanceKm: 3, estimatedMinutes: 15, address: 'C'),
            ]),
      ]);
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/stores');
      await tester.pumpAndSettle();

      double top(String name) => tester.getTopLeft(find.text(name).last).dy;
      expect(top('Zed Mart') < top('Alpha Hub'), isTrue, reason: 'nearest first by default');

      await tester.tap(find.text('Fastest'));
      await tester.pumpAndSettle();
      expect([top('Alpha Hub'), top('Mid Store'), top('Zed Mart')], orderedEquals([...[top('Alpha Hub'), top('Mid Store'), top('Zed Mart')]]..sort()));

      await tester.tap(find.text('A to Z'));
      await tester.pumpAndSettle();
      expect(top('Alpha Hub') < top('Mid Store') && top('Mid Store') < top('Zed Mart'), isTrue);
    });

    testWidgets('a store that reports no delivery range shows no range bar', (tester) async {
      await pump(tester, overrides: [
        serviceableStoresProvider.overrideWith((ref) async => const [NearestStore(id: 'a', name: 'Solo', distanceKm: 1, estimatedMinutes: 10)]),
      ]);
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/stores');
      await tester.pumpAndSettle();
      expect(find.byType(LinearProgressIndicator), findsNothing);
    });

    testWidgets('browsing a category keeps the store visible with how many items it carries', (tester) async {
      await pump(tester);
      await tester.scrollUntilVisible(find.text('Dairy'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Dairy').first);
      await tester.pumpAndSettle();

      expect(find.text('Sharma Fresh Mart'), findsOneWidget);
      expect(find.textContaining('items'), findsWidgets);
      await tester.tap(find.text('Change'));
      await tester.pumpAndSettle();
      expect(find.text('Choose your store'), findsOneWidget);
    });

    testWidgets('when no store delivers here Home says so and the list explains', (tester) async {
      await pump(tester, overrides: [serviceableStoresProvider.overrideWith((ref) async => const <NearestStore>[])]);
      expect(find.text('We do not deliver here yet'), findsOneWidget);
      expect(find.text('Stores that deliver to you'), findsNothing);

      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/stores');
      await tester.pumpAndSettle();
      expect(find.text('No store delivers to this address. Try another address.'), findsOneWidget);
    });

    testWidgets('a failed store lookup shows on Home and tapping it retries', (tester) async {
      var calls = 0;
      await pump(tester, overrides: [
        serviceableStoresProvider.overrideWith((ref) async {
          calls++;
          if (calls == 1) throw StateError('boom');
          return const [NearestStore(id: 's1', name: 'Retry Mart', distanceKm: 1, estimatedMinutes: 9)];
        }),
      ]);
      expect(find.text('Could not load'), findsOneWidget);

      await tester.tap(find.text('Could not load'));
      await tester.pumpAndSettle();

      expect(find.text('Retry Mart'), findsOneWidget);
    });

    testWidgets('a failed list on the store screen can be retried', (tester) async {
      var calls = 0;
      await pump(tester, overrides: [
        serviceableStoresProvider.overrideWith((ref) async {
          calls++;
          if (calls == 1) throw StateError('boom');
          return const [NearestStore(id: 's1', name: 'Retry Mart', distanceKm: 1, estimatedMinutes: 9)];
        }),
      ]);
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/stores');
      await tester.pumpAndSettle();
      expect(find.text('Retry'), findsWidgets);

      await tester.tap(find.text('Retry').last);
      await tester.pumpAndSettle();

      expect(find.text('Retry Mart'), findsOneWidget);
    });
  });
}
