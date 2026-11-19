// Opt-in end-to-end test against a running API (the same way as api_smoke_test.dart). It runs the customer app's real client code
// (ApiClient, repositories, DTOs) against the real API and database, and uses the administrator's API to set the scene:
//
//   flutter test test/integration/e2e_coupon_slot_test.dart \
//     --dart-define=SMOKE_API_URL=http://localhost:5067 --dart-define=SMOKE_ACCOUNTS=true --dart-define=E2E_ADMIN_PASSWORD=<the API's development admin password>
//
// It creates a customer, a coupon and delivery-time settings for one store, and puts everything back at the end (the coupon is switched off,
// the store's own settings are removed). Run it on a development or throwaway database only.
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
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/coupon_repository.dart';
import 'package:quickcart_customer/data/repositories/delivery_slots_repository.dart';
import 'package:quickcart_customer/data/repositories/order_repository.dart';
import 'package:quickcart_customer/data/session_store.dart';

const _url = String.fromEnvironment('SMOKE_API_URL');
const _accounts = bool.fromEnvironment('SMOKE_ACCOUNTS');
const _adminPassword = String.fromEnvironment('E2E_ADMIN_PASSWORD');

void main() {
  final skip = _url.isEmpty
      ? 'Set --dart-define=SMOKE_API_URL=<api base url> to run'
      : !_accounts
          ? 'Set --dart-define=SMOKE_ACCOUNTS=true to create a test customer'
          : _adminPassword.isEmpty
              ? 'Set --dart-define=E2E_ADMIN_PASSWORD=<the development admin password>'
              : null;

  setUpAll(() => HttpOverrides.global = null); // flutter test blocks real HTTP by default

  test('coupon and scheduled delivery: set up by an admin, used by a customer, given back on cancel', () async {
    final config = AppConfig.resolve(envName: 'dev', baseUrl: _url, androidDevice: false);

    // The administrator, signed in the way the portal does.
    final adminToken = await ApiClient(config: config).post('/api/auth/login', body: {'username': 'admin', 'password': _adminPassword}, parse: (d) => asObject(d)['accessToken'] as String);
    final admin = ApiClient(config: config, tokenProvider: () async => adminToken);

    // The customer.
    late AuthRepository auth;
    final api = ApiClient(config: config, tokenProvider: () => auth.accessToken(), onUnauthorized: () => auth.refreshSession());
    auth = AuthRepository(api, MemorySessionStore());
    final catalog = ApiCatalogRepository(api);
    final carts = ApiCartRepository(api);
    final orders = ApiOrderRepository(api);
    final addresses = ApiAddressRepository(api);
    final coupons = ApiCouponRepository(api);
    final slots = ApiDeliverySlotsRepository(api);
    final stamp = DateTime.now().millisecondsSinceEpoch;
    await auth.register(fullName: 'E2E Customer', email: 'e2e-$stamp@example.test', password: 'Sunrise-42x', phoneNumber: '98765 43213');

    final shop = (await catalog.stores(latitude: 19.076, longitude: 72.878)).first;
    final product = (await catalog.products(storeId: shop.id, carriedOnly: true, pageSize: 5)).items.firstWhere((p) => p.inStock);
    final item = (productId: product.id, variantId: product.defaultPack?.id, quantity: 1);
    final code = 'E2E${stamp.toString().substring(stamp.toString().length - 8)}';
    String? couponId;
    String? addressId;
    final placed = <String>[];

    try {
      // Before the administrator does anything, the store offers no choice of time.
      expect((await slots.list(shop.id)).enabled, isFalse, reason: 'scheduling starts off');

      // The administrator gives the store hourly windows with room for one order each, and makes a coupon for ₹5 off, once per customer.
      await admin.put('/api/admin/delivery-slots/stores/${shop.id}',
          body: {'enabled': true, 'open': '00:00', 'close': '24:00', 'windowMinutes': 60, 'capacity': 1, 'cutoffMinutes': 0, 'daysAhead': 2, 'asapAllowed': true}, parse: (_) => null);
      couponId = await admin.post('/api/admin/coupons', body: {'code': code, 'kind': 'Flat', 'value': 5, 'perCustomerLimit': 1}, parse: (d) => asObject(d)['id'] as String);

      // The customer's cart, and the code tried on it.
      await carts.add(shop.id, item);
      final unit = (await carts.current())!.lines.single.unitPrice;
      final tried = await coupons.preview(storeId: shop.id, code: code.toLowerCase());
      expect(tried.valid, isTrue, reason: tried.message);
      expect(tried.discount, 5);

      // The windows the store now offers; one is chosen. Capacity is one, so it shows one place left.
      final offered = await slots.list(shop.id);
      expect((offered.enabled, offered.asapAllowed), (true, true));
      final window = offered.windows.firstWhere((w) => w.available);
      expect(window.left, 1);

      // Order with the code and the window.
      final address = await addresses.create(const AddressDraft(label: 'Home', line: '12 Marine Drive, Mumbai', flatOrBuilding: 'Flat 4', latitude: 19.076, longitude: 72.8777, receiverName: 'E2E Customer', receiverPhone: '9876543213'));
      addressId = address.id;
      final first = await orders.place(storeId: shop.id, idempotencyKey: 'e2e-key-$stamp-0001', addressId: address.id, couponCode: code, deliverySlotStart: window.start);
      placed.add(first.id);
      expect(first.couponCode, code);
      expect(first.couponDiscount, 5);
      expect(first.total, closeTo(first.subtotal + first.deliveryFee + first.handlingFee - 5, 0.001));
      expect(first.subtotal, closeTo(unit, 0.001));
      expect(first.isScheduled, isTrue);
      expect(first.deliverySlotStart, window.start);
      expect(first.deliverySlotEnd, window.end);

      // The order read back from the server keeps both.
      final read = await orders.get(first.id);
      expect((read.couponCode, read.couponDiscount, read.deliverySlotStart), (code, 5.0, window.start));

      // The window is full now, and the code is used up for this customer.
      expect((await slots.list(shop.id)).windows.firstWhere((w) => w.start == window.start).available, isFalse);
      await carts.add(shop.id, item);
      final again = await coupons.preview(storeId: shop.id, code: code);
      expect(again.valid, isFalse);
      expect(again.message, contains('already used'));

      // A second order for the same, full window is refused with a reason, and nothing is taken.
      await expectLater(
        orders.place(storeId: shop.id, idempotencyKey: 'e2e-key-$stamp-0002', addressId: address.id, deliverySlotStart: window.start),
        throwsA(isA<ConflictException>().having((e) => e.reason, 'reason', CheckoutReasons.slotUnavailable)),
      );

      // The customer cancels the first order before the store accepts it: the window and the code are given back.
      expect((await orders.cancel(first.id)).stage, OrderStage.cancelled);
      expect((await slots.list(shop.id)).windows.firstWhere((w) => w.start == window.start).available, isTrue);
      final back = await coupons.preview(storeId: shop.id, code: code);
      expect(back.valid, isTrue, reason: back.message);

      // And now the same window and code work again, as an ordinary new order.
      final second = await orders.place(storeId: shop.id, idempotencyKey: 'e2e-key-$stamp-0003', addressId: address.id, couponCode: code, deliverySlotStart: window.start);
      placed.add(second.id);
      expect((second.couponCode, second.couponDiscount, second.deliverySlotStart), (code, 5.0, window.start));
    } finally {
      for (final id in placed) {
        try {
          await orders.cancel(id);
        } on ApiException {
          // Already cancelled or no longer cancellable: nothing more to do.
        }
      }
      if (addressId != null) {
        try {
          await addresses.delete(addressId);
        } on ApiException {
          // The address may be in use by an order that cannot be cancelled; it is a test customer's.
        }
      }
      await admin.delete('/api/admin/delivery-slots/stores/${shop.id}', parse: (_) => null);
      if (couponId != null) {
        await admin.put('/api/admin/coupons/$couponId', body: {'code': code, 'kind': 'Flat', 'value': 5, 'perCustomerLimit': 1, 'isActive': false}, parse: (_) => null);
      }
      await auth.logout();
    }
  }, skip: skip);
}
