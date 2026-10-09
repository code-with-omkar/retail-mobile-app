import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';

import '../support/auth_fakes.dart';

void main() {
  group('sign in', () {
    test('login keeps the refresh token in the store and sends the access token afterwards', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      final user = await h.repo.login(email: ' asha@example.test ', password: 'Sunrise-42x');
      expect(user.fullName, 'Asha Patil');
      expect(h.store.token, 'refresh-1');

      h.adapter.replyJson(profileBody());
      await h.repo.profile();
      expect(h.authorizationOf(1), 'Bearer access-1');
      expect(h.adapter.requests[0].data, {'username': 'asha@example.test', 'password': 'Sunrise-42x'});
    });

    test('a wrong password is a 401 that is not retried and does not end a session', () async {
      final h = AuthHarness(storedToken: 'old');
      h.adapter.replyJson(failureBody('Invalid username or password.'), status: 401);
      await expectLater(h.repo.login(email: 'a@b.co', password: 'nope'), throwsA(isA<UnauthorizedException>()));
      expect(h.paths, ['POST /api/auth/login']);
      expect(h.sessionEnded, 0);
    });

    test('an account without the Customer role is rejected, revoked and never stored', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody(roles: ['Admin']));
      h.adapter.replyJson({'success': true});
      await expectLater(h.repo.login(email: 'admin@example.test', password: 'Whatever-1'), throwsA(isA<ForbiddenException>()));
      expect(h.store.token, isNull);
      expect(h.paths, ['POST /api/auth/login', 'POST /api/auth/logout']);
      expect(await h.repo.accessToken(), isNull);
    });

    test('register sends the phone only when one was typed', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.register(fullName: ' Asha Patil ', email: 'asha@example.test', password: 'Sunrise-42x', phoneNumber: '  ');
      expect(h.adapter.requests.single.data, {'fullName': 'Asha Patil', 'email': 'asha@example.test', 'password': 'Sunrise-42x'});
    });

    test('a taken email is a ConflictException', () async {
      final h = AuthHarness();
      h.adapter.replyJson(failureBody('An account with this email already exists.'), status: 409);
      await expectLater(h.repo.register(fullName: 'A', email: 'a@b.co', password: 'Sunrise-42x'), throwsA(isA<ConflictException>()));
      expect(h.store.token, isNull);
    });
  });

  group('refresh', () {
    test('a 401 on a signed-in request refreshes once and repeats the request with the new token', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.login(email: 'a@b.co', password: 'x');

      h.adapter.replyJson(failureBody('expired'), status: 401);
      h.adapter.replyJson(sessionBody(access: 'access-2', refresh: 'refresh-2'));
      h.adapter.replyJson(profileBody());
      final profile = await h.repo.profile();

      expect(profile.fullName, 'Asha Patil');
      expect(h.paths, ['POST /api/auth/login', 'GET /api/customer/profile', 'POST /api/auth/refresh', 'GET /api/customer/profile']);
      expect(h.authorizationOf(3), 'Bearer access-2');
      expect(h.adapter.requests[2].data, {'refreshToken': 'refresh-1'});
      expect(h.store.token, 'refresh-2');
    });

    test('several requests failing together share one refresh', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.login(email: 'a@b.co', password: 'x');

      // Two requests fail with 401, one refresh answers, both repeat.
      h.adapter.replyJson(failureBody('expired'), status: 401);
      h.adapter.replyJson(failureBody('expired'), status: 401);
      h.adapter.replyJson(sessionBody(access: 'access-2', refresh: 'refresh-2'));
      h.adapter.replyJson(profileBody());
      h.adapter.replyJson(profileBody());
      await Future.wait([h.repo.profile(), h.repo.profile()]);

      expect(h.paths.where((p) => p == 'POST /api/auth/refresh'), hasLength(1));
      expect(h.store.token, 'refresh-2');
    });

    test('a rejected refresh signs the user out, clears the store and surfaces the 401', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.login(email: 'a@b.co', password: 'x');

      h.adapter.replyJson(failureBody('expired'), status: 401);
      h.adapter.replyJson(failureBody('Invalid refresh token.'), status: 401);
      await expectLater(h.repo.profile(), throwsA(isA<UnauthorizedException>()));
      expect(h.store.token, isNull);
      expect(h.sessionEnded, 1);
      expect(await h.repo.accessToken(), isNull);
    });

    test('a refresh that fails on the network keeps the stored token', () async {
      final h = AuthHarness(storedToken: 'refresh-1');
      h.adapter.fail(DioExceptionType.connectionError);
      expect(await h.repo.refreshSession(), isFalse);
      expect(h.store.token, 'refresh-1');
      expect(h.sessionEnded, 0);
    });

    test('an anonymous request that gets a 401 does not try to refresh', () async {
      final h = AuthHarness(storedToken: 'refresh-1');
      h.adapter.replyJson(failureBody('no'), status: 401);
      await expectLater(h.repo.profile(), throwsA(isA<UnauthorizedException>()));
      expect(h.paths, ['GET /api/customer/profile']);
    });
  });

  group('restore and sign out', () {
    test('without a stored token nothing is requested', () async {
      final h = AuthHarness();
      expect(await h.repo.restore(), isNull);
      expect(h.adapter.requests, isEmpty);
    });

    test('with a stored token the session is refreshed and the profile loaded', () async {
      final h = AuthHarness(storedToken: 'refresh-1');
      h.adapter.replyJson(sessionBody(access: 'access-2', refresh: 'refresh-2'));
      h.adapter.replyJson(profileBody(name: 'Asha Kulkarni', phone: '9876543210'));
      final user = await h.repo.restore();
      expect(user?.fullName, 'Asha Kulkarni');
      expect(user?.phoneNumber, '9876543210');
      expect(h.store.token, 'refresh-2');
      expect(h.authorizationOf(1), 'Bearer access-2');
    });

    test('an offline start restores nothing but keeps the token for next time', () async {
      final h = AuthHarness(storedToken: 'refresh-1');
      h.adapter.fail(DioExceptionType.connectionTimeout);
      expect(await h.repo.restore(), isNull);
      expect(h.store.token, 'refresh-1');
    });

    test('logout revokes on the server and forgets the session', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.login(email: 'a@b.co', password: 'x');
      h.adapter.replyJson({'success': true});
      await h.repo.logout();
      expect(h.paths.last, 'POST /api/auth/logout');
      expect(h.adapter.requests.last.data, {'refreshToken': 'refresh-1'});
      expect(h.store.token, isNull);
      expect(await h.repo.accessToken(), isNull);
    });

    test('logout still signs out on this device when the server cannot be reached', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.login(email: 'a@b.co', password: 'x');
      h.adapter.fail(DioExceptionType.connectionError);
      await h.repo.logout();
      expect(h.store.token, isNull);
      expect(await h.repo.accessToken(), isNull);
    });
  });

  group('passwords', () {
    test('change password sends the current refresh token so this device stays signed in', () async {
      final h = AuthHarness();
      h.adapter.replyJson(sessionBody());
      await h.repo.login(email: 'a@b.co', password: 'x');
      h.adapter.replyJson({'success': true, 'data': null});
      await h.repo.changePassword(currentPassword: 'Sunrise-42x', newPassword: 'Harvest-55z');
      expect(h.adapter.requests.last.data, {'currentPassword': 'Sunrise-42x', 'newPassword': 'Harvest-55z', 'refreshToken': 'refresh-1'});
    });

    test('forgot and reset send trimmed values', () async {
      final h = AuthHarness();
      h.adapter.replyJson({'success': true, 'data': null});
      h.adapter.replyJson({'success': true, 'data': null});
      await h.repo.forgotPassword(' asha@example.test ');
      await h.repo.resetPassword(email: 'asha@example.test', code: ' ABCD2345 ', newPassword: 'Harvest-55z');
      expect(h.adapter.requests[0].data, {'email': 'asha@example.test'});
      expect(h.adapter.requests[1].data, {'email': 'asha@example.test', 'code': 'ABCD2345', 'newPassword': 'Harvest-55z'});
    });

    test('a rejected code carries the API message as a validation error', () async {
      final h = AuthHarness();
      h.adapter.replyJson(failureBody('Invalid or expired code.', ['Invalid or expired code.']), status: 400);
      await expectLater(
        h.repo.resetPassword(email: 'a@b.co', code: 'ZZZZZZZZ', newPassword: 'Harvest-55z'),
        throwsA(isA<ValidationException>().having((e) => e.errors, 'errors', ['Invalid or expired code.'])),
      );
    });
  });
}
