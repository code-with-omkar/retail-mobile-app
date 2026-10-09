import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:razorpay_flutter/razorpay_flutter.dart';

import 'repositories/payment_repository.dart';

/// How the customer's payment attempt ended on the provider's own screen.
sealed class PaymentOutcome {
  const PaymentOutcome();
}

/// The provider says it was paid. The server still checks it before the order counts as paid.
final class PaymentPaid extends PaymentOutcome {
  const PaymentPaid(this.proof);
  final PaymentProof proof;
}

/// The customer closed the screen without paying.
final class PaymentDismissed extends PaymentOutcome {
  const PaymentDismissed();
}

/// The attempt failed (declined, timed out, no network). The customer can try again.
final class PaymentFailed extends PaymentOutcome {
  const PaymentFailed();
}

/// Opens the provider's payment screen. Card, UPI and bank details are entered there and never reach this app or our server.
abstract interface class PaymentLauncher {
  Future<PaymentOutcome> pay(PaymentSession session, {String? name, String? phone, String? email});
}

class RazorpayLauncher implements PaymentLauncher {
  @override
  Future<PaymentOutcome> pay(PaymentSession session, {String? name, String? phone, String? email}) {
    final done = Completer<PaymentOutcome>();
    final razorpay = Razorpay();
    void finish(PaymentOutcome outcome) {
      if (!done.isCompleted) done.complete(outcome);
    }

    razorpay.on(Razorpay.EVENT_PAYMENT_SUCCESS, (PaymentSuccessResponse r) {
      final payment = r.paymentId, order = r.orderId, signature = r.signature;
      finish(payment == null || order == null || signature == null
          ? const PaymentFailed()
          : PaymentPaid(PaymentProof(providerOrderId: order, providerPaymentId: payment, signature: signature)));
    });
    razorpay.on(Razorpay.EVENT_PAYMENT_ERROR, (PaymentFailureResponse r) {
      // Only the provider's own code and short text (never keys); it is what tells a declined test payment from a setup problem.
      debugPrint('[payment] provider reported code=${r.code} message=${r.message}');
      finish(r.code == Razorpay.PAYMENT_CANCELLED ? const PaymentDismissed() : const PaymentFailed());
    });
    // A wallet that finishes outside the app is settled by the provider telling the server, so the app just waits for that.
    razorpay.on(Razorpay.EVENT_EXTERNAL_WALLET, (ExternalWalletResponse _) => finish(const PaymentDismissed()));
    try {
      razorpay.open({
        'key': session.keyId,
        'order_id': session.providerOrderId,
        'amount': session.amountPaise,
        'currency': session.currency,
        'name': 'QuickCart',
        'description': 'Order ${session.orderNumber}',
        'prefill': {'name': ?name, 'contact': ?phone, 'email': ?email},
        'retry': {'enabled': false},
        'timeout': session.expiresAt.difference(DateTime.now()).inSeconds.clamp(60, 900),
      });
    } catch (_) {
      finish(const PaymentFailed());
    }
    return done.future.whenComplete(razorpay.clear);
  }
}
