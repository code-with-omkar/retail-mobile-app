import 'dart:async';

import 'package:quickcart_customer/features/festival/festival.dart';

/// Runs before every test file. Festival animations never stop, so a test that merely opens Home must not meet one by accident
/// (it would never "settle"): the clock for festivals points at a day without a festival. Tests about festivals set their own date.
Future<void> testExecutable(FutureOr<void> Function() testMain) async {
  festivalNow = () => DateTime(2026, 9, 1);
  await testMain();
}
