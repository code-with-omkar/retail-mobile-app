// Opt-in smoke test against a running API. Skipped unless SMOKE_API_URL is provided:
//
//   flutter test test/integration/api_smoke_test.dart --dart-define=SMOKE_API_URL=http://localhost:5067
//
// It uses the app's real ApiClient, repositories and DTOs, so it catches contract drift
// (renamed fields, changed envelope, authorization changes) before a device does.
//
// The account test creates a real customer (smoke-<time>@example.test) in the database the API uses, so it only runs
// with an extra --dart-define=SMOKE_ACCOUNTS=true. Use a development database and delete such rows now and then.
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';
import 'package:quickcart_customer/data/repositories/auth_repository.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/health_repository.dart';
import 'package:quickcart_customer/data/session_store.dart';

const _url = String.fromEnvironment('SMOKE_API_URL');
const _accounts = bool.fromEnvironment('SMOKE_ACCOUNTS');

void main() {
  final skip = _url.isEmpty ? 'Set --dart-define=SMOKE_API_URL=<api base url> to run' : null;
  late ApiClient client;

  setUpAll(() async {
    HttpOverrides.global = null; // flutter test blocks real HTTP by default
    if (_url.isEmpty) return;
    client = ApiClient(config: AppConfig.resolve(envName: 'dev', baseUrl: _url, androidDevice: false));
    try {
      await client.probe('/health/live');
    } on ApiException {
      fail('The API is not reachable at $_url.\n'
          'Start it first:  dotnet run --project src/QuickCommerce.Api --launch-profile http\n'
          'Or run everything in one go:  powershell -File mobile/tool/smoke.ps1');
    }
  });

  test('health: live and ready', () async {
    final r = await HealthRepository(client).check();
    expect(r.liveOk, isTrue);
    expect(r.readyOk, isTrue, reason: 'ready means the database is reachable');
  }, skip: skip);

  test('categories parse into app models', () async {
    final cats = await ApiCatalogRepository(client).categories();
    expect(cats, isNotEmpty);
    expect(cats.every((c) => c.id.isNotEmpty && c.label.isNotEmpty), isTrue);
  }, skip: skip);

  test('public catalog products: paged, anonymous, customer fields only', () async {
    final page = await client.get('/api/catalog/products', query: {'page': 1, 'pageSize': 2}, parse: (d) => PagedDto.fromJson(d, CatalogProductDto.fromJson));
    expect(page.items, isNotEmpty);
    expect(page.items.length, lessThanOrEqualTo(2));
    expect(page.totalCount, greaterThanOrEqualTo(page.items.length));
    expect(page.page, 1);
  }, skip: skip);

  test('products carry a consistent MRP, discount and translations map', () async {
    final page = await client.get('/api/catalog/products', query: {'pageSize': 50}, parse: (d) => PagedDto.fromJson(d, CatalogProductDto.fromJson));
    for (final p in page.items) {
      expect(p.mrp, greaterThanOrEqualTo(p.price), reason: p.name);
      expect(p.discountPercent, inInclusiveRange(0, 99), reason: p.name);
      if (p.mrp == p.price) expect(p.discountPercent, 0, reason: p.name);
    }
  }, skip: skip);

  test('customer product detail and categories use the catalog routes', () async {
    final repo = ApiCatalogRepository(client);
    final cats = await repo.categories();
    expect(cats, isNotEmpty);
    final first = (await repo.products(pageSize: 1)).items.single;
    final detail = await repo.product(first.id);
    expect((detail.name, detail.price, detail.mrp), (first.name, first.price, first.mrp));
    await expectLater(client.get('/api/catalog/products/00000000-0000-0000-0000-000000000000', parse: (d) => d), throwsA(isA<NotFoundException>()));
  }, skip: skip);

  test('nearest store for the demo location has a distance and a delivery estimate; far away is null', () async {
    final repo = ApiCatalogRepository(client);
    final store = await repo.nearestStore(latitude: 19.06, longitude: 72.83);
    expect(store, isNotNull);
    expect(store!.distanceKm, greaterThan(0));
    expect(store.estimatedMinutes, greaterThan(0));
    expect(await repo.nearestStore(latitude: 0, longitude: 0), isNull);
  }, skip: skip);

  test('stock flags are returned for a store and unknown without one', () async {
    final repo = ApiCatalogRepository(client);
    final store = (await repo.nearestStore(latitude: 19.06, longitude: 72.83))!;
    final withStore = (await repo.products(storeId: store.id, pageSize: 50)).items;
    final stockByName = {for (final p in withStore) p.name: p};
    expect(withStore, isNotEmpty);
    // Every product is either in stock or out of stock for a store; low stock only applies to in-stock products.
    for (final p in withStore) {
      if (p.lowStock) expect(p.inStock, isTrue, reason: p.name);
    }
    expect(stockByName.length, withStore.length);
    // Without a store the app treats products as in stock (flags unknown).
    final unknown = (await repo.products(pageSize: 50)).items;
    expect(unknown.every((p) => p.inStock && !p.lowStock), isTrue);
    await expectLater(client.get('/api/catalog/products', query: {'storeId': '00000000-0000-0000-0000-0000000000aa'}, parse: (d) => d), throwsA(isA<NotFoundException>()));
  }, skip: skip);

  test('store selection: stores that deliver here, and that store catalogue and categories', () async {
    final repo = ApiCatalogRepository(client);
    final stores = await repo.stores(latitude: 19.06, longitude: 72.83);
    expect(stores, isNotEmpty);
    expect(stores.every((s) => s.name.isNotEmpty && s.address.isNotEmpty && s.estimatedMinutes > 0), isTrue);
    expect(stores.map((s) => s.distanceKm).toList(), orderedEquals([...stores.map((s) => s.distanceKm)]..sort()));
    expect((await repo.nearestStore(latitude: 19.06, longitude: 72.83))?.id, stores.first.id, reason: 'the nearest store is the first of the list');
    expect(await repo.stores(latitude: 0, longitude: 0), isEmpty);

    final all = (await repo.products(pageSize: 50)).items;
    final carried = await repo.products(storeId: stores.first.id, carriedOnly: true, pageSize: 50);
    expect(carried.totalCount, lessThanOrEqualTo(all.length));
    expect(carried.items.map((p) => p.id), everyElement(isIn(all.map((p) => p.id))));
    expect((await repo.categories(storeId: stores.first.id)), isNotEmpty);
    await expectLater(client.get('/api/catalog/products', query: {'carriedOnly': true}, parse: (d) => d), throwsA(isA<ValidationException>()));
  }, skip: skip);

  test('pack sizes: variants parse into packs with a default, per-pack stock at a store, and unknown stock without one', () async {
    final repo = ApiCatalogRepository(client);
    final store = (await repo.nearestStore(latitude: 19.06, longitude: 72.83))!;

    final withStore = (await repo.products(search: 'Tomato', storeId: store.id, pageSize: 5)).items.single;
    expect(withStore.packs.length, greaterThanOrEqualTo(2), reason: 'the dev database has 250 g, 500 g and 1 kg tomato packs (docs/sql/dev-seed-variants.sql)');
    expect(withStore.packs.where((p) => p.isDefault), hasLength(1));
    expect(withStore.defaultPack!.label, withStore.unit, reason: 'the product fields describe its default pack');
    expect((withStore.defaultPack!.price, withStore.defaultPack!.mrp), (withStore.price, withStore.mrp));
    expect(withStore.packs.map((p) => p.price).toSet().length, withStore.packs.length, reason: 'each pack has its own price');
    expect(withStore.packs.every((p) => p.id.length > 8), isTrue, reason: 'pack ids are variant ids');

    final detail = await repo.product(withStore.id, storeId: store.id);
    expect(detail.packs.map((p) => p.id), withStore.packs.map((p) => p.id));

    // Without a store stock is unknown, so every pack counts as in stock.
    final unknown = await repo.product(withStore.id);
    expect(unknown.packs.every((p) => p.inStock && !p.lowStock), isTrue);

    // A product with a single pack still lists it.
    final milk = (await repo.products(search: 'Milk', pageSize: 5)).items.single;
    expect(milk.packs, hasLength(1));
    expect(milk.packs.single.isDefault, isTrue);
  }, skip: skip);

  test('public catalog rejects an oversized page with a validation error', () async {
    await expectLater(client.get('/api/catalog/products', query: {'pageSize': 500}, parse: (d) => d), throwsA(isA<ValidationException>()));
  }, skip: skip);

  test('the admin product list is still protected', () async {
    await expectLater(client.get('/api/products', parse: (d) => asList(d, ProductDto.fromJson)), throwsA(isA<UnauthorizedException>()));
  }, skip: skip);

  test('nearest store parses', () async {
    final store = await client.get('/api/stores/nearest', query: {'latitude': 19.06, 'longitude': 72.83}, parse: (d) => StoreDto.fromJson(asObject(d)));
    expect(store.name, isNotEmpty);
    expect(store.serviceRadiusKm, greaterThan(0));
  }, skip: skip);

  test('unknown product id is NotFoundException', () async {
    await expectLater(client.get('/api/products/00000000-0000-0000-0000-000000000000', parse: (d) => d), throwsA(isA<NotFoundException>()));
  }, skip: skip);

  test('customer account: register, profile, refresh, logout, log in again', () async {
    final store = MemorySessionStore();
    late AuthRepository auth;
    final api = ApiClient(
      config: AppConfig.resolve(envName: 'dev', baseUrl: _url, androidDevice: false),
      tokenProvider: () => auth.accessToken(),
      onUnauthorized: () => auth.refreshSession(),
    );
    auth = AuthRepository(api, store);
    final email = 'smoke-${DateTime.now().millisecondsSinceEpoch}@example.test';
    const password = 'Sunrise-42x';

    final registered = await auth.register(fullName: 'Smoke Test', email: email, password: password, phoneNumber: '98765 43210');
    expect(registered.email, email);
    expect(store.token, isNotEmpty);

    final profile = await auth.profile();
    expect(profile.fullName, 'Smoke Test');
    expect(profile.phoneNumber, '9876543210');
    expect((await auth.updateProfile(fullName: 'Smoke Renamed', phoneNumber: null)).fullName, 'Smoke Renamed');

    final firstRefresh = store.token;
    expect(await auth.refreshSession(), isTrue);
    expect(store.token, isNot(firstRefresh), reason: 'refresh tokens rotate');

    await expectLater(auth.register(fullName: 'Again', email: email, password: password), throwsA(isA<ConflictException>()));
    await expectLater(auth.login(email: email, password: 'Wrong-pass-1'), throwsA(isA<UnauthorizedException>()));

    await auth.logout();
    expect(store.token, isNull);
    await expectLater(auth.profile(), throwsA(isA<UnauthorizedException>()));
    expect((await auth.login(email: email, password: password)).fullName, 'Smoke Renamed');
    await auth.logout();
  }, skip: skip ?? (_accounts ? null : 'Set --dart-define=SMOKE_ACCOUNTS=true to create a test customer'));
}
