import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/providers.dart';
import '../../data/repositories/payment_repository.dart';

/// Which ways of paying the server offers. When it cannot be asked (offline, error) only cash on delivery is offered, which always works.
final paymentOptionsProvider = FutureProvider.autoDispose<PaymentOptions>((ref) async {
  try {
    return await ref.watch(paymentRepositoryProvider).options();
  } catch (_) {
    return const PaymentOptions();
  }
});
