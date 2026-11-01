import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/google_sign_in_service.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/features/auth/auth_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/auth_fakes.dart';

Map<String, Object?> otpSentBody({int resend = 30}) => {
      'success': true,
      'data': {'resendAfterSeconds': resend, 'expiresInSeconds': 300}
    };

Map<String, Object?> phoneProfileBody({bool verified = true, String phone = '+919876543210'}) => {
      'success': true,
      'data': {'id': 'u1', 'fullName': 'Customer', 'email': '', 'phoneNumber': phone, 'phoneVerified': verified},
    };

class _Google implements GoogleSignInService {
  _Google({this.token = 'google-token', this.configured = true});
  final String? token;
  final bool configured;
  int asked = 0;

  @override
  bool get isConfigured => configured;

  @override
  Future<String?> idToken() async {
    asked++;
    return token;
  }
}

Future<void> _pump(WidgetTester tester, AuthHarness auth, {GoogleSignInService? google, Map<String, Object> prefs = const {}}) async {
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 900 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    sessionStoreProvider.overrideWithValue(MemorySessionStore()),
    authRepositoryProvider.overrideWithValue(auth.repo),
    googleSignInServiceProvider.overrideWithValue(google ?? _Google(configured: false)),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid', useSeedData: true)),
  ], child: const QuickCartApp()));
  await tester.pumpAndSettle();
}

Future<void> _openWelcome(WidgetTester tester) async {
  await tester.tap(find.byTooltip('Profile'));
  await tester.pumpAndSettle();
  await tester.scrollUntilVisible(find.text('Sign in / Register'), 200, scrollable: find.byType(Scrollable).first);
  await tester.pumpAndSettle();
  await tester.tap(find.text('Sign in / Register'));
  await tester.pumpAndSettle();
}

Future<void> _askForCode(WidgetTester tester, String number) async {
  await tester.tap(find.text('Continue with phone'));
  await tester.pumpAndSettle();
  await tester.enterText(find.byType(TextFormField), number);
  await tester.pump();
  await tester.tap(find.text('Get OTP'));
  await tester.pumpAndSettle();
}

Future<void> _typeCode(WidgetTester tester, String code) async {
  await tester.enterText(find.byKey(const Key('otp-field')), code);
  await tester.pump();
}

