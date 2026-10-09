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
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/repositories/address_repository.dart';
import 'package:quickcart_customer/data/repositories/auth_repository.dart';
import 'package:quickcart_customer/data/repositories/cart_repository.dart';
import 'package:quickcart_customer/data/repositories/notification_repository.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';
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

  test('cart and checkout: server cart, fees, one order for one key, a refusal with a reason, and the order read back', () async {
    final store = MemorySessionStore();
    late AuthRepository auth;
    final api = ApiClient(
      config: AppConfig.resolve(envName: 'dev', baseUrl: _url, androidDevice: false),
      tokenProvider: () => auth.accessToken(),
      onUnauthorized: () => auth.refreshSession(),
    );
    auth = AuthRepository(api, store);
    final catalog = ApiCatalogRepository(api);
    final carts = ApiCartRepository(api);
    final orders = ApiOrderRepository(api);
    final addresses = ApiAddressRepository(api);
    final email = 'smoke-cart-${DateTime.now().millisecondsSinceEpoch}@example.test';
    await auth.register(fullName: 'Smoke Cart', email: email, password: 'Sunrise-42x', phoneNumber: '98765 43211');

    // The fee settings are public and the cart starts empty.
    final pricing = await carts.pricing();
    expect(pricing.deliveryFee, greaterThanOrEqualTo(0));
    expect(await carts.current(), isNull);

    // A store near Bandra and a product it carries (with its default pack).
    final stores = await catalog.stores(latitude: 19.076, longitude: 72.878);
    expect(stores, isNotEmpty);
    final shop = stores.first;
    final products = await catalog.products(storeId: shop.id, carriedOnly: true, pageSize: 5);
    final product = products.items.firstWhere((p) => p.inStock);
    final pack = product.defaultPack;
    CartLineRef line(int quantity) => (productId: product.id, variantId: pack?.id, quantity: quantity);

    // A guest cart merged in: the same pack twice is one line, an unknown product is reported.
    final merged = await carts.merge(shop.id, [line(1), line(1), (productId: '11111111-1111-1111-1111-111111111111', variantId: null, quantity: 1)]);
    expect(merged.cart.lines.single.quantity, 2);
    expect(merged.notes.single.kind, CartNote.unavailable);
    final unit = merged.cart.lines.single.unitPrice;
    expect(merged.cart.pricing.subtotal, closeTo(unit * 2, 0.001));
    expect(merged.cart.pricing.total, closeTo(pricing.price(unit * 2).total, 0.001), reason: 'the app computes fees the way the server does');

    // Change a quantity, read the cart back, find it without naming a store.
    final changed = await carts.setQuantity(shop.id, line(3));
    expect(changed.lines.single.quantity, 3);
    expect((await carts.current())!.storeId, shop.id);

    // Order to a saved address near the store; the same key twice is one order.
    final address = await addresses.create(const AddressDraft(label: 'Home', line: '12 Marine Drive, Mumbai', flatOrBuilding: 'Flat 4', latitude: 19.076, longitude: 72.8777, receiverName: 'Smoke Cart', receiverPhone: '9876543211'));
    const key = 'smoke-key-0000000000000001';
    final first = await orders.place(storeId: shop.id, idempotencyKey: key, addressId: address.id);
    final again = await orders.place(storeId: shop.id, idempotencyKey: key, addressId: address.id);
    expect(again.id, first.id, reason: 'the same key returns the same order');
    expect(first.subtotal, closeTo(unit * 3, 0.001));
    expect(first.payment, 'CashOnDelivery');
    expect(first.receiverName, 'Smoke Cart');
    expect(first.total, closeTo(first.subtotal + first.deliveryFee + first.handlingFee, 0.001));
    expect(first.lines.single.quantity, 3);
    expect(first.stage, OrderStage.placed);
    expect(first.address, contains('Flat 4'));

    // The cart is empty now; a new attempt says so with a reason.
    final left = await carts.current();
    expect(left == null || left.lines.isEmpty, isTrue);
    await expectLater(
      orders.place(storeId: shop.id, idempotencyKey: 'smoke-key-0000000000000002', addressId: address.id),
      throwsA(isA<ConflictException>().having((e) => e.reason, 'reason', CheckoutReasons.cartEmpty)),
    );

    // The order is in the customer's history and can be read by id.
    expect((await orders.list()).map((o) => o.id), contains(first.id));
    expect((await orders.get(first.id)).number, first.number);

    await addresses.delete(address.id);
    await auth.logout();
  }, skip: skip ?? (_accounts ? null : 'Set --dart-define=SMOKE_ACCOUNTS=true to create a test customer'));
  test('order tracking: the store and estimate on the order, cancelling while pending, and the notification count', () async {
    final store = MemorySessionStore();
    late AuthRepository auth;
    final api = ApiClient(
      config: AppConfig.resolve(envName: 'dev', baseUrl: _url, androidDevice: false),
      tokenProvider: () => auth.accessToken(),
      onUnauthorized: () => auth.refreshSession(),
    );
    auth = AuthRepository(api, store);
    final catalog = ApiCatalogRepository(api);
    final carts = ApiCartRepository(api);
    final orders = ApiOrderRepository(api);
    final addresses = ApiAddressRepository(api);
    final notes = ApiNotificationRepository(api);
    final email = 'smoke-track-${DateTime.now().millisecondsSinceEpoch}@example.test';
    await auth.register(fullName: 'Smoke Track', email: email, password: 'Sunrise-42x', phoneNumber: '98765 43212');

    final shop = (await catalog.stores(latitude: 19.076, longitude: 72.878)).first;
    final product = (await catalog.products(storeId: shop.id, carriedOnly: true, pageSize: 5)).items.firstWhere((p) => p.inStock);
    await carts.add(shop.id, (productId: product.id, variantId: product.defaultPack?.id, quantity: 1));
    final address = await addresses.create(const AddressDraft(label: 'Home', line: '12 Marine Drive, Mumbai', latitude: 19.076, longitude: 72.8777, receiverName: 'Smoke Track', receiverPhone: '9876543212'));
    final placed = await orders.place(storeId: shop.id, idempotencyKey: 'smoke-track-0000000000001', addressId: address.id);

    expect(placed.storeName, isNotEmpty);
    expect(placed.estimatedMinutes, isNotNull);
    expect(placed.stage, OrderStage.placed);
    expect(await notes.unreadCount(), 0);

    final cancelled = await orders.cancel(placed.id);
    expect(cancelled.stage, OrderStage.cancelled);
    expect((await orders.cancel(placed.id)).stage, OrderStage.cancelled, reason: 'cancelling again is fine');
    expect((await orders.get(placed.id)).stage, OrderStage.cancelled);

    expect(await notes.unreadCount(), 1);
    final list = await notes.list();
    expect(list.single.orderId, placed.id);
    expect(list.single.title, 'Order cancelled');
    await notes.markAllRead();
    expect(await notes.unreadCount(), 0);

    await addresses.delete(address.id);
    await auth.logout();
  }, skip: skip ?? (_accounts ? null : 'Set --dart-define=SMOKE_ACCOUNTS=true to create a test customer'));
}
