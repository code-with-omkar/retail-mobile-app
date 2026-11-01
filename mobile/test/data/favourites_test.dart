import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/widgets.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/favourites_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/favourites/favourites_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/auth_fakes.dart';
import '../support/fake_adapter.dart';

class _Repo implements FavouritesRepository {
  _Repo([List<String> start = const []]) : saved = List.of(start);
  final List<String> saved;
  final calls = <String>[];
  Object? failNext;

  void _maybeFail() {
    final failure = failNext;
    if (failure != null) {
      failNext = null;
      throw failure;
    }
  }

  @override
  Future<List<String>> ids() async {
    calls.add('ids');
    _maybeFail();
    return List.of(saved);
  }

  @override
  Future<void> add(String productId) async {
    calls.add('add $productId');
    _maybeFail();
    if (!saved.contains(productId)) saved.insert(0, productId);
  }

  @override
  Future<void> remove(String productId) async {
    calls.add('remove $productId');
    _maybeFail();
    saved.remove(productId);
  }
}

class _Auth extends AuthNotifier {
  _Auth(this.start);
  AuthState start;
  @override
  AuthState build() => start;
  void signOutNow() => state = const AuthState(AuthStatus.signedOut);
  void signInNow() => state = const AuthState(AuthStatus.signedIn, CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test'));
}

const _signedIn = AuthState(AuthStatus.signedIn, CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test'));

Future<({ProviderContainer container, _Repo repo, _Auth auth})> _open(WidgetTester tester, {bool signedIn = true, List<String> saved = const []}) async {
  SharedPreferences.setMockInitialValues({});
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 1800 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final repo = _Repo(saved);
  final auth = _Auth(signedIn ? _signedIn : const AuthState(AuthStatus.signedOut));
  final products = ProductStore()..putAll(seed.products);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => auth),
    sessionStoreProvider.overrideWithValue(MemorySessionStore()),
    favouritesRepositoryProvider.overrideWithValue(repo),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  return (container: ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))), repo: repo, auth: auth);
}

Future<void> _openProduct(WidgetTester tester, String id) async {
  GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/product/$id');
  await tester.pumpAndSettle();
}

