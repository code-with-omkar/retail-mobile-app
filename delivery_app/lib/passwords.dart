import 'package:flutter/material.dart';

import 'api.dart';

const _red = Color(0xFFB00060);

/// What is wrong with a new password, before asking the server; null when it looks acceptable.
String? passwordProblem(String password, String again) {
  if (password.length < 8) return 'Use at least 8 characters.';
  if (!password.contains(RegExp('[A-Za-z]')) || !password.contains(RegExp('[0-9]'))) return 'Use letters and at least one number.';
  return password == again ? null : 'The two passwords are not the same.';
}

Widget _field(Key key, TextEditingController controller, String label, {bool secret = true}) => Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: TextField(key: key, controller: controller, obscureText: secret, autocorrect: false, decoration: InputDecoration(labelText: label)),
    );

Widget _problem(String? text) => text == null
    ? const SizedBox.shrink()
    : Padding(padding: const EdgeInsets.only(bottom: 12), child: Text(text, key: const Key('error'), style: const TextStyle(color: _red, fontWeight: FontWeight.w700)));

/// Change the password. [forced] is for the first sign-in with a temporary one: there is no way past it except signing out.
class ChangePasswordScreen extends StatefulWidget {
  const ChangePasswordScreen({super.key, required this.api, required this.onDone, this.forced = false, this.onSignOut});
  final DeliveryApi api;
  final VoidCallback onDone;
  final bool forced;
  final VoidCallback? onSignOut;

  @override
  State<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends State<ChangePasswordScreen> {
  final _current = TextEditingController(), _next = TextEditingController(), _again = TextEditingController();
  String? _error;
  bool _busy = false;

  Future<void> _save() async {
    if (_busy) return;
    final problem = _current.text.isEmpty
        ? 'Enter your current password.'
        : passwordProblem(_next.text, _again.text) ?? (_next.text == _current.text ? 'Choose a password different from the current one.' : null);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }

    setState(() { _busy = true; _error = null; });
    try {
      await widget.api.changePassword(_current.text, _next.text);
      if (mounted) widget.onDone();
    } on ApiError catch (e) {
      if (mounted) setState(() { _busy = false; _error = e.message; });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: Text(widget.forced ? 'Choose your password' : 'Change password'),
          actions: [if (widget.forced) IconButton(tooltip: 'Sign out', onPressed: widget.onSignOut, icon: const Icon(Icons.logout))],
        ),
        body: ListView(padding: const EdgeInsets.all(24), children: [
          if (widget.forced) const Padding(padding: EdgeInsets.only(bottom: 16), child: Text('You signed in with a temporary password. Choose your own to continue.')),
          _field(const Key('current'), _current, widget.forced ? 'Temporary password' : 'Current password'),
          _field(const Key('next'), _next, 'New password'),
          _field(const Key('again'), _again, 'New password again'),
          const Padding(padding: EdgeInsets.only(bottom: 12), child: Text('At least 8 characters, with letters and a number.', style: TextStyle(fontSize: 12))),
          _problem(_error),
          FilledButton(key: const Key('save-password'), onPressed: _busy ? null : _save, child: Text(_busy ? 'Saving…' : 'Save password')),
        ]),
      );
}

/// A code by email, then a new password.
class ForgotPasswordScreen extends StatefulWidget {
  const ForgotPasswordScreen({super.key, required this.api});
  final DeliveryApi api;

  @override
  State<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends State<ForgotPasswordScreen> {
  final _email = TextEditingController(), _code = TextEditingController(), _next = TextEditingController(), _again = TextEditingController();
  String? _error;
  bool _busy = false;
  bool _sent = false;

  Future<void> _run(Future<void> Function() action, VoidCallback then) async {
    setState(() { _busy = true; _error = null; });
    try {
      await action();
      if (!mounted) return;
      setState(() => _busy = false);
      then();
    } on ApiError catch (e) {
      if (mounted) setState(() { _busy = false; _error = e.message; });
    }
  }

  Future<void> _send() async {
    if (_busy) return;
    if (_email.text.trim().isEmpty) {
      setState(() => _error = 'Enter your email address.');
      return;
    }
    await _run(() => widget.api.forgotPassword(_email.text), () => setState(() => _sent = true));
  }

  Future<void> _save() async {
    if (_busy) return;
    final problem = _code.text.trim().isEmpty ? 'Enter the code from the email.' : passwordProblem(_next.text, _again.text);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    await _run(() => widget.api.resetPassword(_email.text, _code.text, _next.text), () {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Password saved. Sign in with it.')));
      Navigator.of(context).pop();
    });
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Forgot password')),
        body: ListView(padding: const EdgeInsets.all(24), children: [
          Padding(padding: const EdgeInsets.only(bottom: 16), child: Text(_sent ? 'If that email has an account, the code is on its way.' : 'We email a code to your address.')),
          if (!_sent) _field(const Key('email'), _email, 'Email', secret: false),
          if (_sent) ...[
            _field(const Key('code'), _code, 'Code from the email', secret: false),
            _field(const Key('next'), _next, 'New password'),
            _field(const Key('again'), _again, 'New password again'),
          ],
          _problem(_error),
          FilledButton(key: const Key('send'), onPressed: _busy ? null : (_sent ? _save : _send), child: Text(_busy ? 'Please wait…' : _sent ? 'Save password' : 'Send code')),
        ]),
      );
}
