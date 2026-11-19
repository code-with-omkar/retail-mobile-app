import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';
import 'auth_controller.dart';
import 'auth_widgets.dart';

/// Asks for the mobile number and sends the code. Signs in with it, or (with [verify]) proves the number for the signed-in account.
class PhoneScreen extends ConsumerStatefulWidget {
  const PhoneScreen({super.key, this.next, this.verify = false});
  final String? next;
  final bool verify;

  @override
  ConsumerState<PhoneScreen> createState() => _PhoneScreenState();
}

class _PhoneScreenState extends ConsumerState<PhoneScreen> {
  final _form = GlobalKey<FormState>();
  late final _phone = TextEditingController(text: widget.verify ? _digitsOf(ref.read(authProvider).user?.phoneNumber) : '');
  bool _busy = false;
  String? _error;

  /// The 10 digits of a stored number, whatever form it was typed in.
  static String _digitsOf(String? raw) {
    final digits = (raw ?? '').replaceAll(RegExp(r'\D'), '');
    return digits.length >= 10 ? digits.substring(digits.length - 10) : '';
  }

  @override
  void dispose() {
    _phone.dispose();
    super.dispose();
  }

  String? _validate(String? v) {
    final digits = (v ?? '').replaceAll(RegExp(r'\D'), '');
    if (digits.length != 10 || !RegExp(r'^[6-9]').hasMatch(digits)) return context.tr('Enter a 10-digit number');
    return null;
  }

  Future<void> _send() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    final digits = _phone.text.replaceAll(RegExp(r'\D'), '');
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(authRepositoryProvider);
      final sent = widget.verify ? await repo.requestPhoneVerification(digits) : await repo.requestOtp(digits);
      if (!mounted) return;
      final uri = Uri(path: '/otp', queryParameters: {'phone': digits, 'wait': '${sent.resendAfterSeconds}', if (widget.verify) 'verify': '1', if (widget.next != null) 'next': widget.next});
      context.push(uri.toString());
    } catch (e) {
      if (kDebugMode) debugPrint('[phone] request failed: $e');
      if (mounted) {
        setState(() => _error = switch (e) {
              TooManyRequestsException() => context.tr('Too many attempts. Please wait a moment and try again.'),
              ValidationException() => context.tr('Enter a 10-digit number'),
              ApiException() => context.tr(e.userMessageKey),
              _ => context.tr('Something went wrong. Please try again.'),
            });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AuthScaffold(
        title: context.tr(widget.verify ? 'Verify phone number' : 'Your phone number'),
        subtitle: context.tr(widget.verify ? 'We will text a 6-digit code to prove this number is yours.' : 'We will text you a 6-digit code. No password needed.'),
        children: [
          Form(
            key: _form,
            child: AuthField(
              context.tr('Mobile number'),
              Icons.phone_outlined,
              controller: _phone,
              enabled: !_busy,
              keyboard: TextInputType.phone,
              autofillHints: const [AutofillHints.telephoneNumberNational],
              textInputAction: TextInputAction.done,
              formatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9 +\-]')), LengthLimitingTextInputFormatter(15)],
              validator: _validate,
            ),
          ),
          if (_error != null) ErrorBanner(_error!),
          PillButton(context.tr(_busy ? 'Please wait…' : 'Get OTP'), arrow: !_busy, onPressed: _busy ? null : _send),
        ],
      );
}
