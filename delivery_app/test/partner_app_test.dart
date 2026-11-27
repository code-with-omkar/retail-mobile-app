import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_delivery/api.dart';
import 'package:quickcart_delivery/app.dart';
import 'package:quickcart_delivery/location.dart';

Delivery delivery({String number = 'KH-261012-0001', String status = 'Ready', double cash = 250, String id = 'o1', String? slotStart, String? slotEnd}) => Delivery.fromJson({
      'orderId': id,
      'orderNumber': number,
      'status': status,
      'storeName': 'Kharghar Fresh Hub',
      'storePhone': '02249000001',
      'deliveryAddress': 'Flat 4, Sector 20',
      'latitude': 19.03,
      'longitude': 73.06,
      'receiverName': 'Asha',
      'receiverPhone': '9111111111',
      'paymentMethod': cash > 0 ? 'CashOnDelivery' : 'Online',
      'totalAmount': 250,
      'cashToCollect': cash,
      'items': [
        {'productNameSnapshot': 'Farm Milk', 'variantLabel': '1 l', 'quantity': 2, 'unitPrice': 34, 'totalPrice': 68}
      ],
      'deliveredAt': status == 'Delivered' ? DateTime.now().toUtc().toIso8601String() : null,
      'deliverySlotStart': slotStart,
      'deliverySlotEnd': slotEnd,
    });

class FakeApi implements DeliveryApi {
  /// Every position sent: order id, latitude, longitude.
  final shared = <(String, double, double)>[];

  @override
  Future<void> shareLocation(String orderId, double latitude, double longitude) async => shared.add((orderId, latitude, longitude));

  @override
  bool mustChangePassword = false;

  @override
  Future<void> reportFailed(String orderId, {required String reason, String? note}) async {
    calls.add('failed $orderId $reason ${note ?? ''}');
    if (failNext != null) throw failNext!;
    items.removeWhere((d) => d.orderId == orderId);
  }

  @override
  Future<void> changePassword(String current, String next) async {
    calls.add('changePassword $current $next');
    if (failNext != null) throw failNext!;
    mustChangePassword = false;
  }

  @override
  Future<void> forgotPassword(String email) async => calls.add('forgot $email');

  @override
  Future<void> resetPassword(String email, String code, String next) async => calls.add('reset $email $code $next');

  FakeApi({this.signedIn = false, List<Delivery>? items}) : items = items ?? [];
  bool signedIn;
  final List<Delivery> items;
  final calls = <String>[];
  ApiError? failNext;
  ApiError? listFails;

  @override
  Future<bool> restore() async => signedIn;

  @override
  Future<String> signIn(String username, String password) async {
    calls.add('signIn $username');
    if (password != 'right') throw ApiError('Wrong username or password.');
    signedIn = true;
    return 'Rina';
  }

  @override
  Future<void> signOut() async {
    calls.add('signOut');
    signedIn = false;
  }

  @override
  Future<List<Delivery>> mine() async {
    calls.add('mine');
    if (listFails != null) throw listFails!;
    return List.of(items);
  }

  Delivery _replace(String id, String status) {
    final at = items.indexWhere((d) => d.orderId == id);
    final next = delivery(number: items[at].number, status: status, cash: items[at].cashToCollect, id: id);
    items[at] = next;
    return next;
  }

  @override
  Future<Delivery> pickUp(String orderId) async {
    calls.add('pickUp $orderId');
    if (failNext != null) throw failNext!;
    return _replace(orderId, 'OutForDelivery');
  }

  @override
  Future<Delivery> deliver(String orderId, {required String otp, required double cash}) async {
    calls.add('deliver $orderId $otp $cash');
    if (failNext != null) {
      final e = failNext!;
      failNext = null;
      throw e;
    }

    return _replace(orderId, 'Delivered');
  }
}

