import '../api/api_client.dart';
import '../api/api_exception.dart';
import '../dto/catalog_dto.dart';
import '../dto/order_dto.dart';
import '../models.dart';

/// Which ways of paying the server offers right now, so the checkout shows only those.
class PaymentOptions {
  const PaymentOptions({this.online = false, this.holdMinutes = 15});
  final bool online;

  /// How long the items are held for an unpaid online order.
  final int holdMinutes;
}

/// What the payment screen needs to take a payment. The amount is the server's own total; nothing here is secret.
class PaymentSession {
  const PaymentSession({required this.keyId, required this.providerOrderId, required this.amountPaise, required this.currency, required this.orderNumber, required this.expiresAt});
  final String keyId, providerOrderId, currency, orderNumber;
  final int amountPaise;
  final DateTime expiresAt;
}

/// What the payment screen reports back after the customer paid. The server checks the signature and asks the provider itself.
class PaymentProof {
  const PaymentProof({required this.providerOrderId, required this.providerPaymentId, required this.signature});
  final String providerOrderId, providerPaymentId, signature;
}

/// Stable codes in the `reason` of a refused payment call (see the API reference).
abstract final class PaymentReasons {
  static const unavailable = 'PaymentsUnavailable';
  static const holdExpired = 'PaymentHoldExpired';
  static const alreadyPaid = 'AlreadyPaid';
  static const notAwaitingPayment = 'OrderNotAwaitingPayment';
  static const providerUnavailable = 'PaymentProviderUnavailable';
  static const notCaptured = 'PaymentNotCaptured';
}

abstract interface class PaymentRepository {
  Future<PaymentOptions> options();

  /// Starts (or resumes, inside the hold) the payment of an online order. Asking again returns the same provider order, so an order
  /// can never be charged twice. A [ConflictException] with a reason says why not (see [PaymentReasons]).
  Future<PaymentSession> start(String orderId);

  /// Reports the result of the payment screen. Repeating it changes nothing. Returns the order as it now stands.
  Future<Order> confirm(String orderId, PaymentProof proof);
}

/// `api/payments/options` and `api/customer/orders/{id}/payment`.
class ApiPaymentRepository implements PaymentRepository {
  ApiPaymentRepository(this._api);
  final ApiClient _api;

  @override
  Future<PaymentOptions> options() => _api.get('/api/payments/options', parse: (d) {
        final j = asObject(d);
        return PaymentOptions(online: j['online'] == true, holdMinutes: (j['holdMinutes'] as num?)?.toInt() ?? 15);
      });

  @override
  Future<PaymentSession> start(String orderId) => _api.post('/api/customer/orders/$orderId/payment', parse: (d) {
        final j = asObject(d);
        return PaymentSession(
          keyId: j['keyId'] as String,
          providerOrderId: j['providerOrderId'] as String,
          amountPaise: (j['amountPaise'] as num).toInt(),
          currency: (j['currency'] as String?) ?? 'INR',
          orderNumber: (j['orderNumber'] as String?) ?? '',
          expiresAt: DateTime.parse(j['expiresAt'] as String).toLocal(),
        );
      });

  @override
  Future<Order> confirm(String orderId, PaymentProof proof) => _api.post(
        '/api/customer/orders/$orderId/payment/confirm',
        body: {'providerOrderId': proof.providerOrderId, 'providerPaymentId': proof.providerPaymentId, 'signature': proof.signature},
        parse: (d) => orderFromJson(asObject(d)),
      );
}

/// Without a server (offline seed mode) there is nothing to pay online.
class LocalPaymentRepository implements PaymentRepository {
  @override
  Future<PaymentOptions> options() async => const PaymentOptions();

  @override
  Future<PaymentSession> start(String orderId) async => throw const ConflictException('Online payment is not available', statusCode: 409, reason: PaymentReasons.unavailable);

  @override
  Future<Order> confirm(String orderId, PaymentProof proof) async => throw const ConflictException('Online payment is not available', statusCode: 409, reason: PaymentReasons.unavailable);
}