void main() {
  group('the repository', () {
    test('asking for a code posts the number and reads when to allow asking again', () async {
      final auth = AuthHarness()..adapter.replyJson(otpSentBody(resend: 45));

      final sent = await auth.repo.requestOtp('9876543210');

      expect(auth.paths, ['POST /api/auth/otp/request']);
      expect(auth.adapter.requests.single.data, {'phone': '9876543210'});
      expect((sent.resendAfterSeconds, sent.expiresInSeconds), (45, 300));
    });

    test('the right code starts the session, keeps the refresh token and reads the full profile', () async {
      final auth = AuthHarness()
        ..adapter.replyJson(sessionBody(access: 'a-otp', refresh: 'r-otp'))
        ..adapter.replyJson(phoneProfileBody());

      final profile = await auth.repo.verifyOtp(phone: '9876543210', code: '123456');

      expect(auth.paths, ['POST /api/auth/otp/verify', 'GET /api/customer/profile']);
      expect(auth.adapter.requests.first.data, {'phone': '9876543210', 'code': '123456'});
      expect(auth.authorizationOf(1), 'Bearer a-otp');
      expect(await auth.store.readRefreshToken(), 'r-otp');
      expect((profile.phoneNumber, profile.phoneVerified), ('+919876543210', true));
    });

    test('if the profile cannot be read right after, the sign-in still stands', () async {
      final auth = AuthHarness()
        ..adapter.replyJson(sessionBody())
        ..adapter.replyJson(failureBody('boom'), status: 500);

      final profile = await auth.repo.verifyOtp(phone: '9876543210', code: '123456');

      expect(profile.id, 'u1');
      expect(await auth.store.readRefreshToken(), 'refresh-1');
    });

    test('a wrong code is an unauthorized answer and nothing is stored', () async {
      final auth = AuthHarness()..adapter.replyJson(failureBody('Invalid or expired code.'), status: 401);

      await expectLater(auth.repo.verifyOtp(phone: '9876543210', code: '000000'), throwsA(isA<UnauthorizedException>()));
      expect(await auth.store.readRefreshToken(), isNull);
    });

    test('a staff account that somehow signs in this way is signed out again', () async {
      final auth = AuthHarness()
        ..adapter.replyJson(sessionBody(roles: ['StoreStaff']))
        ..adapter.replyJson({'success': true});

      await expectLater(auth.repo.verifyOtp(phone: '9876543210', code: '123456'), throwsA(isA<ForbiddenException>()));
      expect(auth.paths, ['POST /api/auth/otp/verify', 'POST /api/auth/logout']);
      expect(await auth.store.readRefreshToken(), isNull);
    });

    test('Google sends only the ID token and starts the same kind of session', () async {
      final auth = AuthHarness()
        ..adapter.replyJson(sessionBody(refresh: 'r-google'))
        ..adapter.replyJson(profileBody());

      await auth.repo.googleSignIn('the-id-token');

      expect(auth.adapter.requests.first.path, '/api/auth/google');
      expect(auth.adapter.requests.first.data, {'idToken': 'the-id-token'});
      expect(await auth.store.readRefreshToken(), 'r-google');
    });

    test('verifying a number for the account you have uses the signed-in routes', () async {
      final auth = AuthHarness()
        ..adapter.replyJson(otpSentBody())
        ..adapter.replyJson({'success': true, 'data': true});

      await auth.repo.requestPhoneVerification('9876543210');
      await auth.repo.verifyPhone(phone: '9876543210', code: '123456');

      expect(auth.paths, ['POST /api/account/phone/otp/request', 'POST /api/account/phone/otp/verify']);
    });
  });

  group('the screens', () {
    testWidgets('welcome offers the phone, with email and registration still there and no Google button when it is not configured', (tester) async {
      await _pump(tester, AuthHarness());
      await _openWelcome(tester);

      expect(find.text('Continue with phone'), findsOneWidget);
      expect(find.text('Log in'), findsOneWidget);
      expect(find.text('Create account'), findsOneWidget);
      expect(find.text('Continue with Google'), findsNothing);
    });

    testWidgets('a number that is not ten digits is refused before anything is sent', (tester) async {
      final auth = AuthHarness();
      await _pump(tester, auth);
      await _openWelcome(tester);

      await _askForCode(tester, '12345');

      expect(find.text('Enter a 10-digit number'), findsOneWidget);
      expect(auth.adapter.requests, isEmpty);
    });

    testWidgets('a number starting below 6 is not a mobile number', (tester) async {
      final auth = AuthHarness();
      await _pump(tester, auth);
      await _openWelcome(tester);

      await _askForCode(tester, '5876543210');

      expect(find.text('Enter a 10-digit number'), findsOneWidget);
      expect(auth.adapter.requests, isEmpty);
    });

    testWidgets('asking for a code opens the code screen, the right code signs in and goes home', (tester) async {
      final auth = AuthHarness()
        ..adapter.replyJson(otpSentBody())
        ..adapter.replyJson(sessionBody())
        ..adapter.replyJson(phoneProfileBody());
      await _pump(tester, auth);
      await _openWelcome(tester);

      await _askForCode(tester, '98765 43210');
      expect(find.text('Verify your number'), findsOneWidget);
      expect(auth.adapter.requests.first.data, {'phone': '9876543210'});

      await _typeCode(tester, '123456');
      await tester.tap(find.text('Verify and continue'));
      await tester.pumpAndSettle();

      expect(auth.adapter.requests[1].data, {'phone': '9876543210', 'code': '123456'});
      expect(find.text('Verify your number'), findsNothing);
      final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
      expect(container.read(authProvider).isSignedIn, isTrue);
    });

    testWidgets('a wrong code shows the message and clears the boxes, and the customer is not signed in', (tester) async {
      final auth = AuthHarness()
        ..adapter.replyJson(otpSentBody())
        ..adapter.replyJson(failureBody('Invalid or expired code.'), status: 401);
      await _pump(tester, auth);
      await _openWelcome(tester);
      await _askForCode(tester, '9876543210');

      await _typeCode(tester, '000000');
      await tester.tap(find.text('Verify and continue'));
      await tester.pumpAndSettle();

      expect(find.text('That code is not valid or has expired. Check it or ask for a new one.'), findsOneWidget);
      expect(tester.widget<TextField>(find.byKey(const Key('otp-field'))).controller!.text, isEmpty);
      final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
      expect(container.read(authProvider).isSignedIn, isFalse);
    });

    testWidgets('the verify button waits for six digits', (tester) async {
      final auth = AuthHarness()..adapter.replyJson(otpSentBody());
      await _pump(tester, auth);
      await _openWelcome(tester);
      await _askForCode(tester, '9876543210');

      await _typeCode(tester, '123');
      await tester.tap(find.text('Verify and continue'), warnIfMissed: false);
      await tester.pumpAndSettle();

      expect(auth.adapter.requests, hasLength(1));
    });

    testWidgets('resend is counted down, then asks again and starts a new countdown', (tester) async {
      final auth = AuthHarness()
        ..adapter.replyJson(otpSentBody(resend: 3))
        ..adapter.replyJson(otpSentBody(resend: 3));
      await _pump(tester, auth);
      await _openWelcome(tester);
      await _askForCode(tester, '9876543210');

      expect(find.text('Resend code in 0:03'), findsOneWidget);
      await tester.pump(const Duration(seconds: 1));
      expect(find.text('Resend code in 0:02'), findsOneWidget);
      await tester.pump(const Duration(seconds: 3));
      expect(find.text('Resend code'), findsOneWidget);

      await tester.tap(find.text('Resend code'));
      await tester.pumpAndSettle();

      expect(auth.paths, ['POST /api/auth/otp/request', 'POST /api/auth/otp/request']);
      expect(find.text('Resend code in 0:03'), findsOneWidget);
    });

    testWidgets('too many requests are explained instead of failing silently', (tester) async {
      final auth = AuthHarness()..adapter.replyJson(failureBody('Too many codes were asked for this number.'), status: 429);
      await _pump(tester, auth);
      await _openWelcome(tester);

      await _askForCode(tester, '9876543210');

      expect(find.text('Too many attempts. Please wait a moment and try again.'), findsOneWidget);
      expect(find.text('Verify your number'), findsNothing);
    });

    testWidgets('Google appears when configured, signs in with the token and goes home', (tester) async {
      final google = _Google();
      final auth = AuthHarness()
        ..adapter.replyJson(sessionBody(refresh: 'r-g'))
        ..adapter.replyJson(profileBody());
      await _pump(tester, auth, google: google);
      await _openWelcome(tester);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(google.asked, 1);
      expect(auth.adapter.requests.first.data, {'idToken': 'google-token'});
      final container = ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
      expect(container.read(authProvider).isSignedIn, isTrue);
    });

    testWidgets('closing the Google account picker does nothing', (tester) async {
      final auth = AuthHarness();
      await _pump(tester, auth, google: _Google(token: null));
      await _openWelcome(tester);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(auth.adapter.requests, isEmpty);
      expect(find.text('Continue with Google'), findsOneWidget);
    });

    testWidgets('a Google sign-in the server refuses shows a message', (tester) async {
      final auth = AuthHarness()..adapter.replyJson(failureBody('Google sign-in failed.'), status: 401);
      await _pump(tester, auth, google: _Google());
      await _openWelcome(tester);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(find.text('Google sign-in did not work. Please try again.'), findsOneWidget);
    });
  });

  group('verifying the number of the account you have', () {
    Future<AuthHarness> signedIn(WidgetTester tester, {bool verified = false}) async {
      final auth = AuthHarness(storedToken: 'stored')
        ..adapter.replyJson(sessionBody())
        ..adapter.replyJson(phoneProfileBody(verified: verified));
      await _pump(tester, auth);
      return auth;
    }

    testWidgets('profile offers Verify for an unverified number and shows Verified once it is', (tester) async {
      await signedIn(tester);
      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();
      expect(find.text('Verify phone number'), findsOneWidget);
      expect(find.text('Phone number verified'), findsNothing);
    });

    testWidgets('a verified number is shown as verified', (tester) async {
      await signedIn(tester, verified: true);
      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();
      expect(find.text('Phone number verified'), findsOneWidget);
      expect(find.text('Verify phone number'), findsNothing);
    });

    testWidgets('verifying texts a code to the number on file and ends with the profile showing it verified', (tester) async {
      final auth = await signedIn(tester);
      auth.adapter
        ..replyJson(otpSentBody())
        ..replyJson({'success': true, 'data': true})
        ..replyJson(phoneProfileBody(verified: true));
      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Verify phone number'));
      await tester.pumpAndSettle();
      expect(tester.widget<TextFormField>(find.byType(TextFormField)).controller!.text, '9876543210');
      await tester.tap(find.text('Get OTP'));
      await tester.pumpAndSettle();
      expect(auth.adapter.requests[2].path, '/api/account/phone/otp/request');

      await _typeCode(tester, '123456');
      await tester.tap(find.text('Verify and continue'));
      await tester.pumpAndSettle();

      expect(auth.adapter.requests[3].path, '/api/account/phone/otp/verify');
      expect(find.text('Phone number verified'), findsOneWidget);
    });

    testWidgets('a number another account already holds is explained', (tester) async {
      final auth = await signedIn(tester);
      auth.adapter
        ..replyJson(otpSentBody())
        ..replyJson(failureBody('This number cannot be verified for your account.'), status: 409);
      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Verify phone number'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Get OTP'));
      await tester.pumpAndSettle();

      await _typeCode(tester, '123456');
      await tester.tap(find.text('Verify and continue'));
      await tester.pumpAndSettle();

      expect(find.text('This number is already verified on another account.'), findsOneWidget);
    });
  });
}
