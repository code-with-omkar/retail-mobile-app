import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/analytics.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';
import '../../data/repositories/coupon_repository.dart';
import 'cart_controller.dart';

/// The code the customer typed in the cart and what the server said about it.
class CouponState {
  const CouponState({this.code, this.preview, this.busy = false, this.error});

  /// The code as typed (upper-case), or null when none.
  final String? code;
  final CouponPreview? preview;
  final bool busy;

  /// Set when the code could not be checked at all (offline, server error); the code itself may be fine.
  final String? error;

  bool get applied => preview?.valid == true;
  double get discount => applied ? preview!.discount : 0;

  /// The code to send with the order: only one the server just accepted.
  String? get orderCode => applied ? code : null;
}

/// One coupon on the cart. It is tried on the server (which knows the limits and the customer's history), and tried again when the
/// cart's item total changes, because a minimum order or a percent depends on it. Checkout checks it a last time.
class CouponController extends Notifier<CouponState> {
  var _round = 0;

  @override
  CouponState build() {
    ref.listen(cartTotalsProvider.select((t) => t.subtotal), (before, after) {
      if (state.code != null && before != after) _check(state.code!);
    });
    ref.listen(cartTotalsProvider.select((t) => t.isEmpty), (_, empty) {
      if (empty) clear();
    });
    return const CouponState();
  }

  Future<void> apply(String typed) async {
    final code = typed.replaceAll(RegExp(r'\s'), '').toUpperCase();
    if (code.isEmpty) return;
    await _check(code);
  }

  void clear() {
    _round++;
    state = const CouponState();
  }

  Future<void> _check(String code) async {
    final round = ++_round;
    state = CouponState(code: code, preview: state.preview, busy: true);
    try {
      await ref.read(cartProvider.notifier).settled();
      final storeId = ref.read(serverCartProvider)?.storeId ?? (await ref.read(selectedStoreProvider.future))?.id;
      if (storeId == null) throw const ConflictException('No store', statusCode: 409);
      final preview = await ref.read(couponRepositoryProvider).preview(storeId: storeId, code: code);
      if (round == _round) {
        state = CouponState(code: code, preview: preview);
        // Counted by kind, never by the code itself.
        ref.read(analyticsProvider).track(preview.valid ? Track.couponApplied : Track.couponRefused, detail: preview.valid ? null : preview.reason?.toLowerCase());
      }
    } on ApiException catch (e) {
      if (round == _round) state = CouponState(code: code, error: e is UnauthorizedException ? 'Sign in to use a code.' : 'Could not check the code. Try again.');
    }
  }
}

final couponProvider = NotifierProvider<CouponController, CouponState>(CouponController.new);