Future<void> open(WidgetTester tester, FakeApi api, {List<Uri>? opened, LocationSource? locate, Duration shareEvery = const Duration(hours: 1)}) async {
  tester.view.physicalSize = const Size(390 * 3, 1000 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(const SizedBox());
  await tester.pumpWidget(PartnerApp(
    api: api,
    pollEvery: const Duration(hours: 1),
    shareEvery: shareEvery,
    locate: locate ?? () async => null,
    open: (uri) async {
      opened?.add(uri);
      return true;
    },
  ));
  await tester.pumpAndSettle();
}

void main() {
  sharingTests();
  group('passwords and failed deliveries', () {
    testWidgets('a temporary password must be changed before the deliveries show', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery()])..mustChangePassword = true;
      await open(tester, api);
      expect(find.text('Choose your password'), findsOneWidget);
      expect(find.text('My deliveries'), findsNothing);

      await tester.enterText(find.byKey(const Key('current')), 'Temp-1234abcd');
      await tester.enterText(find.byKey(const Key('next')), 'short');
      await tester.enterText(find.byKey(const Key('again')), 'short');
      await tester.tap(find.byKey(const Key('save-password')));
      await tester.pumpAndSettle();
      expect(find.text('Use at least 8 characters.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('next')), 'my-own-pass1');
      await tester.enterText(find.byKey(const Key('again')), 'my-own-pass1');
      await tester.tap(find.byKey(const Key('save-password')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('changePassword Temp-1234abcd my-own-pass1'));
      expect(find.text('My deliveries'), findsOneWidget);
    });

    testWidgets('forgot password emails a code and saves the new one', (tester) async {
      final api = FakeApi();
      await open(tester, api);
      await tester.tap(find.text('Forgot password?'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('email')), 'rina@example.test');
      await tester.tap(find.byKey(const Key('send')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('code')), '123456');
      await tester.enterText(find.byKey(const Key('next')), 'my-own-pass1');
      await tester.enterText(find.byKey(const Key('again')), 'my-own-pass1');
      await tester.tap(find.byKey(const Key('send')));
      await tester.pumpAndSettle();
      expect(api.calls, containsAll(['forgot rina@example.test', 'reset rina@example.test 123456 my-own-pass1']));
    });

    testWidgets('a delivery that cannot be made goes back to the store with its reason', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery')]);
      await open(tester, api);
      await tester.tap(find.textContaining('KH-261012-0001'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byKey(const Key('could-not')));
      await tester.tap(find.byKey(const Key('could-not')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Wrong address'));
      await tester.tap(find.byKey(const Key('report-failed')));
      await tester.pumpAndSettle();
      expect(api.calls, contains('failed o1 WrongAddress '));
      expect(find.text('Nothing to deliver right now.'), findsOneWidget);
    });
  });

  group('signing in', () {
    testWidgets('a signed-out partner sees the sign-in screen, and a wrong password says so', (tester) async {
      await open(tester, FakeApi());
      expect(find.text('Sign in'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('username')), 'rina@example.test');
      await tester.enterText(find.byKey(const Key('password')), 'nope');
      await tester.tap(find.text('Sign in'));
      await tester.pumpAndSettle();

      expect(find.text('Wrong username or password.'), findsOneWidget);
      expect(find.text('My deliveries'), findsNothing);
    });

    testWidgets('empty fields are not sent', (tester) async {
      final api = FakeApi();
      await open(tester, api);
      await tester.tap(find.text('Sign in'));
      await tester.pumpAndSettle();

      expect(find.text('Enter your username and password.'), findsOneWidget);
      expect(api.calls, isEmpty);
    });

    testWidgets('the right password opens the deliveries; a remembered sign-in skips the screen; sign out returns', (tester) async {
      final api = FakeApi(items: [delivery()]);
      await open(tester, api);
      await tester.enterText(find.byKey(const Key('username')), 'rina@example.test');
      await tester.enterText(find.byKey(const Key('password')), 'right');
      await tester.tap(find.text('Sign in'));
      await tester.pumpAndSettle();
      expect(find.text('My deliveries'), findsOneWidget);

      await tester.tap(find.byTooltip('Sign out'));
      await tester.pumpAndSettle();
      expect(find.text('Sign in'), findsOneWidget);
      expect(api.calls, contains('signOut'));

      final again = FakeApi(signedIn: true, items: [delivery()]);
      await open(tester, again);
      expect(find.text('My deliveries'), findsOneWidget);
    });
  });

  group('the list', () {
    testWidgets('shows what is to do and what was delivered, with cash to collect or paid online', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(), delivery(number: 'KH-2', id: 'o2', status: 'OutForDelivery', cash: 0), delivery(number: 'KH-3', id: 'o3', status: 'Delivered')]);
      await open(tester, api);

      expect(find.text('To do'), findsOneWidget);
      expect(find.text('Delivered in the last day'), findsOneWidget);
      expect(find.text('#KH-261012-0001'), findsOneWidget);
      expect(find.text('Collect ₹250 cash'), findsNWidgets(2));
      expect(find.text('Paid online'), findsOneWidget);
      expect(find.text('Pick up'), findsOneWidget);
      expect(find.text('On the way'), findsOneWidget);
    });

    testWidgets('a scheduled order shows its window on the card and in the detail; an order for now shows none', (tester) async {
      // The window is tomorrow at 9 am India time, whatever day the test runs.
      final indiaNow = DateTime.now().toUtc().add(const Duration(hours: 5, minutes: 30));
      final start = DateTime.utc(indiaNow.year, indiaNow.month, indiaNow.day).add(const Duration(days: 1, hours: 9)).subtract(const Duration(hours: 5, minutes: 30));
      final api = FakeApi(signedIn: true, items: [delivery(slotStart: start.toIso8601String(), slotEnd: start.add(const Duration(hours: 1)).toIso8601String()), delivery(number: 'KH-2', id: 'o2')]);
      await open(tester, api);

      expect(find.byKey(const Key('window-line')), findsOneWidget);
      expect(find.text('Deliver Tomorrow, 9 am - 10 am'), findsOneWidget);

      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();
      expect(find.text('Deliver Tomorrow, 9 am - 10 am'), findsOneWidget);
    });

    test('the window is worded in India time and absent for an order for now', () {
      final now = DateTime.utc(2026, 11, 2, 4, 30); // 10:00 in India
      final today = delivery(slotStart: '2026-11-02T08:30:00Z', slotEnd: '2026-11-02T09:30:00Z');
      final later = delivery(slotStart: '2026-11-04T06:30:00Z', slotEnd: '2026-11-04T08:00:00Z');
      expect(today.windowText(now), 'Today, 2 pm - 3 pm');
      expect(later.windowText(now), 'Wed 4 Nov, 12 pm - 1:30 pm');
      expect(delivery().windowText(now), isNull);
    });

    testWidgets('an empty list says there is nothing to deliver', (tester) async {
      await open(tester, FakeApi(signedIn: true));
      expect(find.text('Nothing to deliver right now.'), findsOneWidget);
    });

    testWidgets('a failed load says so, and an expired sign-in returns to the sign-in screen', (tester) async {
      await open(tester, FakeApi(signedIn: true)..listFails = ApiError('Could not reach the server. Check your connection.'));
      expect(find.text('Could not reach the server. Check your connection.'), findsOneWidget);

      await open(tester, FakeApi(signedIn: true)..listFails = ApiError('no', status: 401));
      expect(find.text('Sign in'), findsOneWidget);
    });
  });

  group('one delivery', () {
    testWidgets('shows the address, items and the cash to collect, and lets the partner call and navigate', (tester) async {
      final opened = <Uri>[];
      await open(tester, FakeApi(signedIn: true, items: [delivery()]), opened: opened);
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();

      expect(find.text('Flat 4, Sector 20'), findsOneWidget);
      expect(find.text('Farm Milk 1 l × 2'), findsOneWidget);
      expect(find.text('Collect ₹250 in cash'), findsOneWidget);

      await tester.tap(find.text('Call customer'));
      await tester.tap(find.text('Call store'));
      await tester.tap(find.text('Navigate'));
      await tester.pumpAndSettle();
      expect(opened.map((u) => u.scheme), ['tel', 'tel', 'geo']);
      expect(opened.first.path, '9111111111');
    });

    testWidgets('picking up moves it on the way and shows the code field', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery()]);
      await open(tester, api);
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('otp')), findsNothing);

      await tester.tap(find.byKey(const Key('pickup')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('pickUp o1'));
      expect(find.byKey(const Key('otp')), findsOneWidget);
      expect(find.byKey(const Key('pickup')), findsNothing);
    });

    testWidgets('a wrong code shows the server message and keeps the delivery open; the right one completes it', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery')]);
      await open(tester, api);
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();

      api.failNext = ApiError('That code is not right. Ask the customer again.', reason: 'WrongCode', status: 400);
      await tester.enterText(find.byKey(const Key('otp')), '0000');
      await tester.tap(find.byKey(const Key('deliver')));
      await tester.pumpAndSettle();
      expect(find.text('That code is not right. Ask the customer again.'), findsOneWidget);
      expect(find.byKey(const Key('otp')), findsOneWidget);

      await tester.enterText(find.byKey(const Key('otp')), '4821');
      await tester.tap(find.byKey(const Key('deliver')));
      await tester.pumpAndSettle();

      expect(api.calls.last, 'deliver o1 4821 250.0');
      expect(find.text('This delivery is complete.'), findsOneWidget);
    });

    testWidgets('the cash field starts at the amount to collect; an online order has none and sends 0', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery', cash: 0)]);
      await open(tester, api);
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('cash')), findsNothing);
      expect(find.text('Paid online — collect nothing'), findsOneWidget);
      await tester.enterText(find.byKey(const Key('otp')), '4821');
      await tester.tap(find.byKey(const Key('deliver')));
      await tester.pumpAndSettle();
      expect(api.calls.last, 'deliver o1 4821 0.0');
    });

    testWidgets('a short code or a missing cash amount is caught on the phone, with no request to the server', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery')]);
      await open(tester, api);
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(const Key('otp')), '48');
      await tester.tap(find.byKey(const Key('deliver')));
      await tester.pumpAndSettle();
      expect(find.text('Enter the 4-digit code from the customer.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('otp')), '4821');
      await tester.enterText(find.byKey(const Key('cash')), '');
      await tester.tap(find.byKey(const Key('deliver')));
      await tester.pumpAndSettle();
      expect(find.text('Enter the cash you collected.'), findsOneWidget);
      expect(api.calls.where((call) => call.startsWith('deliver')), isEmpty);
    });

    testWidgets('the cash field warns when the amount is not the amount due', (tester) async {
      await open(tester, FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery')]));
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();
      expect(find.textContaining('to collect.'), findsNothing); // starts at the amount due

      await tester.enterText(find.byKey(const Key('cash')), '200');
      await tester.pump();
      expect(find.text('Less than the ₹250 to collect.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('cash')), '300');
      await tester.pump();
      expect(find.text('More than the ₹250 to collect.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('cash')), '250');
      await tester.pump();
      expect(find.textContaining('to collect.'), findsNothing);
    });

    testWidgets('the code field takes only digits, at most four', (tester) async {
      await open(tester, FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery')]));
      await tester.tap(find.text('#KH-261012-0001'));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(const Key('otp')), '12ab3456');
      expect(tester.widget<TextField>(find.byKey(const Key('otp'))).controller!.text, '1234');
    });
  });

  group('talking to the server', () {
    late _Adapter adapter;
    late HttpDeliveryApi api;
    late _Tokens tokens;

    setUp(() {
      adapter = _Adapter();
      tokens = _Tokens();
      api = HttpDeliveryApi(baseUrl: 'http://api.test', tokens: tokens, dio: Dio(BaseOptions(baseUrl: 'http://api.test'))..httpClientAdapter = adapter);
    });

    Map<String, dynamic> login({List<String> roles = const ['DeliveryPartner'], String access = 'a1', String refresh = 'r1'}) => {
          'success': true,
          'data': {'accessToken': access, 'refreshToken': refresh, 'user': {'displayName': 'Rina Patil', 'roles': roles}}
        };

    test('only a delivery partner can sign in, and the refresh token is kept', () async {
      adapter.reply(200, login(roles: ['Customer']));
      await expectLater(api.signIn('a', 'b'), throwsA(isA<ApiError>().having((e) => e.message, 'message', 'This account is not a delivery partner.')));
      expect(tokens.value, isNull);

      adapter.reply(200, login());
      expect(await api.signIn('a', 'b'), 'Rina Patil');
      expect(tokens.value, 'r1');
    });

    test('a 401 on sign-in means wrong credentials', () async {
      adapter.reply(401, {'success': false, 'message': 'x'});
      await expectLater(api.signIn('a', 'b'), throwsA(isA<ApiError>().having((e) => e.message, 'message', 'Wrong username or password.')));
    });

    test('an expired access token is refreshed once and the call repeated', () async {
      adapter.reply(200, login());
      await api.signIn('a', 'b');
      adapter
        ..reply(401, {'success': false})
        ..reply(200, login(access: 'a2', refresh: 'r2'))
        ..reply(200, {'success': true, 'data': []});

      expect(await api.mine(), isEmpty);
      expect(adapter.paths, ['/api/auth/login', '/api/delivery/mine', '/api/auth/refresh', '/api/delivery/mine']);
      expect(adapter.auth.last, 'Bearer a2');
      expect(tokens.value, 'r2');
    });

    test('a refresh that fails forgets the sign-in', () async {
      tokens.value = 'old';
      adapter.reply(401, {'success': false});
      expect(await api.restore(), isFalse);
      expect(tokens.value, isNull);
    });

    test('the server message and reason reach the screen', () async {
      adapter.reply(200, login());
      await api.signIn('a', 'b');
      adapter.reply(400, {'success': false, 'message': 'That code is not right. Ask the customer again.', 'reason': 'WrongCode'});

      await expectLater(api.deliver('o1', otp: '0000', cash: 250), throwsA(isA<ApiError>().having((e) => e.isWrongCode, 'wrong code', true).having((e) => e.message, 'message', contains('not right'))));
      adapter.reply(429, {'success': false, 'message': 'Too many wrong codes.', 'reason': 'Locked'});
      await expectLater(api.deliver('o1', otp: '0000', cash: 250), throwsA(isA<ApiError>().having((e) => e.isLocked, 'locked', true)));
    });

    test('the status number from the server is read as a name', () {
      expect(Delivery.fromJson({...{'orderId': 'x', 'orderNumber': 'n'}, 'status': 7}).isOut, isTrue);
      expect(Delivery.fromJson({'orderId': 'x', 'orderNumber': 'n', 'status': 3}).isReady, isTrue);
      expect(Delivery.fromJson({'orderId': 'x', 'orderNumber': 'n', 'status': 'Delivered'}).isDone, isTrue);
    });
  });
}

class _Tokens implements TokenStore {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String? token) async => value = token;
}

class _Adapter implements HttpClientAdapter {
  final _replies = <(int, Map<String, dynamic>)>[];
  final paths = <String>[];
  final auth = <String>[];

  void reply(int status, Map<String, dynamic> body) => _replies.add((status, body));

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<List<int>>? requestStream, Future<void>? cancelFuture) async {
    paths.add(options.path);
    auth.add('${options.headers['Authorization']}');
    final (status, body) = _replies.removeAt(0);
    return ResponseBody.fromString(jsonEncode(body), status, headers: {Headers.contentTypeHeader: ['application/json']});
  }

  @override
  void close({bool force = false}) {}

}

