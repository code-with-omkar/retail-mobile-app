import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';

/// 6-digit code entry with resend countdown. PARKED: not routed until phone OTP login is approved (decision D7).
/// Design only: any 6 digits are accepted and nothing is signed in.
class OtpScreen extends ConsumerStatefulWidget {
  const OtpScreen(this.phone, {super.key, this.name});
  final String phone;
  final String? name;

  @override
  ConsumerState<OtpScreen> createState() => _OtpScreenState();
}

class _OtpScreenState extends ConsumerState<OtpScreen> {
  static const _length = 6;
  static const _resendSeconds = 30;
  final _controller = TextEditingController();
  final _focus = FocusNode();
  Timer? _timer;
  int _left = _resendSeconds;

  @override
  void initState() {
    super.initState();
    _startTimer();
  }

  void _startTimer() {
    _timer?.cancel();
    setState(() => _left = _resendSeconds);
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

  void _verify() {
    context.go('/');
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
            // Invisible field that actually receives the keystrokes.
            Positioned.fill(
              child: Opacity(
                opacity: 0,
                child: TextField(
                  controller: _controller,
                  focusNode: _focus,
                  autofocus: true,
                  keyboardType: TextInputType.number,
                  maxLength: _length,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  enableInteractiveSelection: false,
                  onChanged: (_) => setState(() {}),
                ),
              ),
            ),
          ]),
        ),
        const SizedBox(height: 20),
        Center(
          child: _left > 0
              ? Text(context.tr('Resend code in 0:{s}', {'s': _left.toString().padLeft(2, '0')}), style: const TextStyle(color: Pal.mutedOnGround))
              : TextButton(onPressed: _startTimer, child: Text(context.tr('Resend code'))),
        ),
        const SizedBox(height: 12),
        PillButton(context.tr('Verify and continue'), arrow: true, onPressed: code.length == _length ? _verify : null),
        Center(child: TextButton(onPressed: () => context.pop(), child: Text(context.tr('Change number')))),
        const SizedBox(height: 8),
        Text(context.tr('Design preview: any 6 digits will work.'), textAlign: TextAlign.center, style: const TextStyle(color: Pal.mutedOnGround, fontSize: 12)),
      ]),
    );
  }
}