void main() {
  group('the repository', () {
    late FakeAdapter adapter;
    late ApiFavouritesRepository repo;
    setUp(() {
      final harness = AuthHarness();
      adapter = harness.adapter;
      repo = ApiFavouritesRepository(harness.api);
    });

    test('ids reads the list, newest first', () async {
      adapter.replyJson({'success': true, 'data': ['p2', 'p1']});

      expect(await repo.ids(), ['p2', 'p1']);
      expect(adapter.requests.single.path, '/api/customer/favourites/ids');
    });

    test('an empty answer is an empty list', () async {
      adapter.replyJson({'success': true, 'data': []});
      expect(await repo.ids(), isEmpty);
    });

    test('saving is a PUT and removing is a DELETE on the product', () async {
      adapter
        ..replyJson({'success': true, 'data': true})
        ..replyJson({'success': true, 'data': true});

      await repo.add('p1');
      await repo.remove('p1');

      expect(adapter.requests.map((r) => '${r.method} ${r.path}'), ['PUT /api/customer/favourites/p1', 'DELETE /api/customer/favourites/p1']);
    });

    test('the limit is a conflict the app can show', () async {
      adapter.replyJson({'success': false, 'message': 'You can save up to 200 products.'}, status: 409);

      await expectLater(repo.add('p1'), throwsA(isA<ConflictException>()));
    });
  });

  group('the list in the app', () {
    testWidgets('a signed-in customer\'s list is read at start and shown as hearts', (tester) async {
      final app = await _open(tester, saved: ['tomato']);

      expect(app.repo.calls, ['ids']);
      expect(app.container.read(favouritesProvider).ids, ['tomato']);
      expect(app.container.read(favouritesProvider).loaded, isTrue);
    });

    testWidgets('a guest has no list and nothing is asked of the server', (tester) async {
      final app = await _open(tester, signedIn: false);

      expect(app.repo.calls, isEmpty);
      expect(app.container.read(favouritesProvider).ids, isEmpty);
    });

    testWidgets('signing in reads the list and signing out clears it', (tester) async {
      final app = await _open(tester, signedIn: false, saved: ['tomato']);

      app.auth.signInNow();
      await tester.pumpAndSettle();
      expect(app.container.read(favouritesProvider).ids, ['tomato']);

      app.auth.signOutNow();
      await tester.pumpAndSettle();
      expect(app.container.read(favouritesProvider).ids, isEmpty);
    });

    testWidgets('saving puts the product first at once and tells the server', (tester) async {
      final app = await _open(tester, saved: ['onion']);

      final ok = await app.container.read(favouritesProvider.notifier).toggle('tomato');

      expect(ok, isTrue);
      expect(app.container.read(favouritesProvider).ids, ['tomato', 'onion']);
      expect(app.repo.calls.last, 'add tomato');
    });

    testWidgets('removing takes it off and tells the server', (tester) async {
      final app = await _open(tester, saved: ['tomato', 'onion']);

      await app.container.read(favouritesProvider.notifier).toggle('tomato');

      expect(app.container.read(favouritesProvider).ids, ['onion']);
      expect(app.repo.calls.last, 'remove tomato');
    });

    testWidgets('if the server refuses, the heart goes back to how it was', (tester) async {
      final app = await _open(tester, saved: ['onion']);
      app.repo.failNext = const NetworkException('offline');

      final ok = await app.container.read(favouritesProvider.notifier).toggle('tomato');

      expect(ok, isFalse);
      expect(app.container.read(favouritesProvider).ids, ['onion']);
    });

    testWidgets('a failed read leaves the list as it was', (tester) async {
      final app = await _open(tester, saved: ['tomato']);
      app.repo.failNext = const NetworkException('offline');

      await app.container.read(favouritesProvider.notifier).reload();

      expect(app.container.read(favouritesProvider).ids, ['tomato']);
    });
  });

  group('the product screen heart', () {
    testWidgets('shows whether the product is saved', (tester) async {
      await _open(tester, saved: ['tomato']);
      await _openProduct(tester, 'tomato');
      expect(find.byIcon(Icons.favorite), findsOneWidget);

      GoRouter.of(tester.element(find.byType(Scaffold).first)).pushReplacement('/product/onion');
      await tester.pumpAndSettle();
      expect(find.byIcon(Icons.favorite_border), findsOneWidget);
    });

    testWidgets('tapping saves it, tapping again removes it', (tester) async {
      final app = await _open(tester);
      await _openProduct(tester, 'tomato');

      await tester.tap(find.byTooltip('Save for later'));
      await tester.pumpAndSettle();
      expect(app.repo.saved, ['tomato']);
      expect(find.byIcon(Icons.favorite), findsOneWidget);

      await tester.tap(find.byTooltip('Remove from saved items'));
      await tester.pumpAndSettle();
      expect(app.repo.saved, isEmpty);
      expect(find.byIcon(Icons.favorite_border), findsOneWidget);
    });

    testWidgets('a refusal puts the heart back and says so', (tester) async {
      final app = await _open(tester);
      await _openProduct(tester, 'tomato');
      app.repo.failNext = const ConflictException('You can save up to 200 products.');

      await tester.tap(find.byTooltip('Save for later'));
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.favorite_border), findsOneWidget);
      expect(find.text('Could not update your saved items. Please try again.'), findsOneWidget);
    });

    testWidgets('a guest who taps the heart is taken to sign in, and nothing is saved', (tester) async {
      final app = await _open(tester, signedIn: false);
      await _openProduct(tester, 'tomato');

      await tester.tap(find.byTooltip('Save for later'));
      await tester.pumpAndSettle();

      expect(find.text('Continue with phone'), findsOneWidget);
      expect(app.repo.calls, isEmpty);
    });
  });

  group('the Saved items screen', () {
    testWidgets('shows the saved products, newest first, from Profile', (tester) async {
      await _open(tester, saved: ['onion', 'tomato']);

      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(find.text('Saved items'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Saved items'));
      await tester.pumpAndSettle();

      expect(tester.widgetList<ProductCard>(find.byType(ProductCard)).map((card) => card.product.id), ['onion', 'tomato']);
    });

    testWidgets('says so when nothing is saved', (tester) async {
      await _open(tester);

      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/favourites');
      await tester.pumpAndSettle();

      expect(find.text('Nothing saved yet'), findsOneWidget);
      expect(find.text('Browse products'), findsOneWidget);
    });

    testWidgets('a product that can no longer be read is left out, the others stay', (tester) async {
      await _open(tester, saved: ['ghost', 'tomato']);

      GoRouter.of(tester.element(find.byType(Scaffold).first)).push('/favourites');
      await tester.pumpAndSettle();

      expect(tester.widgetList<ProductCard>(find.byType(ProductCard)).map((card) => card.product.id), ['tomato']);
      expect(find.text('Nothing saved yet'), findsNothing);
    });

    testWidgets('Profile offers Saved items only to someone signed in', (tester) async {
      await _open(tester, signedIn: false);

      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();

      expect(find.text('Saved items'), findsNothing);
    });
  });
}
