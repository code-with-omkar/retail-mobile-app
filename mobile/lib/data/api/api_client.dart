import 'dart:math';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../core/config/app_config.dart';
import 'api_exception.dart';

typedef TokenProvider = Future<String?> Function();

/// Single HTTP entry point for the QuickCart API.
///
/// The API wraps every JSON response as `{ "success": true, "data": ... }` on success and
/// `{ "success": false, "message": "...", "errors": [...] }` on failure. This client unwraps the
/// envelope, maps failures to [ApiException] subtypes, and attaches `X-Correlation-ID`,
/// `Accept-Language` and (when a [tokenProvider] is given) the bearer token.
///
/// When a request that carried a token comes back 401, [onUnauthorized] is asked once to renew the session
/// (it must be single-flight, see `AuthRepository.refreshSession`). If it returns true the request is repeated once with
/// the new token; otherwise the 401 is thrown. `/api/auth/*` routes are never retried: a 401 there means wrong credentials.
class ApiClient {
  ApiClient({required AppConfig config, Dio? dio, this.tokenProvider, this.languageCode, this.onUnauthorized})
      : _dio = dio ?? Dio() {
    _dio.options
      ..baseUrl = config.apiBaseUrl
      ..connectTimeout = config.connectTimeout
      ..receiveTimeout = config.receiveTimeout
      ..headers = {'Accept': 'application/json'}
      // We map status codes ourselves so the envelope message is available for every failure.
      ..validateStatus = (_) => true;
  }

  final Dio _dio;
  final TokenProvider? tokenProvider;
  final Future<bool> Function()? onUnauthorized;

  /// Returns the language to request (`en` / `mr`); read at call time so a language switch applies at once.
  final String Function()? languageCode;
  static final _random = Random();

  /// GET that returns the parsed `data` payload.
  Future<T> get<T>(String path, {Map<String, dynamic>? query, required T Function(Object? data) parse, CancelToken? cancelToken}) =>
      _call('GET', path, query: query, parse: parse, cancelToken: cancelToken);

  Future<T> post<T>(String path, {Object? body, Map<String, dynamic>? query, Map<String, String>? headers, required T Function(Object? data) parse, CancelToken? cancelToken}) =>
      _call('POST', path, body: body, query: query, headers: headers, parse: parse, cancelToken: cancelToken);

  Future<T> put<T>(String path, {Object? body, required T Function(Object? data) parse, CancelToken? cancelToken}) =>
      _call('PUT', path, body: body, parse: parse, cancelToken: cancelToken);

  Future<T> delete<T>(String path, {required T Function(Object? data) parse, CancelToken? cancelToken}) =>
      _call('DELETE', path, parse: parse, cancelToken: cancelToken);

  /// GET for non-envelope endpoints such as `/health/ready` (plain text body). Returns the status code and body.
  Future<({int statusCode, String body, String? correlationId})> probe(String path) async {
    final correlation = _newCorrelationId();
    try {
      final r = await _dio.get<String>(path, options: Options(responseType: ResponseType.plain, headers: {'X-Correlation-ID': correlation}));
      return (statusCode: r.statusCode ?? 0, body: (r.data ?? '').trim(), correlationId: r.headers.value('X-Correlation-ID') ?? correlation);
    } on DioException catch (e) {
      throw _fromDio(e, correlation);
    }
  }

  Future<T> _call<T>(
    String method,
    String path, {
    Object? body,
    Map<String, dynamic>? query,
    Map<String, String>? headers,
    required T Function(Object? data) parse,
    CancelToken? cancelToken,
    bool mayRetry = true,
  }) async {
    final correlation = _newCorrelationId();
    final token = await tokenProvider?.call();
    final lang = languageCode?.call();
    try {
      final response = await _dio.request<Object?>(
        path,
        data: body,
        queryParameters: query,
        cancelToken: cancelToken,
        options: Options(method: method, headers: {
          'X-Correlation-ID': correlation,
          'Accept-Language': ?lang,
          if (token != null && token.isNotEmpty) 'Authorization': 'Bearer $token',
          ...?headers,
        }),
      );
      final id = response.headers.value('X-Correlation-ID') ?? correlation;
      _log(method, path, response.statusCode, id);
      final result = _unwrap(response, id, parse);
      return result;
    } on UnauthorizedException {
      final refresh = onUnauthorized;
      final hadToken = token != null && token.isNotEmpty;
      if (mayRetry && hadToken && refresh != null && !path.startsWith('/api/auth/') && await refresh()) {
        return _call(method, path, body: body, query: query, headers: headers, parse: parse, cancelToken: cancelToken, mayRetry: false);
      }
      rethrow;
    } on DioException catch (e) {
      _log(method, path, e.response?.statusCode, correlation, failure: e.type.name);
      throw _fromDio(e, correlation);
    }
  }

