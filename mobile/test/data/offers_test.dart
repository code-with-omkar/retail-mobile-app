import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';
import 'package:quickcart_customer/data/dto/order_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/offers_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';

class _Auth extends AuthNotifier {
  @override
  AuthState build() => const AuthState(AuthStatus.signedIn, CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test'));
}

class _Offers implements OffersRepository {
  _Offers({this.active_ = const [], this.recent = const [], this.single});
  final List<Offer> active_;
  final List<RecentStore> recent;
  final Offer? single;
  final asked = <String>[];

  @override
  Future<List<Offer>> active(String storeId) async => active_;

  @override
  Future<Offer?> offer(String id) async {
    asked.add(id);
    return single;
  }

  @override
  Future<List<RecentStore>> recentStores() async => recent;

  @override
  Future<List<CatalogProductDto>> boughtBefore(String storeId) async => const [];
}

Future<void> _open(WidgetTester tester, _Offers offers, {FakeNotificationRepository? notes}) async {
  SharedPreferences.setMockInitialValues({});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2400 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(_Auth.new),
    cartRepositoryProvider.overrideWithValue(FakeCartRepository()),
    orderRepositoryProvider.overrideWithValue(FakeOrderRepository()),
    offersRepositoryProvider.overrideWithValue(offers),
    if (notes != null) notificationRepositoryProvider.overrideWithValue(notes),
    productStoreProvider.overrideWithValue(products),
    selectedStoreProvider.overrideWith((ref) async => _store),
    serviceableStoresProvider.overrideWith((ref) async => [_store]),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
}

const _store = NearestStore(id: 's1', name: 'Harbor Point', distanceKm: 2.0, estimatedMinutes: 20);
const _discount = Offer(id: 'o1', title: 'Tomato week', body: 'Fresh tomatoes for less.', discountPercent: 20, productIds: ['p1']);

void main() {
  group('the repository', () {
    late FakeAdapter adapter;
    late ApiOffersRepository repo;
    setUp(() {
      adapter = FakeAdapter();
      repo = ApiOffersRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));
    });

    test('active offers are read for the store and the picture becomes a full link', () async {
      adapter.replyJson({
        'success': true,
        'data': [
          {'id': 'o1', 'titleEn': 'Tomato week', 'bodyEn': 'Less', 'titleMr': 'टोमॅटो', 'imageUrl': '/media/offers/a.png', 'discountPercent': 20, 'endsAt': '2026-10-20T10:00:00Z', 'productIds': ['p1'], 'showOnLoad': true}
        ]
      });

      final offers = await repo.active('s1');

      expect(adapter.requests.single.path, '/api/catalog/offers');
      expect(adapter.requests.single.queryParameters['storeId'], 's1');
      final offer = offers.single;
      expect((offer.title, offer.imageUrl, offer.discountPercent, offer.showOnLoad), ('Tomato week', 'http://api.test/media/offers/a.png', 20, true));
      expect(offer.productIds, ['p1']);
      expect(offer.titleIn(true), 'टोमॅटो');
      expect(offer.titleIn(false), 'Tomato week');
      expect(offer.bodyIn(true), 'Less');
    });

    test('an offer that is gone is null, and a found one lists its stores', () async {
      adapter.replyJson({
        'success': true,
        'data': {
          'id': 'o1',
          'titleEn': 'x',
          'running': false,
          'stores': [
            {'id': 's1', 'name': 'Harbor'}
          ],
          'productIds': []
        }
      });
      final found = await repo.offer('o1');
      expect((found!.running, found.stores.single.name), (false, 'Harbor'));

      adapter.replyJson({'success': false, 'message': 'Offer not found.'}, status: 404);
      expect(await repo.offer('nope'), isNull);
    });

    test('recent stores come newest first as the API sends them', () async {
      adapter.replyJson({
        'success': true,
        'data': [
          {'id': 's1', 'name': 'Harbor', 'address': 'Pier 1', 'lastOrderedAt': '2026-10-14T10:00:00Z'}
        ]
      });

      final stores = await repo.recentStores();

      expect(adapter.requests.single.path, '/api/customer/recent-stores');
      expect((stores.single.name, stores.single.address), ('Harbor', 'Pier 1'));
    });

    test('a notification carries its offer and picture', () {
      final note = notificationFromJson({'id': 'n1', 'type': 'Offer', 'title': 't', 'message': 'm', 'isRead': false, 'createdAt': '2026-10-14T10:00:00Z', 'category': 'Offer', 'campaignId': 'o1', 'imageUrl': '/media/offers/a.png'});

      expect((note.campaignId, note.imageUrl), ('o1', '/media/offers/a.png'));
      expect(note.asRead().campaignId, 'o1');
    });
  });

  group('on Home', () {
    testWidgets('a running offer is a banner, and tapping it opens the offer', (tester) async {
      final offers = _Offers(active_: const [_discount], single: _discount);
      await _open(tester, offers);

      expect(find.text('Offers for you'), findsOneWidget);
      expect(find.text('Tomato week'), findsOneWidget);
      expect(find.text('20% off'), findsOneWidget);

      await tester.tap(find.text('Tomato week'));
      await tester.pumpAndSettle();

      expect(offers.asked, ['o1']);
      expect(find.text('Fresh tomatoes for less.'), findsOneWidget);
      expect(find.text('Products in this offer'), findsOneWidget);
    });

    testWidgets('with no offers there is no banner row', (tester) async {
      await _open(tester, _Offers());

      expect(find.text('Offers for you'), findsNothing);
    });

    testWidgets('an offer that asked to be shown when the app opens pops up once', (tester) async {
      const popup = Offer(id: 'o2', title: 'Diwali sale', body: 'Sweets in store.', showOnLoad: true);
      final offers = _Offers(active_: const [popup], single: popup);
      await _open(tester, offers);

      expect(find.text('See the offer'), findsOneWidget);
      await tester.tap(find.text('Close'));
      await tester.pumpAndSettle();
      expect(find.text('See the offer'), findsNothing);
    });

    testWidgets('the stores the customer bought from show as cards', (tester) async {
      final offers = _Offers(recent: [
        RecentStore(id: 's9', name: 'Old Town Mart', address: 'Lane 4', lastOrderedAt: DateTime(2026, 10, 14)),
      ]);
      await _open(tester, offers);

      expect(find.text('Your stores'), findsOneWidget);
      expect(find.text('Old Town Mart'), findsOneWidget);
    });
  });

  testWidgets('an offer that is gone says so', (tester) async {
    final offers = _Offers(active_: const [_discount]);
    await _open(tester, offers);

    await tester.tap(find.text('Tomato week'));
    await tester.pumpAndSettle();

    expect(find.text('This offer is no longer available.'), findsOneWidget);
  });

  testWidgets('tapping an offer notification opens the offer', (tester) async {
    final notes = FakeNotificationRepository()
      ..items.add(AppNotification(id: 'n1', type: 'Offer', title: 'Tomato week', message: 'Fresh tomatoes for less.', isRead: false, createdAt: DateTime.now(), category: 'Offer', campaignId: 'o1'));
    final offers = _Offers(single: _discount);
    await _open(tester, offers, notes: notes);

    await tester.tap(find.byTooltip('Notifications'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Tomato week'));
    await tester.pumpAndSettle();

    expect(offers.asked, ['o1']);
    expect(find.text('Products in this offer'), findsOneWidget);
  });
}
