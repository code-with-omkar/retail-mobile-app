import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

/// Records requests and replies from a queue, so API client and repository tests need no network.
class FakeAdapter implements HttpClientAdapter {
  final requests = <RequestOptions>[];
  final _replies = <FutureOr<ResponseBody> Function(RequestOptions)>[];

  /// Queue a JSON reply with [status]. [headers] are added to the response.
  void replyJson(Object? body, {int status = 200, Map<String, String> headers = const {}}) {
    _replies.add((_) => ResponseBody.fromString(jsonEncode(body), status, headers: {
          Headers.contentTypeHeader: ['application/json'],
          for (final e in headers.entries) e.key: [e.value],
        }));
  }

  /// Queue a plain-text reply (health endpoints).
  void replyText(String text, {int status = 200}) {
    _replies.add((_) => ResponseBody.fromString(text, status, headers: {
          Headers.contentTypeHeader: ['text/plain'],
        }));
  }

  /// Queue a transport failure of the given [type].
  void fail(DioExceptionType type) {
    _replies.add((o) => throw DioException(requestOptions: o, type: type, message: type.name));
  }

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    requests.add(options);
    if (_replies.isEmpty) throw StateError('FakeAdapter: no reply queued for ${options.method} ${options.path}');
    return _replies.removeAt(0)(options);
  }

  @override
  void close({bool force = false}) {}
}
