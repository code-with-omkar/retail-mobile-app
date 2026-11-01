import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';
import 'auth_controller.dart';
import 'auth_widgets.dart';

/// Six-digit code entry with a resend countdown. Signs the customer in, or (with [verify]) proves a number for the account they already have.
class OtpScreen extends ConsumerStatefulWidget {
  const OtpScreen(this.phone, {super.key, this.verify = false, this.next, this.resendSeconds = 30});
  final String phone;
  final bool verify;
  final String? next;
  final int resendSeconds;

  @override
  ConsumerState<OtpScreen> createState() => _OtpScreenState();
}

class _OtpScreenState extends ConsumerState<OtpScreen> {
  static const _length = 6;
  final _controller = TextEditingController();
  final _focus = FocusNode();
  Timer? _timer;
  late int _left = widget.resendSeconds;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _startTimer(widget.resendSeconds);
  }

  void _startTimer(int seconds) {
    _timer?.cancel();
    setState(() => _left = seconds);
    if (seconds <= 0) return;
    _timer = Timer.periodic(const Duration(seconds: 1), (t) {
      if (_left <= 1) t.cancel();
      if (mounted) setState(() => _left--);
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  String _message(Object e) {
    if (kDebugMode) debugPrint('[otp] failed: $e');
    return switch (e) {
      UnauthorizedException() => context.tr('That code is not valid or has expired. Check it or ask for a new one.'),
      ConflictException() when widget.verify => context.tr('This number is already verified on another account.'),
      TooManyRequestsException() => context.tr('Too many attempts. Please wait a moment and try again.'),
      ApiException() => context.tr(e.userMessageKey),
      _ => context.tr('Something went wrong. Please try again.'),
    };
  }

  Future<void> _verify() async {
    if (_busy || _controller.text.length != _length) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(authRepositoryProvider);
      if (widget.verify) {
        await repo.verifyPhone(phone: widget.phone, code: _controller.text);
        final profile = await repo.profile();
        if (!mounted) return;
        ref.read(authProvider.notifier).profileChanged(profile);
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Number verified. You can now sign in with a code.'))));
        context.go('/profile');
      } else {
        await ref.read(authProvider.notifier).loginWithOtp(widget.phone, _controller.text);
        if (mounted) context.go(safeNext(widget.next));
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = _message(e);
          _controller.clear();
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _resend() async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(authRepositoryProvider);
      final sent = widget.verify ? await repo.requestPhoneVerification(widget.phone) : await repo.requestOtp(widget.phone);
      if (mounted) {
        _controller.clear();
        _startTimer(sent.resendAfterSeconds);
      }
    } catch (e) {
      if (mounted) setState(() => _error = _message(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final code = _controller.text;
    final masked = widget.phone.length == 10 ? '+91 ${widget.phone.substring(0, 2)}•••••${widget.phone.substring(7)}' : '+91 ${widget.phone}';
    return AppScaffold(
      appBar: appTopBar(context, ''),
      body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24), children: [
        Text(context.tr('Verify your number'), style: const TextStyle(color: Colors.white, fontSize: 34, fontWeight: FontWeight.w900, height: 1.1)),
        const SizedBox(height: 8),
        Text(context.tr('Enter the 6-digit code sent to {phone}', {'phone': masked}), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 16)),
        const SizedBox(height: 28),
        GestureDetector(
          onTap: _focus.requestFocus,
          child: Stack(children: [
            Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              for (var i = 0; i < _length; i++)
                Container(
                  width: 48,
                  height: 60,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: p.card, borderRadius: BorderRadius.circular(QC.rField), border: Border.all(color: i == code.length ? Pal.yellow : Colors.transparent, width: 2.5)),
                  child: Text(i < code.length ? code[i] : '', style: TextStyle(color: p.onCard, fontSize: 24, fontWeight: FontWeight.w900)),
                ),
            ]),
            // Invisible field that actually receives the keystrokes (and the code a text-message app offers to fill in).
            Positioned.fill(
              child: Opacity(
                opacity: 0,
                child: TextField(
                  key: const Key('otp-field'),
                  controller: _controller,
                  focusNode: _focus,
                  autofocus: true,
                  enabled: !_busy,
                  keyboardType: TextInputType.number,
                  autofillHints: const [AutofillHints.oneTimeCode],
                  maxLength: _length,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  enableInteractiveSelection: false,
                  onChanged: (_) => setState(() => _error = null),
                ),
              ),
            ),
          ]),
        ),
        const SizedBox(height: 20),
        if (_error != null) ErrorBanner(_error!),
        Center(
          child: _left > 0
              ? Text(context.tr('Resend code in 0:{s}', {'s': _left.toString().padLeft(2, '0')}), style: const TextStyle(color: Pal.mutedOnGround))
              : TextButton(onPressed: _busy ? null : _resend, child: Text(context.tr('Resend code'))),
        ),
        const SizedBox(height: 12),
        PillButton(context.tr(_busy ? 'Please wait…' : 'Verify and continue'), arrow: !_busy, onPressed: code.length == _length && !_busy ? _verify : null),
        Center(child: TextButton(onPressed: () => context.pop(), child: Text(context.tr('Change number')))),
      ]),
    );
  }
}
