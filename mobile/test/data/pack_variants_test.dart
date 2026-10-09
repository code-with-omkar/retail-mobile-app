import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

Map<String, dynamic> _variant(String id, String label, num price, {num? mrp, bool isDefault = false, bool? inStock, bool? lowStock}) =>
    {'id': id, 'label': label, 'price': price, 'mrp': mrp ?? price, 'discountPercent': 0, 'isDefault': isDefault, 'inStock': inStock, 'lowStock': lowStock};

Map<String, dynamic> _productJson({List<Map<String, dynamic>>? variants, bool? inStock}) => {
      'id': 'p1',
      'name': 'Tomato',
      'description': 'Fresh',
      'price': 45,
      'mrp': 55,
      'discountPercent': 18,
      'unitOfMeasure': '1 kg',
      'categoryId': 'c1',
      'imageUrl': null,
      'translations': <String, dynamic>{},
      'inStock': inStock,
      'variants': ?variants,
    };

const _small = PackOption(id: 'v250', label: '250 g', price: 13, mrp: 16);
const _half = PackOption(id: 'v500', label: '500 g', price: 24, mrp: 29);
const _kilo = PackOption(id: 'v1', label: '1 kg', price: 45, mrp: 55, isDefault: true);

Product _tomato({List<PackOption> packs = const [_small, _half, _kilo], bool inStock = true}) => Product(
      id: 'p1',
      name: 'Tomato',
      nameMr: 'टोमॅटो',
      unit: '1 kg',
      price: 45,
      mrp: 55,
      color: Colors.pink,
      category: 'vegetables',
      description: 'Fresh tomatoes',
      packs: packs,
      inStock: inStock,
    );

