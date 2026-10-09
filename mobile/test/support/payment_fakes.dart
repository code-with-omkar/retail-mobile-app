import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/payment_launcher.dart';
import 'package:quickcart_customer/data/repositories/payment_repository.dart';

import 'commerce_fakes.dart';

/// Payments in memory: behaves like the server (an order is paid only through [confirm]), records every call and can be told to fail.
class FakePaymentRepository implements PaymentRepository {
  FakePaymentRepository(this.orders, {this.online = true});

  final FakeOrderRepository orders;
  bool online;
  final calls = <String>[];
  final confirmed = <PaymentProof>[];

  /// The next [start] throws this once.
  Object? failStart;

  /// The next [confirm] throws this once.
  Object? failConfirm;

  @override
  Future<PaymentOptions> options() async {
    calls.add('options');
    return PaymentOptions(online: online);
  }

  @override
  Future<PaymentSession> start(String orderId) async {
    calls.add('start $orderId');
    if (failStart case final failure?) {
      failStart = null;
      throw failure;
    }
    final order = orders.orders.firstWhere((o) => o.id == orderId);
    if (order.stage != OrderStage.awaitingPayment) throw const ConflictException('Not awaiting payment', statusCode: 409, reason: PaymentReasons.notAwaitingPayment);
    return PaymentSession(keyId: 'rzp_test_fake', providerOrderId: 'order_$orderId', amountPaise: (order.total * 100).round(), currency: 'INR', orderNumber: order.number, expiresAt: order.paymentExpiresAt!);
  }

  @override
  Future<Order> confirm(String orderId, PaymentProof proof) async {
    calls.add('confirm $orderId');
    confirmed.add(proof);
    if (failConfirm case final failure?) {
      failConfirm = null;
      throw failure;
    }
    return markPaid(orderId);
  }

  /// What the server does when the payment is confirmed (by the app, or by the provider telling the server).
  Order markPaid(String orderId) {
    final at = orders.orders.indexWhere((o) => o.id == orderId);
    return orders.orders[at] = orders.orders[at].withPayment(stage: OrderStage.placed, state: PaymentState.paid);
  }

  /// What the server does when the hold runs out.
  Order expire(String orderId) {
    final at = orders.orders.indexWhere((o) => o.id == orderId);
    return orders.orders[at] = orders.orders[at].withPayment(stage: OrderStage.cancelled, state: PaymentState.failed);
  }
}

/// The provider screen: each call to [pay] ends the way the next queued outcome says (paid when nothing is queued).
class FakePaymentLauncher implements PaymentLauncher {
  final outcomes = <PaymentOutcome>[];
  final sessions = <PaymentSession>[];
  ({String? name, String? phone, String? email})? lastPrefill;

  @override
  Future<PaymentOutcome> pay(PaymentSession session, {String? name, String? phone, String? email}) async {
    sessions.add(session);
    lastPrefill = (name: name, phone: phone, email: email);
    if (outcomes.isNotEmpty) return outcomes.removeAt(0);
    return PaymentPaid(PaymentProof(providerOrderId: session.providerOrderId, providerPaymentId: 'pay_fake_${sessions.length}', signature: 'sig'));
  }
}
