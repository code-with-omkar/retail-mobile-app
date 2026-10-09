import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/widgets.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:shared_preferences/shared_preferences.dart';

Product _product(String id, String name, {String category = 'vegetables', String? imageUrl}) => Product(
      id: id,
      name: name,
      nameMr: name,
      unit: '1 kg',
      price: 20,
      mrp: 20,
      color: Colors.pink,
      category: category,
      description: 'About $name',
      imageUrl: imageUrl,
    );

/// A catalog whose product detail call can be held, failed, or made to say "not found".
class _Repo implements CatalogRepository {
  final items = [
    _product('p1', 'Tomato'),
    _product('p2', 'Rice', category: 'pantry', imageUrl: 'http://images.invalid/rice.png'),
  ];
  Completer<void>? productGate;
  ApiException? productError;
  int productCalls = 0;

  @override
  Future<List<Category>> categories({String? storeId}) async => const [Category('c1', 'Vegetables', Icons.eco_outlined)];

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async => const [NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10)];

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async => const NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10);

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) async => ProductPage(items: items, page: 1, totalCount: items.length, hasMore: false);

  @override
  Future<Product> product(String id, {String? storeId}) async {
    productCalls++;
    await productGate?.future;
    final error = productError;
    if (error != null) throw error;
    return items.firstWhere((p) => p.id == id);
  }
}

Future<void> _pump(WidgetTester tester, CatalogRepository repo) async {
  SharedPreferences.setMockInitialValues({});
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

Future<void> _openTomato(WidgetTester tester) async {
  await tester.ensureVisible(find.textContaining('Tomato', findRichText: true));
  await tester.pumpAndSettle();
  await tester.tap(find.textContaining('Tomato', findRichText: true));
}

void main() {
  testWidgets('shows placeholders while the product loads, then the product', (tester) async {
    final repo = _Repo()..productGate = Completer<void>();
    await _pump(tester, repo);

    await _openTomato(tester);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.byType(SkeletonBox), findsWidgets);
    expect(find.text('In stock'), findsNothing);

    repo.productGate!.complete();
    await tester.pumpAndSettle();
    expect(find.byType(SkeletonBox), findsNothing);
    expect(find.text('In stock'), findsOneWidget);
    expect(find.text('About Tomato'), findsOneWidget);
  });

  testWidgets('a failed load shows a translated message with Retry, and Retry recovers', (tester) async {
    final repo = _Repo()..productError = const NetworkException('offline');
    await _pump(tester, repo);

    await _openTomato(tester);
    await tester.pumpAndSettle();
    expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
    expect(find.text('offline'), findsNothing, reason: 'raw exception text must never reach the user');

    repo.productError = null;
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Cannot reach the server. Check your connection and try again.'), findsNothing);
    expect(find.text('About Tomato'), findsOneWidget);
    expect(repo.productCalls, 2);
  });

  testWidgets('an unknown product shows a not-found message', (tester) async {
    final repo = _Repo()..productError = const NotFoundException('Product not found');
    await _pump(tester, repo);

    await _openTomato(tester);
    await tester.pumpAndSettle();

    expect(find.text('We could not find what you were looking for.'), findsOneWidget);
  });

  testWidgets('a server error never shows server text and still offers Retry', (tester) async {
    final repo = _Repo()..productError = const ServerException('SqlException: login failed for user sa', statusCode: 500);
    await _pump(tester, repo);

    await _openTomato(tester);
    await tester.pumpAndSettle();

    expect(find.text('Something went wrong on our side. Please try again.'), findsOneWidget);
    expect(find.textContaining('SqlException'), findsNothing);
    expect(find.text('Retry'), findsOneWidget);
  });

  testWidgets('a product without a photo, or whose photo fails to load, shows a generated category icon', (tester) async {
    await _pump(tester, _Repo());

    // Tomato has no image url (vegetables icon); Rice has one that cannot load (pantry icon).
    expect(find.byIcon(Icons.eco_outlined), findsWidgets);
    await tester.ensureVisible(find.textContaining('Rice', findRichText: true));
    await tester.pumpAndSettle();
    expect(find.byIcon(Icons.shopping_basket_outlined), findsWidgets);
  });
}
