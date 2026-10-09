import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/features/home/product_pager.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/location_fakes.dart';

import '../support/fake_adapter.dart';

Map<String, dynamic> _item(String id, String name, {bool? inStock, bool? lowStock}) => {
      'id': id,
      'name': name,
      'description': 'd',
      'price': 10,
      'mrp': 10,
      'discountPercent': 0,
      'unitOfMeasure': '1 kg',
      'categoryId': 'c1',
      'imageUrl': null,
      'translations': <String, dynamic>{},
      'inStock': ?inStock,
      'lowStock': ?lowStock,
    };

final _categories = {
  'success': true,
  'data': [
    {'id': 'c1', 'name': 'Vegetables', 'parentCategoryId': null, 'translations': <String, dynamic>{}}
  ]
};

void main() {
  late FakeAdapter adapter;
  late ApiCatalogRepository repo;
  setUp(() {
    adapter = FakeAdapter();
    repo = ApiCatalogRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));
  });

  group('nearestStore', () {
    test('parses the store and sends the coordinates', () async {
      adapter.replyJson({
        'success': true,
        'data': {'id': 's1', 'name': 'Harbor Point', 'distanceKm': 5.3, 'estimatedMinutes': 19, 'serviceRadiusKm': 8}
      });

      final store = await repo.nearestStore(latitude: 19.06, longitude: 72.83);

      expect((store!.id, store.name, store.distanceKm, store.estimatedMinutes), ('s1', 'Harbor Point', 5.3, 19));
      expect(adapter.requests.single.path, '/api/catalog/stores/nearest');
      expect(adapter.requests.single.queryParameters, {'latitude': 19.06, 'longitude': 72.83});
    });

    test('no serviceable store (404) is null, not an error', () async {
      adapter.replyJson({'success': false, 'message': 'No serviceable store near this location', 'errors': <String>[]}, status: 404);
      expect(await repo.nearestStore(latitude: 0, longitude: 0), isNull);
    });

    test('other failures still surface as typed exceptions', () async {
      adapter.replyJson({'success': false, 'message': 'boom', 'errors': <String>[]}, status: 500);
      await expectLater(repo.nearestStore(latitude: 1, longitude: 1), throwsA(isA<ServerException>()));
    });
  });

  group('stock flags', () {
    test('products send the store id and map inStock / lowStock', () async {
      adapter
        ..replyJson(_categories)
        ..replyJson({
          'success': true,
          'data': {
            'items': [_item('p1', 'Rice', inStock: true, lowStock: true), _item('p2', 'Milk', inStock: false, lowStock: false)],
            'page': 1,
            'pageSize': 20,
            'totalCount': 2,
            'hasMore': false
          }
        });

      final page = await repo.products(storeId: 's1');

      expect(adapter.requests.last.queryParameters['storeId'], 's1');
      expect((page.items[0].inStock, page.items[0].lowStock), (true, true));
      expect((page.items[1].inStock, page.items[1].lowStock), (false, false));
    });

    test('without a store the flags are unknown and the product counts as in stock', () async {
      adapter
        ..replyJson(_categories)
        ..replyJson({
          'success': true,
          'data': {'items': [_item('p1', 'Rice')], 'page': 1, 'pageSize': 20, 'totalCount': 1, 'hasMore': false}
        });

      final page = await repo.products();

      expect(adapter.requests.last.queryParameters.containsKey('storeId'), isFalse);
      expect((page.items.single.inStock, page.items.single.lowStock), (true, false));
    });

    test('product(id) forwards the store id', () async {
      adapter
        ..replyJson(_categories)
        ..replyJson({'success': true, 'data': _item('p1', 'Rice', inStock: false, lowStock: false)});

      final p = await repo.product('p1', storeId: 's9');

      expect(adapter.requests.last.path, '/api/catalog/products/p1');
      expect(adapter.requests.last.queryParameters, {'storeId': 's9'});
      expect(p.inStock, isFalse);
    });
  });

  group('ProductPager store resolution', () {
    test('with a store the list is that store catalogue; without one it is the whole catalogue', () async {
      final withStore = _FakeRepo(items: [_p('p1', 'P1')]);
      await ProductPager(withStore, storeId: () async => 's1').loadFirst();
      expect(withStore.carriedOnlySeen, [true]);

      final noStore = _FakeRepo(items: [_p('p1', 'P1')]);
      await ProductPager(noStore, storeId: () async => null).loadFirst();
      expect(noStore.carriedOnlySeen, [false]);
      expect(noStore.storeIdsSeen, [null]);
    });

    test('resolves the store once and passes it on every page', () async {
      final fake = _FakeRepo(items: [for (var i = 0; i < 6; i++) _p('p$i', 'P$i')]);
      var resolved = 0;
      final pager = ProductPager(fake, pageSize: 4, storeId: () async {
        resolved++;
        return 's1';
      });
      await pager.loadFirst();
      await pager.loadMore();
      expect(resolved, 1);
      expect(fake.storeIdsSeen, ['s1', 's1']);
    });

    test('a failing store lookup does not break the list (stock stays unknown)', () async {
      final fake = _FakeRepo(items: [_p('p1', 'P1')]);
      final pager = ProductPager(fake, storeId: () async => throw StateError('lookup failed'));
      await pager.loadFirst();
      expect((pager.items.length, pager.error), (1, null));
      expect(fake.storeIdsSeen, [null]);
    });
  });

  group('interim delivery location', () {
    test('coordinates are rounded to 3 decimals', () {
      expect(roundCoordinate(19.059649), 19.06);
      expect(roundCoordinate(72.82951), 72.83);
      expect(roundCoordinate(-33.8688197), -33.869);
    });

    test('defaults to Bandra West and can be overridden', () {
      final c = AppConfig.resolve(envName: 'dev', baseUrl: '', androidDevice: false);
      expect((c.defaultLatitude, c.defaultLongitude), (19.0596, 72.8295));
      final d = AppConfig.resolve(envName: 'dev', baseUrl: '', androidDevice: false, defaultLatitude: 12.5, defaultLongitude: 77.5);
      expect((d.defaultLatitude, d.defaultLongitude), (12.5, 77.5));
    });

    Future<ProviderContainer> container(Map<String, Object> prefs, {bool seed = false}) async {
      SharedPreferences.setMockInitialValues(prefs);
      final c = ProviderContainer(overrides: [
        sharedPrefsProvider.overrideWithValue(await SharedPreferences.getInstance()),
        appConfigProvider.overrideWithValue(AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://x', useSeedData: seed, defaultLatitude: 12.3456, defaultLongitude: 77.6543)),
      ]);
      addTearDown(c.dispose);
      return c;
    }

    test('the delivery location is the rounded place chosen on the device', () async {
      final c = await container(deviceLocationPrefs);
      expect(c.read(deliveryLocationProvider), (latitude: 19.06, longitude: 72.83));
    });

    test('with no place chosen there is no delivery location (the app asks where to deliver)', () async {
      final c = await container({});
      expect(c.read(deliveryLocationProvider), isNull);
      expect(c.read(deliveryPlaceProvider), isNull);
    });

    test('only in seed mode (offline development) a fixed place stands in, rounded like any other', () async {
      final c = await container({}, seed: true);
      expect(c.read(deliveryLocationProvider), (latitude: 12.346, longitude: 77.654));
    });
  });

  group('screens', () {
    Future<void> pump(WidgetTester tester, CatalogRepository repo) async {
      SharedPreferences.setMockInitialValues(deviceLocationPrefs);
      final sp = await SharedPreferences.getInstance();
      tester.view.physicalSize = const Size(390 * 3, 844 * 3);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(ProviderScope(
        retry: noAutomaticRetry,
        overrides: [
          sharedPrefsProvider.overrideWithValue(sp),
          appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
          catalogRepositoryProvider.overrideWithValue(repo),
        ],
        child: const QuickCartApp(),
      ));
      await tester.pumpAndSettle();
    }

    testWidgets('Home shows the real store, distance and estimate, and loads products for that store', (tester) async {
      final fake = _FakeRepo(store: const NearestStore(id: 's7', name: 'Test Store', distanceKm: 2.4, estimatedMinutes: 17), items: [_p('p1', 'Tomato')]);
      await pump(tester, fake);

      expect(find.text('SHOPPING FROM'), findsOneWidget);
      expect(find.text('Test Store'), findsOneWidget);
      expect(find.textContaining('2.4 km away'), findsOneWidget);
      expect(find.textContaining('arrives in 17 min'), findsOneWidget);
      expect(fake.storeIdsSeen, contains('s7'));
      expect(fake.carriedOnlySeen, everyElement(isTrue), reason: 'with a store, only what that store carries is requested');
    });

    testWidgets('Home explains when no store serves the location', (tester) async {
      await pump(tester, _FakeRepo(store: null, items: [_p('p1', 'Tomato')]));

      expect(find.text('We do not deliver here yet'), findsOneWidget);
      await tester.drag(find.byType(CustomScrollView).first, const Offset(0, -500));
      await tester.pumpAndSettle();
      expect(find.textContaining('Tomato', findRichText: true), findsOneWidget, reason: 'products still load, with unknown stock');
    });

    testWidgets('an out-of-stock product shows Out of stock on its card', (tester) async {
      await pump(tester, _FakeRepo(store: const NearestStore(id: 's1', name: 'S', distanceKm: 1, estimatedMinutes: 10), items: [_p('p1', 'Milk', inStock: false)]));

      await tester.drag(find.byType(CustomScrollView).first, const Offset(0, -500));
      await tester.pumpAndSettle();
      expect(find.text('Out of stock'), findsOneWidget);
    });

    testWidgets('an out-of-stock product page offers to check other stores', (tester) async {
      await pump(tester, _FakeRepo(store: const NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10), items: [_p('p1', 'Rice', inStock: false)]));

      await tester.drag(find.byType(CustomScrollView).first, const Offset(0, -500));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('Rice', findRichText: true));
      await tester.pumpAndSettle();
      expect(find.text('Check other stores'), findsOneWidget);

      await tester.tap(find.text('Check other stores'));
      await tester.pumpAndSettle();

      expect(find.text('Choose your store'), findsOneWidget);
    });

    testWidgets('the product page says "Only a few left" and names the store', (tester) async {
      await pump(tester, _FakeRepo(store: const NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10), items: [_p('p1', 'Rice', lowStock: true)]));

      await tester.ensureVisible(find.textContaining('Rice', findRichText: true));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('Rice', findRichText: true));
      await tester.pumpAndSettle();

      expect(find.text('Only a few left'), findsOneWidget);
      expect(find.text('Sold by Harbor Point'), findsOneWidget);
    });
  });
}

Product _p(String id, String name, {bool inStock = true, bool lowStock = false}) => Product(
      id: id,
      name: name,
      nameMr: name,
      unit: '1 kg',
      price: 10,
      mrp: 10,
      color: Colors.pink,
      category: 'vegetables',
      description: 'd',
      inStock: inStock,
      lowStock: lowStock,
    );

class _FakeRepo implements CatalogRepository {
  _FakeRepo({this.store, required this.items});
  final NearestStore? store;
  final List<Product> items;
  final storeIdsSeen = <String?>[];
  final carriedOnlySeen = <bool>[];

  @override
  Future<List<Category>> categories({String? storeId}) async => const [Category('c1', 'Vegetables', Icons.eco_outlined)];

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async => [?store];

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async => store;

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async {
    storeIdsSeen.add(storeId);
    carriedOnlySeen.add(carriedOnly);
    final start = (page - 1) * pageSize;
    final slice = items.skip(start).take(pageSize).toList();
    return ProductPage(items: slice, page: page, totalCount: items.length, hasMore: start + slice.length < items.length);
  }

  @override
  Future<Product> product(String id, {String? storeId}) async {
    storeIdsSeen.add(storeId);
    return items.firstWhere((p) => p.id == id);
  }
}
