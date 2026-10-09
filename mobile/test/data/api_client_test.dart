import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';

import '../support/fake_adapter.dart';

void main() {
  late FakeAdapter adapter;
  late ApiClient client;

  setUp(() {
    adapter = FakeAdapter();
    final dio = Dio()..httpClientAdapter = adapter;
    client = ApiClient(
      config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'),
      dio: dio,
      tokenProvider: () async => 'token-123',
      languageCode: () => 'mr',
    );
  });

  Future<T> getStrings<T>(T Function(Object?) parse) => client.get<T>('/api/x', parse: parse);

  test('unwraps the success envelope and parses data', () async {
    adapter.replyJson({'success': true, 'data': [1, 2, 3]});
    final result = await getStrings((d) => (d as List).cast<int>());
    expect(result, [1, 2, 3]);
  });

  test('sends correlation id, language and bearer token; never logs or sends anything extra', () async {
    adapter.replyJson({'success': true, 'data': null}, headers: {'X-Correlation-ID': 'abc'});
    await getStrings((_) => null);
    final h = adapter.requests.single.headers;
    expect(h['X-Correlation-ID'], matches(RegExp(r'^[0-9a-f]{32}$')));
    expect(h['Accept-Language'], 'mr');
    expect(h['Authorization'], 'Bearer token-123');
    expect(adapter.requests.single.uri.toString(), 'http://api.test/api/x');
  });

  test('omits Authorization when there is no token', () async {
    final c = ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);
    adapter.replyJson({'success': true, 'data': 1});
    await c.get<int>('/api/x', parse: (d) => d as int);
    expect(adapter.requests.single.headers.containsKey('Authorization'), isFalse);
  });

  group('maps failures to typed exceptions', () {
    Future<ApiException> failWith(int status, [Object? body]) async {
      adapter.replyJson(body ?? {'success': false, 'message': 'nope', 'errors': <String>[]}, status: status, headers: {'X-Correlation-ID': 'cid-$status'});
      try {
        await getStrings((_) => null);
      } on ApiException catch (e) {
        return e;
      }
      fail('expected an ApiException');
    }

    test('400 -> ValidationException with the errors list', () async {
      final e = await failWith(400, {'success': false, 'message': 'Invalid', 'errors': ['Quantity must be greater than zero']});
      expect(e, isA<ValidationException>());
      expect(e.errors, ['Quantity must be greater than zero']);
      expect(e.correlationId, 'cid-400');
    });

    test('400 with an ASP.NET errors map is flattened', () async {
      final e = await failWith(400, {
        'errors': {'Email': ['Required', 'Invalid']}
      });
      expect(e.errors, ['Email: Required', 'Email: Invalid']);
    });

    test('401, 403, 404, 409, 429, 500', () async {
      expect(await failWith(401), isA<UnauthorizedException>());
      expect(await failWith(403), isA<ForbiddenException>());
      expect(await failWith(404), isA<NotFoundException>());
      expect(await failWith(409), isA<ConflictException>());
      expect(await failWith(429), isA<TooManyRequestsException>());
      expect(await failWith(500), isA<ServerException>());
    });

    test('server message is kept for logs but the UI key is generic', () async {
      final e = await failWith(500, {'success': false, 'message': 'SqlException: Login failed for user sa', 'errors': <String>[]});
      expect(e.message, contains('SqlException'));
      expect(e.userMessageKey, isNot(contains('Sql')));
    });
  });

  test('2xx without an envelope is an UnexpectedResponseException', () async {
    adapter.replyJson({'hello': 'world'});
    expect(getStrings((d) => d), throwsA(isA<UnexpectedResponseException>()));
  });

  test('a parse failure becomes an UnexpectedResponseException', () async {
    adapter.replyJson({'success': true, 'data': 'not a list'});
    expect(getStrings((d) => (d as List).length), throwsA(isA<UnexpectedResponseException>()));
  });

  test('timeouts and connection errors', () async {
    adapter.fail(DioExceptionType.receiveTimeout);
    expect(getStrings((_) => null), throwsA(isA<TimeoutApiException>()));
    adapter.fail(DioExceptionType.connectionError);
    expect(getStrings((_) => null), throwsA(isA<NetworkException>()));
  });

  test('probe returns status and plain body for health endpoints', () async {
    adapter.replyText('Healthy');
    final r = await client.probe('/health/ready');
    expect(r.statusCode, 200);
    expect(r.body, 'Healthy');
  });
}
