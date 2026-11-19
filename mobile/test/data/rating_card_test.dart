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
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/repositories/rating_repository.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/fake_adapter.dart';

class _Auth extends AuthNotifier {
  @override
  AuthState build() => const AuthState(AuthStatus.signedIn, CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test'));
}

class _Ratings implements RatingRepository {
  _Ratings({this.canRate = true, this.canRateDelivery = false, this.rating});
  bool canRate, canRateDelivery;
  OrderRating? rating;
  final saved = <({int store, int? delivery, String? comment})>[];
  final reads = <String>[];
  Object? failNext;
  Duration delay = Duration.zero;

  @override
  Future<OrderRatingState> state(String orderId) async {
    reads.add(orderId);
    return OrderRatingState(canRate: canRate, canRateDelivery: canRateDelivery, rating: rating);
  }

  @override
  Future<OrderRating> save(String orderId, {required int storeStars, int? deliveryStars, String? comment}) async {
    if (delay > Duration.zero) await Future<void>.delayed(delay);
    final failure = failNext;
    if (failure != null) {
      failNext = null;
      throw failure;
    }

    saved.add((store: storeStars, delivery: deliveryStars, comment: comment));
    return rating = OrderRating(storeStars: storeStars, deliveryStars: deliveryStars, comment: comment?.trim().isEmpty == true ? null : comment?.trim());
  }
}

Future<void> _open(WidgetTester tester, _Ratings ratings, {OrderStage stage = OrderStage.delivered}) async {
  SharedPreferences.setMockInitialValues({});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 2400 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final orders = FakeOrderRepository()..orders.add(sampleOrder(stage: stage));
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(_Auth.new),
    cartRepositoryProvider.overrideWithValue(FakeCartRepository()),
    orderRepositoryProvider.overrideWithValue(orders),
    ratingRepositoryProvider.overrideWithValue(ratings),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  await tester.tap(find.byTooltip('Orders'));
  await tester.pumpAndSettle();
  await tester.tap(find.text('#ORD-1001'));
  await tester.pumpAndSettle();
}

Future<void> _tap(WidgetTester tester, Key key) async {
  await tester.ensureVisible(find.byKey(key));
  await tester.tap(find.byKey(key));
  await tester.pumpAndSettle();
}

Future<void> _send(WidgetTester tester) async {
  await tester.ensureVisible(find.text('Send rating'));
  await tester.tap(find.text('Send rating'));
  await tester.pumpAndSettle();
}

void main() {
  group('the repository', () {
    late FakeAdapter adapter;
    late ApiRatingRepository repo;
    setUp(() {
      adapter = FakeAdapter();
      repo = ApiRatingRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));
    });

    test('the state tells whether the order can be rated and what was given', () async {
      adapter.replyJson({
        'success': true,
        'data': {
          'canRate': true,
          'canRateDelivery': true,
          'rating': {'orderId': 'o1', 'storeStars': 4, 'deliveryStars': 5, 'comment': 'good', 'canEdit': true}
        }
      });

      final state = await repo.state('o1');

      expect(adapter.requests.single.path, '/api/customer/orders/o1/rating');
      expect((state.canRate, state.canRateDelivery, state.rating!.storeStars, state.rating!.deliveryStars, state.rating!.comment), (true, true, 4, 5, 'good'));
    });

    test('an order with nothing given yet has no rating', () async {
      adapter.replyJson({'success': true, 'data': {'canRate': false, 'canRateDelivery': false, 'rating': null}});

      final state = await repo.state('o1');

      expect((state.canRate, state.rating), (false, null));
    });

    test('saving sends only what was chosen: no delivery stars or comment when left out', () async {
      adapter.replyJson({'success': true, 'data': {'orderId': 'o1', 'storeStars': 5, 'canEdit': true}});

      await repo.save('o1', storeStars: 5, comment: '   ');

      expect(adapter.requests.single.method, 'PUT');
      expect(adapter.requests.single.data, {'storeStars': 5});
    });

    test('saving sends the delivery stars and the trimmed comment', () async {
      adapter.replyJson({'success': true, 'data': {'orderId': 'o1', 'storeStars': 3, 'deliveryStars': 4, 'comment': 'ok', 'canEdit': true}});

      final saved = await repo.save('o1', storeStars: 3, deliveryStars: 4, comment: '  ok ');

      expect(adapter.requests.single.data, {'storeStars': 3, 'deliveryStars': 4, 'comment': 'ok'});
      expect((saved.storeStars, saved.deliveryStars, saved.comment), (3, 4, 'ok'));
    });

    test('a refused rating is a typed failure', () async {
      adapter.replyJson({'success': false, 'message': 'Ratings can be given or changed for 14 days after delivery.'}, status: 409);

      await expectLater(repo.save('o1', storeStars: 3), throwsA(isA<ConflictException>()));
    });
  });

  group('on a delivered order', () {
    testWidgets('asks how it was, and Send waits for a star', (tester) async {
      final ratings = _Ratings();
      await _open(tester, ratings);

      expect(find.text('How was Harbor Point Dark Store?'), findsOneWidget);
      expect(find.byKey(const Key('delivery-star-1')), findsNothing);
      await tester.ensureVisible(find.text('Send rating'));
      await tester.tap(find.text('Send rating'), warnIfMissed: false);
      await tester.pumpAndSettle();
      expect(ratings.saved, isEmpty);
    });

    testWidgets('stars and a comment are sent, then the card shows what was given', (tester) async {
      final ratings = _Ratings();
      await _open(tester, ratings);

      await _tap(tester, const Key('store-star-4'));
      await tester.enterText(find.byKey(const Key('rating-comment')), '  Fresh and quick  ');
      await _send(tester);

      expect(ratings.saved, [(store: 4, delivery: null, comment: '  Fresh and quick  ')]);
      expect(find.text('Thank you for your rating.'), findsOneWidget);
      expect(find.text('Your rating'), findsOneWidget);
      expect(find.text('Fresh and quick'), findsOneWidget);
      expect(find.text('Change rating'), findsOneWidget);
    });

    testWidgets('the delivery is rated only when a partner brought the order, and it is optional', (tester) async {
      final ratings = _Ratings(canRateDelivery: true);
      await _open(tester, ratings);
      expect(find.text('How was the delivery?'), findsOneWidget);

      await _tap(tester, const Key('store-star-5'));
      await _send(tester);

      expect(ratings.saved.single.delivery, isNull);
    });

    testWidgets('both ratings are sent together', (tester) async {
      final ratings = _Ratings(canRateDelivery: true);
      await _open(tester, ratings);

      await _tap(tester, const Key('store-star-5'));
      await _tap(tester, const Key('delivery-star-3'));
      await _send(tester);

      expect(ratings.saved.single.store, 5);
      expect(ratings.saved.single.delivery, 3);
      expect(find.byKey(const Key('given-delivery')), findsOneWidget);
    });

    testWidgets('a given rating can be changed, starting from what was given', (tester) async {
      final ratings = _Ratings(rating: const OrderRating(storeStars: 2, comment: 'slow'));
      await _open(tester, ratings);
      expect(find.text('slow'), findsOneWidget);

      await tester.ensureVisible(find.text('Change rating'));
      await tester.tap(find.text('Change rating'));
      await tester.pumpAndSettle();
      expect(tester.widget<TextField>(find.byKey(const Key('rating-comment'))).controller!.text, 'slow');

      await _tap(tester, const Key('store-star-5'));
      await _send(tester);

      expect(ratings.saved.single.store, 5);
      expect(ratings.rating!.storeStars, 5);
    });

    testWidgets('Cancel while changing keeps the old rating', (tester) async {
      final ratings = _Ratings(rating: const OrderRating(storeStars: 2));
      await _open(tester, ratings);

      await tester.ensureVisible(find.text('Change rating'));
      await tester.tap(find.text('Change rating'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Cancel'));
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();

      expect(find.text('Your rating'), findsOneWidget);
      expect(ratings.saved, isEmpty);
    });

    testWidgets('after the 14 days the rating is shown but cannot be changed', (tester) async {
      await _open(tester, _Ratings(canRate: false, rating: const OrderRating(storeStars: 4, canEdit: false)));

      expect(find.text('Your rating'), findsOneWidget);
      expect(find.text('Change rating'), findsNothing);
    });

    testWidgets('an order that can no longer be rated and was never rated shows nothing', (tester) async {
      await _open(tester, _Ratings(canRate: false));

      expect(find.text('Your rating'), findsNothing);
      expect(find.textContaining('How was'), findsNothing);
    });

    testWidgets('tapping Send twice sends once', (tester) async {
      final ratings = _Ratings()..delay = const Duration(milliseconds: 200);
      await _open(tester, ratings);
      await _tap(tester, const Key('store-star-5'));

      await tester.ensureVisible(find.text('Send rating'));
      await tester.tap(find.text('Send rating'));
      await tester.pump(const Duration(milliseconds: 50));
      await tester.tap(find.text('Please wait…'), warnIfMissed: false);
      await tester.pumpAndSettle();

      expect(ratings.saved, hasLength(1));
    });

    testWidgets('a refusal is explained and the stars the customer chose stay', (tester) async {
      final ratings = _Ratings()..failNext = const ConflictException('Ratings can be given or changed for 14 days after delivery.');
      await _open(tester, ratings);
      await _tap(tester, const Key('store-star-3'));

      await _send(tester);

      expect(find.text('This order can no longer be rated.'), findsOneWidget);
      expect(ratings.saved, isEmpty);
      expect(find.text('Send rating'), findsOneWidget);
    });

    testWidgets('a comment the server refuses is explained', (tester) async {
      final ratings = _Ratings()..failNext = const ValidationException('The comment can be up to 500 characters of plain text.');
      await _open(tester, ratings);
      await _tap(tester, const Key('store-star-3'));
      await tester.enterText(find.byKey(const Key('rating-comment')), 'a <b> tag');

      await _send(tester);

      expect(find.text('Please check your rating. A comment cannot contain < or >.'), findsOneWidget);
    });

    testWidgets('no connection shows the usual message and nothing is lost', (tester) async {
      final ratings = _Ratings()..failNext = const NetworkException('offline');
      await _open(tester, ratings);
      await _tap(tester, const Key('store-star-5'));

      await _send(tester);

      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
      expect(find.text('Send rating'), findsOneWidget);
    });
  });

  group('on an order that is not delivered', () {
    testWidgets('there is no rating card and the server is not asked', (tester) async {
      final ratings = _Ratings();
      await _open(tester, ratings, stage: OrderStage.onTheWay);

      expect(find.textContaining('How was'), findsNothing);
      expect(ratings.reads, isEmpty);
    });
  });
}
