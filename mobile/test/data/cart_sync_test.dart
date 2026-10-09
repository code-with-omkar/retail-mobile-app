import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/commerce_fakes.dart';
import '../support/location_fakes.dart';

class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;
  @override
  AuthState build() => start;
  void become(AuthState next) => state = next;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');
const _signedIn = AuthState(AuthStatus.signedIn, _asha);
const _signedOut = AuthState(AuthStatus.signedOut);
const _store1 = NearestStore(id: 'store-1', name: 'Harbor Point', distanceKm: 1, estimatedMinutes: 10);
const _store2 = NearestStore(id: 'store-2', name: 'Cedar Market', distanceKm: 4, estimatedMinutes: 18);

class _Rig {
  _Rig(this.container, this.repo, this.auth);
  final ProviderContainer container;
  final FakeCartRepository repo;
  final _Auth auth;

  CartNotifier get cart => container.read(cartProvider.notifier);
  Map<String, int> get quantities => container.read(cartProvider);
  CartNotice get notice => container.read(cartNoticeProvider);
  ServerCart? get server => container.read(serverCartProvider);

  /// Lets every started call finish.
  Future<void> idle() async {
    for (var i = 0; i < 4; i++) {
      await Future<void>.delayed(const Duration(milliseconds: 10));
      await cart.settled();
    }
  }
}

Future<_Rig> _rig({AuthState auth = _signedOut, void Function(FakeCartRepository repo)? server, String selected = 'store-1', Map<String, Object> initialPrefs = const {}}) async {
  SharedPreferences.setMockInitialValues(initialPrefs);
  final prefs = await SharedPreferences.getInstance();
  final repo = FakeCartRepository()
    ..know('tomato:1kg', productId: 'tomato', variantId: 'tomato:1kg', name: 'Tomato', price: 28)
    ..know('onion:1kg', productId: 'onion', variantId: 'onion:1kg', name: 'Onion', price: 26)
    ..know('apple:1kg', productId: 'apple', variantId: 'apple:1kg', name: 'Royal Apples', price: 149, stock: 2);
  server?.call(repo);
  final notifier = _Auth(auth);
  final products = ProductStore()..putAll(seed.products);
  final container = ProviderContainer(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(prefs),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => notifier),
    cartRepositoryProvider.overrideWithValue(repo),
    productStoreProvider.overrideWithValue(products),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(const SeedCatalogRepository(), products)),
    selectedStoreProvider.overrideWith((ref) async => switch (selected) { 'store-1' => _store1, 'none' => null, _ => _store2 }),
    serviceableStoresProvider.overrideWith((ref) async => [_store1, _store2]),
  ]);
  addTearDown(container.dispose);
  container.listen(cartProvider, (_, _) {});
  container.listen(serverCartProvider, (_, _) {});
  container.listen(cartNoticeProvider, (_, _) {});
  container.listen(cartChoiceProvider, (_, _) {});
  final rig = _Rig(container, repo, notifier);
  await rig.idle();
  return rig;
}

