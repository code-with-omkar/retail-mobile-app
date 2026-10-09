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
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/features/home/product_pager.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/fake_adapter.dart';
import '../support/location_fakes.dart';

const _veg = '10000000-0000-0000-0000-000000000002';
const _dairy = '10000000-0000-0000-0000-000000000004';

Map<String, dynamic> _item(String id, String name, String categoryId, {num price = 34.5}) =>
    {'id': id, 'name': name, 'description': 'd', 'price': price, 'unitOfMeasure': '1 kg', 'categoryId': categoryId, 'imageUrl': null};

Map<String, dynamic> _page(List<Map<String, dynamic>> items, {int page = 1, int total = 0, bool hasMore = false}) =>
    {'success': true, 'data': {'items': items, 'page': page, 'pageSize': 20, 'totalCount': total == 0 ? items.length : total, 'hasMore': hasMore}};

final _categories = {
  'success': true,
  'data': [
    {'id': _veg, 'name': 'Vegetables', 'parentCategoryId': null, 'isActive': true},
    {'id': _dairy, 'name': 'Dairy', 'parentCategoryId': null, 'isActive': true},
  ]
};

void main() {
  late FakeAdapter adapter;
  late ApiCatalogRepository repo;
  setUp(() {
    adapter = FakeAdapter();
    repo = ApiCatalogRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));
  });

  group('ApiCatalogRepository', () {
    test('products maps DTOs, keeps decimal prices, and sends paging + filters', () async {
      adapter
        ..replyJson(_categories)
        ..replyJson(_page([_item('p1', 'Tomato', _veg)], total: 5, hasMore: true));

      final result = await repo.products(search: ' tom ', categoryId: _veg, page: 2, pageSize: 10);

      final request = adapter.requests.last;
      expect(request.path, '/api/catalog/products');
      expect(request.queryParameters, {'page': 2, 'pageSize': 10, 'search': 'tom', 'categoryId': _veg});
      expect((result.page, result.totalCount, result.hasMore), (1, 5, true));
      final p = result.items.single;
      expect((p.name, p.price, p.mrp, p.unit, p.category, p.categoryId), ('Tomato', 34.5, 34.5, '1 kg', 'vegetables', _veg));
      expect(p.discountPct, 0);
      expect(p.packs, isEmpty, reason: 'an API response without variants has no packs');
    });

    test('categories are fetched once and reused', () async {
      adapter
        ..replyJson(_categories)
        ..replyJson(_page([_item('p1', 'A', _veg)]))
        ..replyJson(_page([_item('p2', 'B', _dairy)]));

      await repo.products();
      final second = await repo.products();

      expect(adapter.requests.where((r) => r.path == '/api/catalog/categories').length, 1);
      expect(second.items.single.category, 'dairy');
    });

    test('a failure surfaces as a typed exception', () async {
      adapter.replyJson({'success': false, 'message': 'boom', 'errors': <String>[]}, status: 500);
      await expectLater(repo.categories(), throwsA(isA<ServerException>()));
    });

    test('maps MRP, discount and Marathi translations; falls back to English when missing', () async {
      adapter
        ..replyJson({
          'success': true,
          'data': [
            {'id': _veg, 'name': 'Vegetables', 'parentCategoryId': null, 'translations': {'mr': {'name': 'भाज्या'}}},
            {'id': _dairy, 'name': 'Dairy', 'parentCategoryId': null, 'translations': <String, dynamic>{}},
          ]
        })
        ..replyJson(_page([
          {..._item('p1', 'Tomato', _veg, price: 45), 'mrp': 55, 'discountPercent': 18, 'translations': {'MR': {'name': 'टोमॅटो', 'description': 'ताजे लाल टोमॅटो'}}},
          _item('p2', 'Milk', _dairy, price: 34),
        ]));

      final cats = await repo.categories();
      final page = await repo.products();

      expect((cats.first.label, cats.first.labelMr, cats.first.hasMarathi), ('Vegetables', 'भाज्या', true));
      expect((cats.last.labelMr, cats.last.hasMarathi), ('Dairy', false));
      final tomato = page.items.first, milk = page.items.last;
      expect((tomato.mrp, tomato.price, tomato.discountPct), (55.0, 45.0, 18));
      expect((tomato.nameMr, tomato.descriptionMr), ('टोमॅटो', 'ताजे लाल टोमॅटो'));
      expect((milk.mrp, milk.discountPct, milk.nameMr, milk.descriptionMr), (34.0, 0, 'Milk', 'd'));
    });

    test('product(id) reads the detail route', () async {
      adapter
        ..replyJson(_categories)
        ..replyJson({'success': true, 'data': {..._item('p9', 'Milk', _dairy), 'sku': 'S', 'isActive': true}});
      final p = await repo.product('p9');
      expect((p.name, p.category), ('Milk', 'dairy'));
      expect(adapter.requests.last.path, '/api/catalog/products/p9');
    });
  });

  test('CachingCatalogRepository remembers products for the cart', () async {
    final store = ProductStore();
    final cached = CachingCatalogRepository(const SeedCatalogRepository(), store);
    final page = await cached.products();
    expect(store.get(page.items.first.id), isNotNull);
    expect(store.get('missing'), isNull);
  });

  group('SeedCatalogRepository paging', () {
    test('pages, filters by category and by search', () async {
      const seeded = SeedCatalogRepository();
      final first = await seeded.products(pageSize: 4);
      final third = await seeded.products(page: 3, pageSize: 4);
      expect((first.items.length, first.hasMore, third.hasMore), (4, true, false));
      expect((await seeded.products(categoryId: 'dairy')).items.map((p) => p.name), containsAll(['Farm Milk']));
      expect((await seeded.products(search: 'onion')).items.map((p) => p.name), ['Onion']);
    });
  });

  group('ProductPager', () {
    test('loads first page, appends more, and reports the end', () async {
      final pager = ProductPager(const SeedCatalogRepository(), pageSize: 4);
      await pager.loadFirst();
      expect((pager.items.length, pager.hasMore), (4, true));
      await pager.loadMore();
      await pager.loadMore();
      expect((pager.items.length, pager.hasMore, pager.totalCount), (10, false, 10));
      await pager.loadMore();
      expect(pager.items.length, 10, reason: 'no request after the end');
    });

    test('keeps loaded items and allows retry when a later page fails', () async {
      final repo = _FlakyRepository();
      final pager = ProductPager(repo, pageSize: 4);
      await pager.loadFirst();
      repo.failNext = true;
      await pager.loadMore();
      expect(pager.items.length, 4);
      expect(pager.error, isA<ServerException>());
      expect(pager.hasMore, isTrue);
      await pager.retry();
      expect((pager.items.length, pager.error), (8, null));
    });

    test('a first-page failure shows no items and can be retried', () async {
      final repo = _FlakyRepository()..failNext = true;
      final pager = ProductPager(repo);
      await pager.loadFirst();
      expect(pager.items, isEmpty);
      expect(pager.error, isA<ServerException>());
      expect(pager.isEmpty, isFalse);
      await pager.retry();
      expect((pager.items.isNotEmpty, pager.error), (true, null));
    });

    test('a disposed pager ignores late results', () async {
      final pager = ProductPager(const SeedCatalogRepository());
      final f = pager.loadFirst();
      pager.dispose();
      await f; // must not throw
    });
  });

  group('screens on the API repository', () {
    Future<void> pumpHome(WidgetTester tester, CatalogRepository repo) async {
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
    }

    testWidgets('shows skeletons while loading, then the data', (tester) async {
      final repo = _SlowRepository();
      await pumpHome(tester, repo);
      await tester.pump();
      expect(find.text('Popular near you'), findsOneWidget);
      expect(find.textContaining('Tomato', findRichText: true), findsNothing);

      repo.complete();
      await tester.pumpAndSettle();
      // The stores row now sits above the products, so scroll down to them.
      await tester.drag(find.byType(CustomScrollView).first, const Offset(0, -500));
      await tester.pumpAndSettle();
      expect(find.textContaining('Tomato', findRichText: true), findsOneWidget);
    });

    testWidgets('shows a translated error with Retry, and recovers', (tester) async {
      final repo = _FlakyRepository()..failEverything = true;
      await pumpHome(tester, repo);
      await tester.pumpAndSettle();
      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsWidgets);
      expect(find.text('Retry'), findsWidgets);

      repo.failEverything = false;
      // Home has independent sections (store, categories, popular); retry each one.
      for (var i = 0; i < 5 && find.text('Retry').evaluate().isNotEmpty; i++) {
        await tester.tap(find.text('Retry').first);
        await tester.pumpAndSettle();
      }
      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsNothing);
    });
  });
}