  T _unwrap<T>(Response<Object?> r, String correlationId, T Function(Object? data) parse) {
    final status = r.statusCode ?? 0;
    final body = r.data;
    final map = body is Map<String, dynamic> ? body : null;
    // 204: success with nothing to return (for example no cart yet).
    if (status == 204) return parse(null);
    if (status >= 200 && status < 300) {
      if (map == null || map['success'] != true) {
        throw UnexpectedResponseException('Response is not a success envelope', statusCode: status, correlationId: correlationId);
      }
      try {
        return parse(map['data']);
      } on ApiException {
        rethrow;
      } catch (e) {
        throw UnexpectedResponseException('Could not read response data: $e', statusCode: status, correlationId: correlationId);
      }
    }
    throw _fromStatus(status, map, correlationId);
  }

  ApiException _fromStatus(int status, Map<String, dynamic>? map, String id) {
    final message = (map?['message'] as String?) ?? 'HTTP $status';
    final errors = _errors(map);
    return switch (status) {
      400 || 422 => ValidationException(message, statusCode: status, correlationId: id, errors: errors),
      401 => UnauthorizedException(message, statusCode: status, correlationId: id),
      403 => ForbiddenException(message, statusCode: status, correlationId: id),
      404 => NotFoundException(message, statusCode: status, correlationId: id),
      409 => ConflictException(message, statusCode: status, correlationId: id, errors: errors, reason: map?['reason'] as String?, details: map?['details'] is List ? List<Object?>.of(map!['details'] as List) : const []),
      429 => TooManyRequestsException(message, statusCode: status, correlationId: id),
      >= 500 => ServerException(message, statusCode: status, correlationId: id),
      _ => UnexpectedResponseException(message, statusCode: status, correlationId: id),
    };
  }

  /// `errors` is a string list in this API, but ASP.NET's default model-validation response is a
  /// `{ field: [messages] }` map. Accept both.
  List<String> _errors(Map<String, dynamic>? map) {
    final e = map?['errors'];
    if (e is List) return e.map((x) => '$x').toList();
    if (e is Map) return [for (final entry in e.entries) for (final m in (entry.value is List ? entry.value as List : [entry.value])) '${entry.key}: $m'];
    return const [];
  }

  ApiException _fromDio(DioException e, String correlation) => switch (e.type) {
        DioExceptionType.connectionTimeout || DioExceptionType.sendTimeout || DioExceptionType.receiveTimeout => TimeoutApiException(e.message ?? 'Timed out', correlationId: correlation),
        DioExceptionType.cancel => const NetworkException('Request cancelled'),
        DioExceptionType.badResponse => _fromStatus(e.response?.statusCode ?? 0, e.response?.data is Map<String, dynamic> ? e.response!.data as Map<String, dynamic> : null, correlation),
        _ => NetworkException(e.message ?? 'Network error', correlationId: correlation),
      };

  /// Random 128-bit hex id. Enough to correlate a request across app and API logs.
  static String _newCorrelationId() => List.generate(16, (_) => _random.nextInt(256).toRadixString(16).padLeft(2, '0')).join();

  /// Method, path, status and correlation id only. Bodies and headers (tokens, addresses, phone numbers) are never logged.
  void _log(String method, String path, int? status, String id, {String? failure}) {
    if (!kDebugMode) return;
    debugPrint('[api] $method $path -> ${status ?? failure ?? '?'} ($id)');
  }
}
