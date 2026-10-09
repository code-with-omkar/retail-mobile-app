import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/widgets.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/auth_dto.dart';
import 'package:quickcart_customer/data/location_services.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/repositories/cart_repository.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/local_shop.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';
import 'package:quickcart_customer/data/repositories/product_store.dart';
import 'package:quickcart_customer/data/seed.dart' as seed;
import 'package:quickcart_customer/features/address/address_controller.dart';
import 'package:quickcart_customer/features/address/delivery_actions.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:quickcart_customer/features/cart/cart_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/location_fakes.dart';

class _Auth extends AuthNotifier {
  _Auth(this.start);
  final AuthState start;
  @override
  AuthState build() => start;
}

const _asha = CustomerProfile(id: 'u1', fullName: 'Asha Patil', email: 'asha@example.test', phoneNumber: '9876543210');

/// The seed catalogue, except that stores only deliver near Mumbai, so an address elsewhere is not served.
class _GeoCatalog implements CatalogRepository {
  final _seed = const SeedCatalogRepository();

  @override
  Future<List<Category>> categories({String? storeId}) => _seed.categories();

  @override
  Future<List<NearestStore>> stores({required double latitude, required double longitude}) async => FakeAddressRepository.servesPoint(latitude, longitude) ? _seed.stores(latitude: latitude, longitude: longitude) : const [];

  @override
  Future<NearestStore?> nearestStore({required double latitude, required double longitude}) async => (await stores(latitude: latitude, longitude: longitude)).firstOrNull;

  @override
  Future<ProductPage> products({String? search, String? categoryId, String? storeId, bool carriedOnly = false, int page = 1, int pageSize = 20}) => _seed.products(search: search, categoryId: categoryId, storeId: storeId, page: page, pageSize: pageSize);

  @override
  Future<Product> product(String id, {String? storeId}) => _seed.product(id, storeId: storeId);
}

class _App {
  _App(this.container, this.repo, this.location, this.geocoding, this.shop);
  final LocalShop shop;
  final ProviderContainer container;
  final FakeAddressRepository repo;
  final FakeLocationService location;
  final FakeGeocodingService geocoding;
}