void main() {
  group('a guest', () {
    test('keeps the cart on the phone and never talks to the server', () async {
      final r = await _rig();

      r.cart.add('tomato:1kg');
      r.cart.add('tomato:1kg');
      r.cart.add('onion:1kg');
      r.cart.remove('onion:1kg');
      await r.idle();

      expect(r.quantities, {'tomato:1kg': 2});
      expect(r.repo.calls, isEmpty);
      expect(r.server, isNull);
    });
  });

  group('where no store delivers', () {
    test('nothing is added, nothing is sent, and the customer is told why', () async {
      final r = await _rig(auth: _signedIn, selected: 'none', initialPrefs: deviceLocationPrefs);
      await r.container.read(selectedStoreProvider.future);

      r.cart.add('tomato:1kg');
      r.cart.setQuantity('onion:1kg', 2);
      await r.idle();

      expect(r.quantities, isEmpty);
      expect(r.repo.calls.where((c) => c != 'current'), isEmpty);
      expect(r.notice.problem, 'We do not deliver to your location yet. Choose another address.');
    });

    test('with no place chosen at all the customer is asked to choose one', () async {
      final r = await _rig(auth: _signedIn, selected: 'none');
      await r.container.read(selectedStoreProvider.future);

      r.cart.add('tomato:1kg');
      await r.idle();

      expect(r.quantities, isEmpty);
      expect(r.notice.problem, 'Choose your delivery location first.');
    });

    test('taking items away still works', () async {
      final r = await _rig(auth: _signedIn, server: (repo) {
        repo.storeId = 'store-1';
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 2, 26));
      }, selected: 'none');
      await r.container.read(selectedStoreProvider.future);

      r.cart.remove('onion:1kg');
      await r.idle();

      expect(r.quantities, {'onion:1kg': 1});
    });
  });

  group('signing in', () {
    test('merges the phone cart into the server and shows what the server holds', () async {
      final r = await _rig();
      r.cart.add('tomato:1kg', 2);
      r.cart.add('onion:1kg');

      r.auth.become(_signedIn);
      await r.idle();

      expect(r.repo.calls.where((c) => c.startsWith('merge')), hasLength(1));
      expect(r.repo.calls.first, 'current');
      expect(r.quantities, {'tomato:1kg': 2, 'onion:1kg': 1});
      expect(r.server!.storeId, 'store-1');
      expect(r.notice.isEmpty, isTrue);
    });

    test('tells the customer what could not be added and shows the reduced quantity', () async {
      final r = await _rig();
      r.cart.add('apple:1kg', 5);
      r.cart.add('tomato:1kg');

      r.auth.become(_signedIn);
      await r.idle();

      expect(r.quantities, {'apple:1kg': 2, 'tomato:1kg': 1}, reason: 'the store only has 2 apples');
      expect(r.notice.notes.map((n) => (n.kind, n.quantity)), [(CartNote.reduced, 2)]);
    });

    test('an empty phone cart just loads the server cart, with its products, and shops in its store', () async {
      final r = await _rig(server: (repo) {
        repo.storeId = 'store-2';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 3, 28));
        repo.lines.add(FakeServerLine('mystery', 'mystery:1', 'Mystery Item', '2 kg', 1, 60));
      });

      r.auth.become(_signedIn);
      await r.idle();

      expect(r.quantities, {'tomato:1kg': 3, 'mystery:1': 1});
      expect(r.repo.calls.where((c) => c.startsWith('merge')), isEmpty);
      expect(r.container.read(selectedStoreIdProvider), 'store-2');
      final totals = r.container.read(cartTotalsProvider);
      expect(totals.lines.map((l) => l.product.name), containsAll(['Tomato', 'Mystery Item']), reason: 'a product the catalogue does not know still shows, from what the cart holds');
      expect(totals.lines.singleWhere((l) => l.product.id == 'mystery').unitPrice, 60);
    });

    test('a signed-in customer opening the app finds the cart they left', () async {
      final r = await _rig(auth: _signedIn, server: (repo) {
        repo.storeId = 'store-1';
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 4, 26));
      });

      expect(r.quantities, {'onion:1kg': 4});
    });

    test('a cart in another store than the phone cart asks which to keep, and changes nothing until answered', () async {
      final r = await _rig(server: (repo) {
        repo.storeId = 'store-2';
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 4, 26));
      });
      r.cart.add('tomato:1kg');

      r.auth.become(_signedIn);
      await r.idle();

      final choice = r.container.read(cartChoiceProvider)!;
      expect(choice.saved.storeId, 'store-2');
      expect(choice.phone, {'tomato:1kg': 1});
      expect(r.repo.calls.where((c) => c.startsWith('merge') || c == 'clear'), isEmpty);
      expect(r.quantities, {'tomato:1kg': 1});
    });

    test('choosing the saved cart drops the phone cart', () async {
      final r = await _rig(server: (repo) {
        repo.storeId = 'store-2';
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 4, 26));
      });
      r.cart.add('tomato:1kg');
      r.auth.become(_signedIn);
      await r.idle();

      await r.cart.keepSavedCart();
      await r.idle();

      expect(r.quantities, {'onion:1kg': 4});
      expect(r.container.read(cartChoiceProvider), isNull);
      expect(r.repo.calls.where((c) => c.startsWith('merge')), isEmpty);
    });

    test('choosing the phone cart empties the saved one and puts the phone cart in its place', () async {
      final r = await _rig(server: (repo) {
        repo.storeId = 'store-2';
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 4, 26));
      });
      r.cart.add('tomato:1kg', 2);
      r.auth.become(_signedIn);
      await r.idle();

      await r.cart.usePhoneCart();
      await r.idle();

      expect(r.quantities, {'tomato:1kg': 2});
      expect(r.repo.lines.map((l) => l.key), ['tomato:1kg']);
      expect(r.repo.storeId, 'store-1');
      expect(r.repo.calls, containsAllInOrder(['clear', 'merge tomato:1kgx2']));
    });

    test('a server that cannot be reached leaves the phone cart alone and says so', () async {
      final r = await _rig();
      r.cart.add('tomato:1kg');
      r.repo.failNext = const NetworkException('offline');

      r.auth.become(_signedIn);
      await r.idle();

      expect(r.quantities, {'tomato:1kg': 1});
      expect(r.notice.problem, 'Cannot reach the server. Check your connection and try again.');
    });
  });

  group('a signed-in customer changing the cart', () {
    test('the first add creates the line, the next sets its quantity, and taking the last away removes it', () async {
      final r = await _rig(auth: _signedIn);

      r.cart.add('tomato:1kg');
      await r.idle();
      r.cart.add('tomato:1kg');
      await r.idle();
      r.cart.remove('tomato:1kg');
      r.cart.remove('tomato:1kg');
      await r.idle();

      expect(r.repo.calls.where((c) => c != 'current'), ['add tomato:1kg x1', 'set tomato:1kg x2', 'set tomato:1kg x1', 'remove tomato:1kg']);
      expect(r.quantities, isEmpty);
      expect(r.server?.lines, isEmpty);
    });

    test('taps faster than the server answers are sent as one change to the final quantity', () async {
      final r = await _rig(auth: _signedIn);
      r.repo.delay = const Duration(milliseconds: 40);

      for (var i = 0; i < 5; i++) {
        r.cart.add('tomato:1kg');
      }
      expect(r.quantities, {'tomato:1kg': 5}, reason: 'the screen moves at once');
      await r.idle();
      await Future<void>.delayed(const Duration(milliseconds: 200));
      await r.idle();

      expect(r.repo.lines.single.quantity, 5);
      expect(r.repo.calls.where((c) => c != 'current').length, lessThanOrEqualTo(2), reason: 'not one call per tap');
      expect(r.quantities, {'tomato:1kg': 5});
    });

    test('a change the server refuses is undone, the cart goes back to what the server holds, and the customer is told', () async {
      final r = await _rig(auth: _signedIn, server: (repo) {
        repo.storeId = 'store-1';
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 1, 26));
      });
      expect(r.quantities, {'onion:1kg': 1});
      r.repo.failNext = const ConflictException('Cart item quantity cannot exceed 1000', statusCode: 409);

      r.cart.add('tomato:1kg');
      expect(r.quantities, {'onion:1kg': 1, 'tomato:1kg': 1}, reason: 'optimistic');
      await r.idle();

      expect(r.quantities, {'onion:1kg': 1});
      expect(r.notice.problem, 'This changed while you were shopping. Please review and try again.');
    });

    test('changes from two phones show after a reload', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.add('tomato:1kg');
      await r.idle();
      r.repo.lines.single.quantity = 7; // changed on another phone

      await r.cart.reload();

      expect(r.quantities, {'tomato:1kg': 7});
    });

    test('a cart emptied elsewhere is empty here after a reload', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.add('tomato:1kg');
      await r.idle();
      r.repo.lines.clear();

      await r.cart.reload();

      expect(r.quantities, isEmpty);
      expect(r.server, isNull);
    });

    test('clearing deletes the cart on the server once, and nothing is sent when there is nothing to delete', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.clear();
      await r.idle();
      expect(r.repo.calls.where((c) => c == 'clear'), isEmpty);
      r.cart.add('tomato:1kg');
      await r.idle();

      r.cart.clear();
      await r.idle();

      expect(r.repo.calls.where((c) => c == 'clear'), hasLength(1));
      expect(r.quantities, isEmpty);
      expect(r.repo.lines, isEmpty);
    });

    test('after clearing, the next item goes to the store being shopped in now, not the old one', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.add('tomato:1kg');
      await r.idle();
      expect(r.repo.storeId, 'store-1');
      r.cart.clear();
      await r.idle();
      r.container.read(selectedStoreIdProvider.notifier).select('store-2');

      r.cart.add('onion:1kg');
      await r.idle();

      expect(r.repo.lines.single.key, 'onion:1kg');
    });

    test('when the order is placed the cart is empty and nothing is deleted (the server already did)', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.add('tomato:1kg');
      await r.idle();
      r.repo.calls.clear();

      r.cart.orderPlaced();
      await r.idle();

      expect(r.quantities, isEmpty);
      expect(r.server, isNull);
      expect(r.repo.calls, isEmpty);
    });

    test('signing out empties the phone and leaves the cart on the server', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.add('tomato:1kg');
      await r.idle();
      r.repo.calls.clear();

      r.auth.become(_signedOut);
      await r.idle();

      expect(r.quantities, isEmpty);
      expect(r.server, isNull);
      expect(r.repo.lines, hasLength(1));
      expect(r.repo.calls, isEmpty);
    });

    test('the same customer signing in again finds the cart where they left it', () async {
      final r = await _rig(auth: _signedIn);
      r.cart.add('tomato:1kg', 3);
      await r.idle();
      r.auth.become(_signedOut);
      await r.idle();

      r.auth.become(_signedIn);
      await r.idle();

      expect(r.quantities, {'tomato:1kg': 3});
    });
  });

  group('fixing a cart checkout would refuse', () {
    Future<_Rig> withProblems() async {
      final r = await _rig(auth: _signedIn, server: (repo) {
        repo.storeId = 'store-1';
        repo.lines.add(FakeServerLine('tomato', 'tomato:1kg', 'Tomato', '1 kg', 2, 28, current: 31));
        repo.lines.add(FakeServerLine('onion', 'onion:1kg', 'Onion', '1 kg', 1, 26, unavailable: true));
        repo.lines.add(FakeServerLine('apple', 'apple:1kg', 'Royal Apples', '1 kg', 3, 149, available: 1));
      });
      return r;
    }

    test('each kind of problem is found from what the server says', () async {
      final r = await withProblems();

      final totals = r.container.read(cartTotalsProvider);

      expect(totals.hasProblems, isTrue);
      expect(totals.priceChanged.map((l) => l.key), ['tomato:1kg']);
      expect(totals.unavailable.map((l) => l.key), ['onion:1kg']);
      expect(totals.notEnoughStock.map((l) => l.key), ['apple:1kg']);
      expect(totals.lines.firstWhere((l) => l.key == 'tomato:1kg').unitPrice, 28, reason: 'checkout charges the price in the cart, so that is the one shown');
    });

    test('updating prices brings the cart to the current price', () async {
      final r = await withProblems();

      await r.cart.reprice();

      final tomato = r.container.read(cartTotalsProvider).lines.firstWhere((l) => l.key == 'tomato:1kg');
      expect((tomato.unitPrice, tomato.priceChanged), (31.0, false));
      expect(r.repo.calls, contains('reprice'));
    });

    test('removing unavailable items takes out only those', () async {
      final r = await withProblems();

      r.cart.removeUnavailable();
      await r.idle();

      expect(r.quantities.keys, unorderedEquals(['tomato:1kg', 'apple:1kg']));
      expect(r.repo.lines.map((l) => l.key), isNot(contains('onion:1kg')));
    });

    test('using the available quantity lowers short lines to what is left', () async {
      final r = await withProblems();

      r.cart.useAvailableQuantities();
      await r.idle();

      expect(r.quantities['apple:1kg'], 1);
      expect(r.quantities['tomato:1kg'], 2);
      expect(r.repo.lines.firstWhere((l) => l.key == 'apple:1kg').quantity, 1);
    });

    test('a line with none left is removed rather than set to zero', () async {
      final r = await _rig(auth: _signedIn, server: (repo) {
        repo.storeId = 'store-1';
        repo.lines.add(FakeServerLine('apple', 'apple:1kg', 'Royal Apples', '1 kg', 3, 149, available: 0));
      });

      r.cart.useAvailableQuantities();
      await r.idle();

      expect(r.quantities, isEmpty);
      expect(r.repo.lines, isEmpty);
    });
  });

  group('the bill', () {
    test('a guest sees the server fee settings applied to the cart', () async {
      final r = await _rig();
      await r.container.read(pricingProvider.future);

      r.cart.add('tomato:1kg', 2);
      final totals = r.container.read(cartTotalsProvider);

      expect((totals.subtotal, totals.delivery, totals.handling, totals.total, totals.awayFromFreeDelivery, totals.feesKnown), (56.0, 25.0, 5.0, 86.0, 143.0, true));
    });

    test('delivery is free from the threshold', () async {
      final r = await _rig();
      await r.container.read(pricingProvider.future);

      r.cart.add('apple:1kg', 2);
      final totals = r.container.read(cartTotalsProvider);

      expect((totals.delivery, totals.total, totals.awayFromFreeDelivery), (0.0, 303.0, 0.0));
    });

    test('before the fee settings arrive no fees are shown, rather than wrong ones', () async {
      final r = await _rig();
      r.cart.add('tomato:1kg');

      final totals = r.container.read(cartTotalsProvider);

      expect((totals.feesKnown, totals.delivery, totals.total), (false, 0.0, 28.0));
    });
  });
}
