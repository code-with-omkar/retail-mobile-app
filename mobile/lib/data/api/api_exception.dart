/// Typed failures from the API layer. The UI shows [userMessageKey] (an English string that is
/// translated with `context.tr`), never [message] or a raw exception, so server internals do not
/// leak to users. [correlationId] ties a failure to the API's logs (`X-Correlation-ID`).
sealed class ApiException implements Exception {
  const ApiException(this.message, {this.statusCode, this.correlationId, this.errors = const []});

  /// Server or client supplied description. For logs and developers only.
  final String message;
  final int? statusCode;
  final String? correlationId;

  /// Validation details from the API's `errors` array (or ProblemDetails map, flattened).
  final List<String> errors;

  String get userMessageKey;

  @override
  String toString() => '$runtimeType(${statusCode ?? '-'}): $message${correlationId == null ? '' : ' [$correlationId]'}';
}

/// No connection, DNS failure, or the server refused the connection.
final class NetworkException extends ApiException {
  const NetworkException(super.message, {super.correlationId});
  @override
  String get userMessageKey => 'Cannot reach the server. Check your connection and try again.';
}

final class TimeoutApiException extends ApiException {
  const TimeoutApiException(super.message, {super.correlationId});
  @override
  String get userMessageKey => 'The server took too long to respond. Please try again.';
}

/// 400 / 422: the request was understood but rejected. [errors] lists field or rule messages.
final class ValidationException extends ApiException {
  const ValidationException(super.message, {super.statusCode, super.correlationId, super.errors});
  @override
  String get userMessageKey => 'Please check the details you entered.';
}

/// 401: missing, expired or invalid credentials.
final class UnauthorizedException extends ApiException {
  const UnauthorizedException(super.message, {super.statusCode, super.correlationId});
  @override
  String get userMessageKey => 'Please sign in to continue.';
}

/// 403: signed in but not allowed.
final class ForbiddenException extends ApiException {
  const ForbiddenException(super.message, {super.statusCode, super.correlationId});
  @override
  String get userMessageKey => 'You do not have access to this.';
}

final class NotFoundException extends ApiException {
  const NotFoundException(super.message, {super.statusCode, super.correlationId});
  @override
  String get userMessageKey => 'We could not find what you were looking for.';
}

/// 409: state conflict, for example price changed or stock no longer available at checkout.
final class ConflictException extends ApiException {
  const ConflictException(super.message, {super.statusCode, super.correlationId, super.errors});
  @override
  String get userMessageKey => 'This changed while you were shopping. Please review and try again.';
}

final class TooManyRequestsException extends ApiException {
  const TooManyRequestsException(super.message, {super.statusCode, super.correlationId});
  @override
  String get userMessageKey => 'Too many attempts. Please wait a moment and try again.';
}

/// 5xx from the API.
final class ServerException extends ApiException {
  const ServerException(super.message, {super.statusCode, super.correlationId});
  @override
  String get userMessageKey => 'Something went wrong on our side. Please try again.';
}

/// The response was not the shape this app expects (missing envelope, wrong field types).
final class UnexpectedResponseException extends ApiException {
  const UnexpectedResponseException(super.message, {super.statusCode, super.correlationId});
  @override
  String get userMessageKey => 'Something went wrong. Please try again.';
}

/// Every key the UI can show, so tests can verify translations exist.
const apiErrorMessageKeys = <String>[
  'Cannot reach the server. Check your connection and try again.',
  'The server took too long to respond. Please try again.',
  'Please check the details you entered.',
  'Please sign in to continue.',
  'You do not have access to this.',
  'We could not find what you were looking for.',
  'This changed while you were shopping. Please review and try again.',
  'Too many attempts. Please wait a moment and try again.',
  'Something went wrong on our side. Please try again.',
  'Something went wrong. Please try again.',
];