Future<_App> _pump(
  WidgetTester tester, {
  Map<String, Object> prefs = const {},
  AuthState auth = const AuthState(AuthStatus.signedOut),
  FakeAddressRepository? repo,
  FakeLocationService? location,
  double height = 1500,
}) async {
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = Size(390 * 3, height * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  final addresses = repo ?? FakeAddressRepository();
  final locator = location ?? FakeLocationService();
  final geocoder = FakeGeocodingService();
  final products = ProductStore()..putAll(seed.products);
  final shop = LocalShop();
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid')),
    authProvider.overrideWith(() => _Auth(auth)),
    addressRepositoryProvider.overrideWithValue(addresses),
    locationServiceProvider.overrideWithValue(locator),
    geocodingServiceProvider.overrideWithValue(geocoder),
    productStoreProvider.overrideWithValue(products),
    cartRepositoryProvider.overrideWithValue(LocalCartRepository(shop)),
    orderRepositoryProvider.overrideWithValue(LocalOrderRepository(shop)),
    catalogRepositoryProvider.overrideWithValue(CachingCatalogRepository(_GeoCatalog(), products)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
  return _App(ProviderScope.containerOf(tester.element(find.byType(QuickCartApp))), addresses, locator, geocoder, shop);
}

void _go(WidgetTester tester, String path, {Object? extra}) => GoRouter.of(tester.element(find.byType(Scaffold).first)).push(path, extra: extra);

Future<void> _tap(WidgetTester tester, Finder finder) async {
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> _typeInto(WidgetTester tester, int fieldIndex, String text) async {
  await tester.enterText(find.byType(TextFormField).at(fieldIndex), text);
  await tester.pump();
}

void main() {
  group('Home asks where to deliver', () {
    testWidgets('with no place chosen it shows the prompt instead of a store', (tester) async {
      await _pump(tester);

      expect(find.text('Where should we deliver?'), findsOneWidget);
      expect(find.text('Use my current location'), findsOneWidget);
      expect(find.text('Enter an address'), findsOneWidget);
      expect(find.text('Choose location'), findsOneWidget, reason: 'the header');
      expect(find.text('SHOPPING FROM'), findsNothing);
      expect(find.text('Stores that deliver to you'), findsNothing);
    });

    testWidgets('using the current location sets the place, remembers it and shows the stores', (tester) async {
      final app = await _pump(tester);

      await _tap(tester, find.text('Use my current location'));

      expect(app.location.asked, 1);
      expect(find.text('Where should we deliver?'), findsNothing);
      expect(find.text('SHOPPING FROM'), findsOneWidget);
      expect(find.text('Current location · 12 Marine Drive'), findsOneWidget);
      final stored = (await SharedPreferences.getInstance()).getString('device_place')!;
      expect(stored, contains('19.076'));
      expect(app.repo.calls, isEmpty, reason: 'a guest never touches saved addresses');
    });

    testWidgets('a place without readable address text still works and says so', (tester) async {
      final app = await _pump(tester);
      app.geocoding.reverseResult = null;

      await _tap(tester, find.text('Use my current location'));

      expect(find.text('SHOPPING FROM'), findsOneWidget);
      expect(find.textContaining('Current location'), findsWidgets);
    });

    testWidgets('permission denied: explains, keeps the prompt, nothing is saved', (tester) async {
      final app = await _pump(tester, location: FakeLocationService(const LocationFix(LocationAccess.denied)));

      await _tap(tester, find.text('Use my current location'));

      expect(find.text('We need permission to use your location. You can enter an address instead.'), findsOneWidget);
      expect(find.text('Where should we deliver?'), findsOneWidget);
      expect((await SharedPreferences.getInstance()).getString('device_place'), isNull);
      expect(app.location.opened, isEmpty);
    });

    testWidgets('permission denied for good: explains and offers the app settings', (tester) async {
      final app = await _pump(tester, location: FakeLocationService(const LocationFix(LocationAccess.deniedForever)));

      await _tap(tester, find.text('Use my current location'));
      expect(find.text('Location permission needed'), findsOneWidget);

      await tester.tap(find.text('Open settings'));
      await tester.pumpAndSettle();

      expect(app.location.opened, [LocationAccess.deniedForever]);
      expect(find.text('Where should we deliver?'), findsOneWidget);
    });

    testWidgets('location switched off: explains and offers the location settings, or not now', (tester) async {
      final app = await _pump(tester, location: FakeLocationService(const LocationFix(LocationAccess.servicesOff)));

      await _tap(tester, find.text('Use my current location'));
      expect(find.text('Location is off'), findsOneWidget);
      await tester.tap(find.text('Not now'));
      await tester.pumpAndSettle();
      expect(app.location.opened, isEmpty);

      await _tap(tester, find.text('Use my current location'));
      await tester.tap(find.text('Open settings'));
      await tester.pumpAndSettle();
      expect(app.location.opened, [LocationAccess.servicesOff]);
    });

    testWidgets('no position arrived: says so and the customer can enter an address', (tester) async {
      await _pump(tester, location: FakeLocationService(const LocationFix(LocationAccess.unavailable)));

      await _tap(tester, find.text('Use my current location'));

      expect(find.text('Could not get your location. Try again, or enter an address.'), findsOneWidget);
      expect(find.text('Enter an address'), findsOneWidget);
    });
  });

  group('a guest entering an address', () {
    testWidgets('find it, see that stores deliver there, use it', (tester) async {
      final app = await _pump(tester);
      await _tap(tester, find.text('Enter an address'));
      expect(find.text('Delivery location'), findsWidgets);
      expect(find.text('Receiver name'), findsNothing, reason: 'a guest only sets a location');

      await _typeInto(tester, 0, 'Hill Road, Bandra West, Mumbai');
      await _tap(tester, find.text('Find this address'));

      expect(app.geocoding.forwardQueries, ['Hill Road, Bandra West, Mumbai']);
      expect(find.text('We deliver here'), findsOneWidget);
      expect(find.text('19.0760, 72.8777'), findsOneWidget);

      await _tap(tester, find.text('Use this location'));

      expect(find.text('SHOPPING FROM'), findsOneWidget);
      expect(find.text('Delivery location · Bandra West'), findsOneWidget);
      expect((await SharedPreferences.getInstance()).getString('device_place'), contains('Hill Road, Bandra West, Mumbai'));
    });

    testWidgets('an address nothing matches is explained, and one that needs no lookup service points to alternatives', (tester) async {
      final app = await _pump(tester);
      await _tap(tester, find.text('Enter an address'));
      await _typeInto(tester, 0, 'zzzz');

      app.geocoding.forwardResult = null;
      await _tap(tester, find.text('Find this address'));
      expect(find.text('We could not find this address. Check it, or use your current location.'), findsOneWidget);

      app.geocoding.unavailable = true;
      await _tap(tester, find.text('Find this address'));
      expect(find.text('Address lookup is not available right now. Use your current location, or enter coordinates below.'), findsOneWidget);
      expect(find.text('Location set'), findsNothing);
    });

    testWidgets('finding an empty address, or saving without a location, is refused with a reason', (tester) async {
      await _pump(tester);
      await _tap(tester, find.text('Enter an address'));

      await _tap(tester, find.text('Find this address'));
      expect(find.text('Type the address first.'), findsOneWidget);

      await _typeInto(tester, 0, 'Hill Road, Mumbai');
      await _tap(tester, find.text('Use this location'));
      expect(find.text('Set the location first: find the address or use your current location.'), findsOneWidget);
      expect((await SharedPreferences.getInstance()).getString('device_place'), isNull);
    });

    testWidgets('coordinates can be entered by hand, and impossible ones are rejected', (tester) async {
      await _pump(tester);
      await _tap(tester, find.text('Enter an address'));
      await _tap(tester, find.text('Enter coordinates yourself'));

      await _typeInto(tester, 1, '95');
      await _typeInto(tester, 2, '72.8');
      await _tap(tester, find.text('Use these coordinates'));
      expect(find.text('Enter valid coordinates, for example 19.0760 and 72.8777.'), findsOneWidget);

      await _typeInto(tester, 1, '19,07');
      await _typeInto(tester, 2, '72.87');
      await _tap(tester, find.text('Use these coordinates'));
      expect(find.text('19.0700, 72.8700'), findsOneWidget);
      expect(find.text('We deliver here'), findsOneWidget);
    });

    testWidgets('a place nobody delivers to is shown as such, and Home offers another address', (tester) async {
      final app = await _pump(tester);
      app.geocoding.forwardResult = (latitude: 28.61, longitude: 77.21);
      await _tap(tester, find.text('Enter an address'));
      await _typeInto(tester, 0, 'Connaught Place, New Delhi');
      await _tap(tester, find.text('Find this address'));

      expect(find.text('We do not deliver here yet'), findsOneWidget);
      expect(find.textContaining('You can still save it'), findsOneWidget);

      await _tap(tester, find.text('Use this location'));

      expect(find.text('We do not deliver here yet'), findsOneWidget);
      expect(find.text('Try another address'), findsOneWidget);
      expect(find.text('Stores that deliver to you'), findsNothing);
    });

    testWidgets('the address screen shows the current place and invites signing in to save addresses', (tester) async {
      await _pump(tester, prefs: deviceLocationPrefs);
      await _tap(tester, find.text('DELIVER TO'));

      expect(find.text('Delivery addresses'), findsOneWidget);
      expect(find.text('Delivering to'), findsOneWidget);
      expect(find.textContaining('Flat 402, Sea Breeze Apts'), findsOneWidget);
      expect(find.text('Save addresses to your account'), findsOneWidget);
      expect(find.text('Sign in / Register'), findsOneWidget);
    });
  });

  group('saved addresses', () {
    Future<_App> signedIn(WidgetTester tester, {void Function(FakeAddressRepository)? seedRepo, Map<String, Object> prefs = const {}, FakeAddressRepository? repo}) async {
      final r = repo ?? FakeAddressRepository();
      seedRepo?.call(r);
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: r, prefs: prefs);
      return app;
    }

    void twoAddresses(FakeAddressRepository r) => r
      ..seed('Home', isDefault: true, flat: 'Flat 4')
      ..seed('Work', line: '12th Floor, Trade Centre, BKC, Mumbai', latitude: 19.065, longitude: 72.868);

    testWidgets('Home delivers to the default address, and the list shows every address with its state', (tester) async {
      await signedIn(tester, seedRepo: (r) {
        twoAddresses(r);
        r.seed('Delhi', latitude: 28.61, longitude: 77.21, line: 'Connaught Place, Delhi');
      });
      expect(find.text('Home · 12 Marine Drive'), findsOneWidget);

      await _tap(tester, find.text('DELIVER TO'));

      expect(find.text('Delivery addresses'), findsOneWidget);
      expect(find.text('Home'), findsOneWidget);
      expect(find.text('Work'), findsOneWidget);
      expect(find.text('Delhi'), findsOneWidget);
      expect(find.text('Default'), findsOneWidget);
      expect(find.text('Flat 4, 12 Marine Drive, Mumbai'), findsOneWidget);
      expect(find.text('Asha Patil · 9876543210'), findsNWidgets(3));
      expect(find.byIcon(Icons.check_circle), findsOneWidget, reason: 'the address being delivered to');
      expect(find.text('We do not deliver here yet'), findsOneWidget, reason: 'only the Delhi address, as a badge');
    });

    testWidgets('choosing another address delivers to it and goes back', (tester) async {
      final app = await signedIn(tester, seedRepo: twoAddresses);
      await _tap(tester, find.text('DELIVER TO'));

      await _tap(tester, find.text('Work'));

      expect(find.text('Delivery addresses'), findsNothing);
      expect(find.text('Work · BKC'), findsOneWidget);
      expect(app.container.read(selectedAddressIdProvider), 'a2');
      expect((await SharedPreferences.getInstance()).getString('selected_address_id'), 'a2');
    });

    testWidgets('making an address the default and deleting one go to the server; deleting asks first', (tester) async {
      final app = await signedIn(tester, seedRepo: twoAddresses);
      await _tap(tester, find.text('DELIVER TO'));

      await _tap(tester, find.byTooltip('More').last);
      await _tap(tester, find.text('Make default'));
      expect(app.repo.calls, contains('default a2'));
      expect(find.text('Default'), findsOneWidget);

      await _tap(tester, find.byTooltip('More').last);
      await _tap(tester, find.text('Delete'));
      expect(find.text('Delete this address?'), findsOneWidget);
      await _tap(tester, find.text('Cancel'));
      expect(app.repo.addresses, hasLength(2));

      await _tap(tester, find.byTooltip('More').last);
      await _tap(tester, find.text('Delete'));
      await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Delete')));
      await tester.pumpAndSettle();
      expect(app.repo.addresses.map((a) => a.id), ['a2']);
      expect(find.text('Home'), findsNothing);
    });

    testWidgets('a failed delete says why and keeps the address', (tester) async {
      final app = await signedIn(tester, seedRepo: twoAddresses);
      await _tap(tester, find.text('DELIVER TO'));
      app.repo.failNext = const NotFoundException('gone', statusCode: 404);

      await _tap(tester, find.byTooltip('More').last);
      await _tap(tester, find.text('Delete'));
      await tester.tap(find.descendant(of: find.byType(AlertDialog), matching: find.text('Delete')));
      await tester.pumpAndSettle();

      expect(find.text('This address no longer exists.'), findsOneWidget);
    });

    testWidgets('with no address yet the list explains and offers to add one', (tester) async {
      await signedIn(tester);
      expect(find.text('Where should we deliver?'), findsOneWidget);
      await _tap(tester, find.text('Choose location'));

      expect(find.textContaining('You have no saved addresses yet'), findsOneWidget);
      expect(find.text('Add new address'), findsOneWidget);
    });
  });

  group('the address editor', () {
    Future<_App> openNew(WidgetTester tester, {FakeAddressRepository? repo}) async {
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo ?? FakeAddressRepository(), height: 2200);
      await _tap(tester, find.text('Enter an address'));
      return app;
    }

    testWidgets('the receiver is filled in from the profile and every field is there', (tester) async {
      await openNew(tester);

      expect(find.text('Add address'), findsWidgets);
      expect(find.text('Receiver name'), findsOneWidget);
      expect(find.text('Asha Patil'), findsOneWidget);
      expect(find.text('9876543210'), findsOneWidget);
      expect(find.text('Flat / house no. / building'), findsOneWidget);
      expect(find.text('Landmark (optional)'), findsOneWidget);
      for (final label in ['Home', 'Work', 'Other']) {
        expect(find.text(label), findsOneWidget);
      }
    });

    testWidgets('saving sends the typed fields and the found point, selects the new address and returns to Home', (tester) async {
      final app = await openNew(tester);
      await _typeInto(tester, 0, 'Hill Road, Bandra West, Mumbai');
      await _tap(tester, find.text('Find this address'));
      await _typeInto(tester, 1, 'Flat 4');
      await _typeInto(tester, 2, 'Near the station');
      await _tap(tester, find.text('Work'));

      await _tap(tester, find.text('Save address'));

      final saved = app.repo.addresses.single;
      expect((saved.label, saved.line, saved.flatOrBuilding, saved.landmark), ('Work', 'Hill Road, Bandra West, Mumbai', 'Flat 4', 'Near the station'));
      expect((saved.latitude, saved.longitude, saved.receiverName, saved.receiverPhone), (19.076, 72.8777, 'Asha Patil', '9876543210'));
      expect(saved.isDefault, isTrue, reason: 'the first address becomes the default');
      expect(find.text('SHOPPING FROM'), findsOneWidget);
      expect(find.text('Work · Bandra West'), findsOneWidget);
      expect(app.container.read(deliveryPlaceProvider)?.addressId, saved.id, reason: 'the new default is where we deliver');
    });

    testWidgets('a custom name is used when Other is chosen, and is required', (tester) async {
      final app = await openNew(tester);
      await _typeInto(tester, 0, 'Hill Road, Mumbai');
      await _tap(tester, find.text('Find this address'));
      await _tap(tester, find.text('Other'));

      await _tap(tester, find.text('Save address'));
      expect(app.repo.addresses, isEmpty);
      expect(find.text('Required'), findsOneWidget);

      await _typeInto(tester, 5, 'Mum and Dad');
      await _tap(tester, find.text('Save address'));
      expect(app.repo.addresses.single.label, 'Mum and Dad');
    });

    testWidgets('missing or wrong details are caught before anything is sent', (tester) async {
      final app = await openNew(tester);
      await _typeInto(tester, 3, '');
      await _typeInto(tester, 4, '12345');

      await _tap(tester, find.text('Save address'));

      expect(app.repo.calls.where((c) => c == 'create'), isEmpty);
      expect(find.text('Required'), findsWidgets);
      expect(find.text('Enter a phone number with 10 to 15 digits.'), findsOneWidget);
      expect(find.text('Set the location first: find the address or use your current location.'), findsOneWidget);
    });

    testWidgets('the limit is explained, not shown as a crash', (tester) async {
      final repo = FakeAddressRepository(limit: 1)..seed('Home', isDefault: true);
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo, height: 2200);
      await _tap(tester, find.text('DELIVER TO'));
      await _tap(tester, find.text('Add new address'));
      await _typeInto(tester, 0, 'Hill Road, Mumbai');
      await _tap(tester, find.text('Find this address'));

      await _tap(tester, find.text('Save address'));

      expect(find.text('You have reached the limit of saved addresses. Delete one to add another.'), findsOneWidget);
      expect(app.repo.addresses, hasLength(1));
      expect(find.text('Add address'), findsWidgets, reason: 'still on the editor, the input is kept');
    });

    testWidgets('messages the server wrote for the customer are shown as they are', (tester) async {
      final app = await openNew(tester);
      await _typeInto(tester, 0, 'Hill Road, Mumbai');
      await _tap(tester, find.text('Find this address'));
      app.repo.failNext = const ValidationException('bad', statusCode: 400, errors: ['Pick the location on the map.', 'Who will receive the delivery?']);

      await _tap(tester, find.text('Save address'));

      expect(find.textContaining('Pick the location on the map.'), findsOneWidget);
      expect(find.textContaining('Who will receive the delivery?'), findsOneWidget);
    });

    testWidgets('an unexpected failure shows translated text, never the exception', (tester) async {
      final app = await openNew(tester);
      await _typeInto(tester, 0, 'Hill Road, Mumbai');
      await _tap(tester, find.text('Find this address'));
      app.repo.failNext = const NetworkException('SocketException: connection refused');

      await _tap(tester, find.text('Save address'));

      expect(find.text('Cannot reach the server. Check your connection and try again.'), findsOneWidget);
      expect(find.textContaining('SocketException'), findsNothing);
    });

    testWidgets('editing starts from the saved values and sends an update', (tester) async {
      final repo = FakeAddressRepository();
      final home = repo.seed('Home', isDefault: true, flat: 'Flat 4');
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo, height: 2200);
      _go(tester, '/addresses/${home.id}/edit', extra: AddressEditorArgs(existing: home));
      await tester.pumpAndSettle();

      expect(find.text('Edit address'), findsWidgets);
      expect(find.text('12 Marine Drive, Mumbai'), findsOneWidget);
      expect(find.text('Flat 4'), findsOneWidget);
      expect(find.text('We deliver here'), findsOneWidget);

      await _typeInto(tester, 1, 'Flat 9');
      await _tap(tester, find.text('Save address'));

      expect(app.repo.calls, contains('update a1'));
      expect(app.repo.addresses.single.flatOrBuilding, 'Flat 9');
    });

    testWidgets('an address that is not served is flagged but can still be saved', (tester) async {
      final app = await openNew(tester);
      app.geocoding.forwardResult = (latitude: 28.61, longitude: 77.21);
      await _typeInto(tester, 0, 'Connaught Place, New Delhi');
      await _tap(tester, find.text('Find this address'));
      expect(find.text('We do not deliver here yet'), findsOneWidget);

      await _tap(tester, find.text('Save address'));

      expect(app.repo.addresses.single.serviceable, isFalse);
    });

    testWidgets('use my current location fills in the address and the point', (tester) async {
      await openNew(tester);

      await _tap(tester, find.text('Use my current location'));

      expect(find.text('12 Marine Drive, Mumbai'), findsOneWidget);
      expect(find.text('We deliver here'), findsOneWidget);
    });

    testWidgets('for a signed-in customer the current location opens the editor to save it as an address', (tester) async {
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), height: 2200);

      await _tap(tester, find.text('Use my current location'));

      expect(app.location.asked, 1);
      expect(find.text('Add address'), findsWidgets);
      expect(find.text('12 Marine Drive, Mumbai'), findsOneWidget);
      expect(find.text('Location set'), findsNothing, reason: 'it already says whether stores deliver there');
      expect(find.text('We deliver here'), findsOneWidget);
    });
  });

  group('the cart follows the address', () {
    Future<_App> withCart(WidgetTester tester, FakeAddressRepository repo) async {
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo);
      app.container.read(cartProvider.notifier).add('tomato:1kg');
      await tester.pumpAndSettle();
      return app;
    }

    FakeAddressRepository homeWorkDelhi() => FakeAddressRepository()
      ..seed('Home', isDefault: true)
      ..seed('Work', latitude: 19.065, longitude: 72.868)
      ..seed('Delhi', latitude: 28.61, longitude: 77.21, line: 'Connaught Place, Delhi');

    testWidgets('another address the cart store also serves changes nothing about the cart', (tester) async {
      final app = await withCart(tester, homeWorkDelhi());
      await _tap(tester, find.text('DELIVER TO'));

      await _tap(tester, find.text('Work'));

      expect(find.text('Change delivery address?'), findsNothing);
      expect(app.container.read(cartProvider), {'tomato:1kg': 1});
      expect(app.container.read(selectedAddressIdProvider), 'a2');
    });

    testWidgets('an address the cart store does not serve asks first; keeping the current address changes nothing', (tester) async {
      final app = await withCart(tester, homeWorkDelhi());
      await _tap(tester, find.text('DELIVER TO'));

      await _tap(tester, find.text('Delhi'));
      expect(find.text('Change delivery address?'), findsOneWidget);
      expect(find.textContaining('does not deliver to this address'), findsOneWidget);
      await tester.tap(find.text('Keep current address'));
      await tester.pumpAndSettle();

      expect(app.container.read(cartProvider), {'tomato:1kg': 1});
      expect(app.container.read(selectedAddressIdProvider), isNull);
      expect(find.text('Delivery addresses'), findsOneWidget, reason: 'still choosing');
    });

    testWidgets('agreeing empties the cart and switches the address', (tester) async {
      final app = await withCart(tester, homeWorkDelhi());
      await _tap(tester, find.text('DELIVER TO'));

      await _tap(tester, find.text('Delhi'));
      await tester.tap(find.text('Change and empty cart'));
      await tester.pumpAndSettle();

      expect(app.container.read(cartProvider), isEmpty);
      expect(app.container.read(selectedAddressIdProvider), 'a3');
    });

    testWidgets('with an empty cart nothing is asked', (tester) async {
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: homeWorkDelhi());
      await _tap(tester, find.text('DELIVER TO'));

      await _tap(tester, find.text('Delhi'));

      expect(find.text('Change delivery address?'), findsNothing);
      expect(app.container.read(selectedAddressIdProvider), 'a3');
    });
  });

  group('checkout', () {
    Future<void> openCheckout(WidgetTester tester, _App app, {bool alreadyOnServer = false}) async {
      if (alreadyOnServer) {
        // A cart saved earlier, in a store that no longer delivers to the chosen address.
        app.shop.setLine('30000000-0000-0000-0000-000000000001', 'tomato:1kg', 1);
      } else {
        app.container.read(cartProvider.notifier).add('tomato:1kg');
      }
      await tester.pumpAndSettle();
      _go(tester, '/checkout');
      await tester.pumpAndSettle();
    }

    PillButton placeOrder(WidgetTester tester) => tester.widget<PillButton>(find.byType(PillButton).last);

    testWidgets('an address nobody delivers to blocks the order with a clear reason and a way to choose another', (tester) async {
      final repo = FakeAddressRepository()..seed('Delhi', isDefault: true, latitude: 28.61, longitude: 77.21, line: 'Connaught Place, Delhi');
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo, height: 2000);
      await openCheckout(tester, app, alreadyOnServer: true);

      expect(find.text('Checkout'), findsWidgets);
      expect(find.text('Delivering to Delhi'), findsOneWidget);
      expect(find.text('We do not deliver to this address yet. Choose another address to place your order.'), findsOneWidget);
      expect(placeOrder(tester).onPressed, isNull);

      await _tap(tester, find.text('Change'));
      expect(find.text('Delivery addresses'), findsOneWidget);
    });

    testWidgets('an address that is served can be ordered to, and the order carries that address', (tester) async {
      final repo = FakeAddressRepository()..seed('Home', isDefault: true, flat: 'Flat 4');
      final app = await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo, height: 2000);
      await openCheckout(tester, app);

      expect(find.text('Delivering to Home'), findsOneWidget);
      expect(find.text('Flat 4, 12 Marine Drive, Mumbai'), findsOneWidget);
      expect(find.textContaining('We do not deliver to this address'), findsNothing);
      expect(placeOrder(tester).onPressed, isNotNull);
    });
  });

  group('profile', () {
    testWidgets('the saved addresses tile shows where we deliver and opens the list', (tester) async {
      final repo = FakeAddressRepository()..seed('Home', isDefault: true);
      await _pump(tester, auth: const AuthState(AuthStatus.signedIn, _asha), repo: repo);
      await _tap(tester, find.byTooltip('Profile'));

      expect(find.text('Home · 12 Marine Drive, Mumbai'), findsOneWidget);
      await _tap(tester, find.text('Saved addresses'));

      expect(find.text('Delivery addresses'), findsOneWidget);
    });

    testWidgets('with nothing chosen the tile invites adding an address', (tester) async {
      await _pump(tester);
      await _tap(tester, find.byTooltip('Profile'));

      expect(find.text('Add an address'), findsOneWidget);
    });
  });
}