void main() {
  group('variants from the API', () {
    test('a product carries its variants, with the default marked and stock unknown without a store', () {
      final dto = CatalogProductDto.fromJson(_productJson(variants: [
        _variant('v250', '250 g', 13, mrp: 16),
        _variant('v500', '500 g', 24, mrp: 29),
        _variant('v1', '1 kg', 45, mrp: 55, isDefault: true),
      ]));

      final product = productFromCatalogDto(dto, 'vegetables');

      expect(product.packs.map((p) => p.label), ['250 g', '500 g', '1 kg']);
      expect(product.defaultPack!.id, 'v1');
      expect(product.packs.first.isDefault, isFalse);
      expect(product.packs.every((p) => p.inStock && !p.lowStock), isTrue, reason: 'no store asked: unknown counts as in stock');
      expect((product.packs[1].price, product.packs[1].mrp, product.packs[1].discountPct), (24.0, 29.0, 17));
    });

    test('with a store each pack has its own stock flags', () {
      final dto = CatalogProductDto.fromJson(_productJson(inStock: true, variants: [
        _variant('v250', '250 g', 13, inStock: false, lowStock: false),
        _variant('v500', '500 g', 24, inStock: true, lowStock: true),
        _variant('v1', '1 kg', 45, isDefault: true, inStock: true, lowStock: false),
      ]));

      final packs = productFromCatalogDto(dto, 'vegetables').packs;

      expect((packs[0].inStock, packs[0].lowStock), (false, false));
      expect((packs[1].inStock, packs[1].lowStock), (true, true));
      expect((packs[2].inStock, packs[2].lowStock), (true, false));
    });

    test('an API response without variants gives a product without packs, and a missing mrp falls back to the price', () {
      final withoutVariants = productFromCatalogDto(CatalogProductDto.fromJson(_productJson()), 'vegetables');
      expect(withoutVariants.packs, isEmpty);
      expect(withoutVariants.defaultPack, isNull);

      final noMrp = CatalogVariantDto.fromJson({'id': 'v', 'label': '1 kg', 'price': 30, 'isDefault': true});
      expect((noMrp.price, noMrp.mrp), (30.0, 30.0));
    });

    test('a variant without an id or label is rejected rather than half-read', () {
      expect(() => CatalogVariantDto.fromJson({'label': '1 kg', 'price': 1}), throwsFormatException);
      expect(() => CatalogVariantDto.fromJson({'id': 'v', 'price': 1}), throwsFormatException);
    });
  });

  group('cart keys and lines', () {
    test('the key is the pack id; with no pack named it is the default pack; no packs means the product id', () {
      final product = _tomato();
      expect(cartKey(product, _half), 'v500');
      expect(cartKey(product), 'v1');
      expect(cartKey(_tomato(packs: const [])), 'p1');
    });

    test('the product store finds a product by its id or by any of its pack ids', () {
      final store = ProductStore()..put(_tomato());
      expect(store.get('p1')?.name, 'Tomato');
      expect(store.get('v500')?.name, 'Tomato');
      expect(store.get('unknown'), isNull);
    });

    test('a pack key makes a line with that pack price, label and savings', () {
      final store = ProductStore()..put(_tomato());

      final half = lineFromKey('v500', 2, store.get)!;
      final kilo = lineFromKey('v1', 1, store.get)!;

      expect((half.unitLabel, half.unitPrice, half.unitMrp, half.total), ('500 g', 24.0, 29.0, 48.0));
      expect((kilo.unitLabel, kilo.unitPrice, kilo.total), ('1 kg', 45.0, 45.0));
      expect(CartTotals([half, kilo]).savings, 2 * (29 - 24) + (55 - 45));
      expect(lineFromKey('gone', 1, store.get), isNull);
    });

    test('a product without packs keeps working with its own price and unit', () {
      final store = ProductStore()..put(_tomato(packs: const []));

      final line = lineFromKey('p1', 3, store.get)!;

      expect((line.pack, line.unitLabel, line.unitPrice, line.total), (null, '1 kg', 45.0, 135.0));
    });
  });

  group('product screen', () {
    Future<ProviderContainer> open(WidgetTester tester, Product product) async {
      SharedPreferences.setMockInitialValues({});
      final sp = await SharedPreferences.getInstance();
      tester.view.physicalSize = const Size(390 * 3, 844 * 3);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
        sharedPrefsProvider.overrideWithValue(sp),
        appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
        catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(_Repo(product), ProductStore())),
        productStoreProvider.overrideWithValue(ProductStore()..put(product)),
      ], child: const QuickCartApp()));
      await tester.pumpAndSettle();
      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/product/p1');
      await tester.pumpAndSettle();
      return ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
    }

    testWidgets('the pack selector is built from the variants, the default is chosen, and the price follows the pack', (tester) async {
      await open(tester, _tomato());

      expect(find.text('250 g'), findsOneWidget);
      expect(find.text('500 g'), findsOneWidget);
      expect(find.text('1 kg'), findsOneWidget);
      expect(find.text('₹45'), findsWidgets, reason: 'the default 1 kg pack is selected first');
      expect(find.textContaining('Add · ₹45'), findsOneWidget);

      await tester.tap(find.text('500 g'));
      await tester.pumpAndSettle();

      expect(find.text('₹24'), findsWidgets);
      expect(find.text('You save ₹5'), findsOneWidget);
      expect(find.textContaining('Add · ₹24'), findsOneWidget);
    });

    testWidgets('adding puts that pack in the cart, and two sizes of one product are two lines', (tester) async {
      final container = await open(tester, _tomato());

      await tester.tap(find.text('500 g'));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('Add · ₹24'));
      await tester.pumpAndSettle();
      await tester.pump(const Duration(seconds: 6)); // let the Added to cart message go, it covers the button
      await tester.pumpAndSettle();
      await tester.tap(find.text('1 kg'));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('Add · ₹45'));
      await tester.pumpAndSettle();

      expect(container.read(cartProvider), {'v500': 1, 'v1': 1});
      final lines = container.read(cartTotalsProvider).lines;
      expect(lines.map((l) => (l.unitLabel, l.total)), [('500 g', 24.0), ('1 kg', 45.0)]);
      expect(container.read(cartTotalsProvider).subtotal, 69.0);
    });

    testWidgets('a pack that is out of stock cannot be chosen', (tester) async {
      final container = await open(tester, _tomato(packs: const [_small, PackOption(id: 'v500', label: '500 g', price: 24, mrp: 29, inStock: false), _kilo]));

      await tester.tap(find.text('500 g'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Add · ₹45'), findsOneWidget, reason: 'the selection did not move');
      expect(find.textContaining('Add · ₹24'), findsNothing);
      expect(container.read(cartProvider), isEmpty);
    });

    testWidgets('when the default pack is sold out the first pack in stock is chosen', (tester) async {
      await open(tester, _tomato(packs: const [_small, _half, PackOption(id: 'v1', label: '1 kg', price: 45, mrp: 55, isDefault: true, inStock: false)], inStock: false));

      expect(find.textContaining('Add · ₹13'), findsOneWidget);
      expect(find.text('In stock'), findsOneWidget);
    });

    testWidgets('when every pack is sold out the page says so and offers no add', (tester) async {
      await open(tester, _tomato(packs: const [PackOption(id: 'v500', label: '500 g', price: 24, mrp: 29, inStock: false), PackOption(id: 'v1', label: '1 kg', price: 45, mrp: 55, isDefault: true, inStock: false)], inStock: false));

      expect(find.text('Out of stock'), findsWidgets);
      expect(find.textContaining('Add ·'), findsNothing);
      expect(find.text('Check other stores'), findsOneWidget);
    });

    testWidgets('a low-stock pack says only a few are left', (tester) async {
      await open(tester, _tomato(packs: const [_small, PackOption(id: 'v500', label: '500 g', price: 24, mrp: 29, lowStock: true), _kilo]));

      expect(find.text('Only a few left'), findsNothing);
      await tester.tap(find.text('500 g'));
      await tester.pumpAndSettle();
      expect(find.text('Only a few left'), findsOneWidget);
    });

    testWidgets('a product with a single pack shows its label and no selector', (tester) async {
      await open(tester, _tomato(packs: const [PackOption(id: 'v500ml', label: '500 ml', price: 34, mrp: 34, isDefault: true)]));

      expect(find.text('500 ml'), findsOneWidget);
      expect(find.byType(GestureDetector), findsWidgets);
      expect(find.text('250 g'), findsNothing);
    });
  });
}

/// A catalogue of one product.
class _Repo implements CatalogRepository {
  const _Repo(this.item);
  final Product item;

  @override
  Future<List<Category>> categories({String? storeId}) async => const [Category('c1', 'Vegetables', Icons.eco_outlined)];

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async => const [NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10)];

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async => const NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10);

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async =>
      ProductPage(items: [item], page: 1, totalCount: 1, hasMore: false);

  @override
  Future<Product> product(String id, {String? storeId}) async => item;
}