void sharingTests() {
  group('sharing the position', () {
    testWidgets('while an order is out for delivery the position goes to the server, with a note saying so', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery', id: 'o9')]);
      await open(tester, api, shareEvery: const Duration(seconds: 1), locate: () async => (latitude: 19.01, longitude: 73.02));

      await tester.pump(const Duration(seconds: 2));

      expect(api.shared, isNotEmpty);
      expect(api.shared.first, ('o9', 19.01, 73.02));
      expect(find.byKey(const Key('sharing-note')), findsOneWidget);
    });

    testWidgets('nothing is sent when no order is out for delivery', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'Ready')]);
      await open(tester, api, shareEvery: const Duration(seconds: 1), locate: () async => (latitude: 19.01, longitude: 73.02));

      await tester.pump(const Duration(seconds: 3));

      expect(api.shared, isEmpty);
      expect(find.byKey(const Key('sharing-note')), findsNothing);
    });

    testWidgets('when the phone cannot say where it is, nothing is sent', (tester) async {
      final api = FakeApi(signedIn: true, items: [delivery(status: 'OutForDelivery')]);
      await open(tester, api, shareEvery: const Duration(seconds: 1));

      await tester.pump(const Duration(seconds: 3));

      expect(api.shared, isEmpty);
    });
  });
}