class _FlakyRepository implements CatalogRepository {
  final _seed = const SeedCatalogRepository();
  bool failNext = false, failEverything = false;

  void _maybeFail() {
    if (failEverything || failNext) {
      failNext = false;
      throw failEverything ? const NetworkException('offline') : const ServerException('boom', statusCode: 500);
    }
  }

  @override
  Future<List<Category>> categories({String? storeId}) async {
    _maybeFail();
    return _seed.categories();
  }

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async {
    _maybeFail();
    return _seed.stores(latitude: latitude, longitude: longitude);
  }

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async {
    _maybeFail();
    return _seed.nearestStore(latitude: latitude, longitude: longitude);
  }

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async {
    _maybeFail();
    return _seed.products(search: search, categoryId: categoryId, storeId: storeId, page: page, pageSize: pageSize);
  }

  @override
  Future<Product> product(String id, {String? storeId}) async {
    _maybeFail();
    return _seed.product(id, storeId: storeId);
  }
}

/// Holds every call until [complete] so tests can observe the loading state.
class _SlowRepository implements CatalogRepository {
  final _seed = const SeedCatalogRepository();
  bool _open = false;

  void complete() => _open = true;

  Future<T> _wait<T>(Future<T> Function() run) async {
    while (!_open) {
      await Future<void>.delayed(const Duration(milliseconds: 50));
    }
    return run();
  }

  @override
  Future<List<Category>> categories({String? storeId}) => _wait(() => _seed.categories(storeId: storeId));

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) => _wait(() => _seed.stores(latitude: latitude, longitude: longitude));

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) => _wait(() => _seed.nearestStore(latitude: latitude, longitude: longitude));

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) => _wait(() => _seed.products(search: search, categoryId: categoryId, storeId: storeId, page: page, pageSize: pageSize));

  @override
  Future<Product> product(String id, {String? storeId}) => _wait(() => _seed.product(id, storeId: storeId));
}
