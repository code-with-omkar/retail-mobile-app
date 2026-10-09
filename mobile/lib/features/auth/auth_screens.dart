import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/language_toggle.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/providers.dart';
import '../../data/seed.dart';
import 'auth_controller.dart';
import 'auth_widgets.dart';

/// Welcome screen (see docs/design 03/06). Phone OTP and Google / Apple sign-in are shown again when decision D7 is made;
/// until then customers use email and password.
class WelcomeScreen extends StatelessWidget {
  const WelcomeScreen({super.key, this.next});
  final String? next;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return AppScaffold(
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, c) => SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: QC.gutter),
            child: ConstrainedBox(
              constraints: BoxConstraints(minHeight: c.maxHeight),
              child: IntrinsicHeight(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const SizedBox(height: 12),
                  Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                    Flexible(child: Row(mainAxisSize: MainAxisSize.min, children: [
                      Container(width: 52, height: 52, decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(18)), child: const Icon(Icons.shopping_basket_outlined, color: Pal.ink)),
                      const SizedBox(width: 10),
                      const Flexible(child: Text('QuickCart', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900))),
                    ])),
                    const LanguageToggle(),
                  ]),
                  const SizedBox(height: 20),
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.all(24),
                    decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(QC.rCard)),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      const Icon(Icons.storefront_outlined, size: 32, color: Pal.ink),
                      const SizedBox(height: 20),
                      Text(context.tr('Your trusted neighbourhood shop, now digital'), style: const TextStyle(color: Pal.ink, fontSize: 22, fontWeight: FontWeight.w900, height: 1.2)),
                    ]),
                  ),
                  const SizedBox(height: 12),
                  Row(children: [
                    Expanded(child: _FeatureTile(p.tiles[0], Icons.timer_outlined, context.tr('{n} min delivery', {'n': '$etaMinutes'}))),
                    const SizedBox(width: 12),
                    Expanded(child: _FeatureTile(p.tiles[1], Icons.eco_outlined, context.tr('Fresh & reliable'))),
                  ]),
                  const SizedBox(height: 24),
                  Text(context.tr('Your local shop,\nnow in your hand.'), style: const TextStyle(color: Colors.white, fontSize: 34, fontWeight: FontWeight.w900, height: 1.05)),
                  const Spacer(),
                  const SizedBox(height: 24),
                  PillButton(context.tr('Log in'), arrow: true, onPressed: () => context.push(withNext('/login', next))),
                  const SizedBox(height: 12),
                  SoftPillButton(context.tr('Create account'), height: 60, onPressed: () => context.push(withNext('/register', next))),
                  Center(child: TextButton(onPressed: () => context.go('/'), child: Text(context.tr('Browse as guest'), style: const TextStyle(color: Pal.mutedOnGround)))),
                  const SizedBox(height: 8),
                ]),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _FeatureTile extends StatelessWidget {
  const _FeatureTile(this.color, this.icon, this.label);
  final Color color;
  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) => Container(
        height: 132,
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(QC.rCard)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Icon(icon, color: Pal.ink, size: 28),
          const Spacer(),
          Text(label, style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 17, height: 1.15)),
        ]),
      );
}

/// Runs [action] for a form screen: one submit at a time, errors shown above the button, never on a disposed screen.
mixin _Submitting<T extends ConsumerStatefulWidget> on ConsumerState<T> {
  bool busy = false;
  String? error;

  Future<void> submit(GlobalKey<FormState> form, Future<void> Function() action, {bool signingIn = false}) async {
    if (busy || !form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await action();
    } catch (e) {
      if (kDebugMode) debugPrint('[auth] submit failed: $e');
      if (mounted) setState(() => error = accountErrorText(context, e, signingIn: signingIn));
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }
}

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key, this.next});
  final String? next;
  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> with _Submitting {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _hide = true;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _login() => submit(_form, () async {
        await ref.read(authProvider.notifier).login(_email.text, _password.text);
        if (mounted) context.go(safeNext(widget.next));
      }, signingIn: true);

  @override
  Widget build(BuildContext context) => AuthScaffold(
        title: context.tr('Welcome back'),
        subtitle: context.tr('Log in to see your orders and saved addresses.'),
        children: [
          Form(
            key: _form,
            child: AutofillGroup(
              child: Column(children: [
                AuthField(context.tr('Email'), Icons.mail_outline, controller: _email, enabled: !busy, keyboard: TextInputType.emailAddress, autofillHints: const [AutofillHints.username, AutofillHints.email], textInputAction: TextInputAction.next, validator: (v) => emailField(context, v)),
                AuthField(
                  context.tr('Password'),
                  Icons.lock_outline,
                  controller: _password,
                  enabled: !busy,
                  obscure: _hide,
                  autofillHints: const [AutofillHints.password],
                  textInputAction: TextInputAction.done,
                  validator: (v) => requiredField(context, v),
                  suffix: passwordToggle(context, _hide, () => setState(() => _hide = !_hide)),
                ),
              ]),
            ),
          ),
          Align(alignment: Alignment.centerRight, child: TextButton(onPressed: () => context.push('/forgot'), child: Text(context.tr('Forgot password?')))),
          if (error != null) ErrorBanner(error!),
          PillButton(context.tr(busy ? 'Please wait…' : 'Log in'), arrow: !busy, onPressed: busy ? null : _login),
          const SizedBox(height: 12),
          Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            Flexible(child: Text(context.tr('New here?'), style: const TextStyle(color: Pal.mutedOnGround))),
            TextButton(onPressed: () => context.pushReplacement(withNext('/register', widget.next)), child: Text(context.tr('Create account'))),
          ]),
        ],
      );
}

class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key, this.next});
  final String? next;
  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> with _Submitting {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _agree = false;
  bool _hide = true;

  @override
  void dispose() {
    _name.dispose();
    _phone.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _register() => submit(_form, () async {
        await ref.read(authProvider.notifier).register(fullName: _name.text, email: _email.text, password: _password.text, phoneNumber: _phone.text.trim().isEmpty ? null : _phone.text);
        if (mounted) context.go(safeNext(widget.next));
      });

  @override
  Widget build(BuildContext context) => AuthScaffold(
        title: context.tr('Create your account'),
        subtitle: context.tr('Sign up in a few seconds and start shopping.'),
        children: [
          Form(
            key: _form,
            child: AutofillGroup(
              child: Column(children: [
                AuthField(context.tr('Full name'), Icons.person_outline, controller: _name, enabled: !busy, autofillHints: const [AutofillHints.name], capitalization: TextCapitalization.words, textInputAction: TextInputAction.next, validator: (v) => requiredField(context, v)),
                AuthField(context.tr('Email'), Icons.mail_outline, controller: _email, enabled: !busy, keyboard: TextInputType.emailAddress, autofillHints: const [AutofillHints.email], textInputAction: TextInputAction.next, validator: (v) => emailField(context, v)),
                AuthField(context.tr('Mobile number (optional)'), Icons.phone_outlined, controller: _phone, enabled: !busy, keyboard: TextInputType.phone, autofillHints: const [AutofillHints.telephoneNumber], formatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9+ ]'))], textInputAction: TextInputAction.next, validator: (v) => optionalPhoneField(context, v)),
                AuthField(
                  context.tr('Password'),
                  Icons.lock_outline,
                  controller: _password,
                  enabled: !busy,
                  obscure: _hide,
                  autofillHints: const [AutofillHints.newPassword],
                  textInputAction: TextInputAction.done,
                  validator: (v) => passwordField(context, v),
                  suffix: passwordToggle(context, _hide, () => setState(() => _hide = !_hide)),
                ),
              ]),
            ),
          ),
          CheckboxListTile(
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            value: _agree,
            onChanged: busy ? null : (v) => setState(() => _agree = v ?? false),
            title: Text(context.tr('I agree to the Terms of Service and Privacy Policy'), style: const TextStyle(fontSize: 13, color: Colors.white)),
          ),
          const SizedBox(height: 8),
          if (error != null) ErrorBanner(error!),
          PillButton(context.tr(busy ? 'Please wait…' : 'Create account'), arrow: !busy, onPressed: (_agree && !busy) ? _register : null),
          Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            Flexible(child: Text(context.tr('Already registered?'), style: const TextStyle(color: Pal.mutedOnGround))),
            TextButton(onPressed: () => context.pushReplacement(withNext('/login', widget.next)), child: Text(context.tr('Log in'))),
          ]),
        ],
      );
}

/// Step 1 of a reset: ask for the email, the API mails an 8-character code (decision D20).
class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});
  @override
  ConsumerState<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> with _Submitting {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  Future<void> _send() => submit(_form, () async {
        final email = _email.text.trim();
        await ref.read(authRepositoryProvider).forgotPassword(email);
        if (mounted) context.pushReplacement(Uri(path: '/reset', queryParameters: {'email': email}).toString());
      });

  @override
  Widget build(BuildContext context) => AuthScaffold(
        title: context.tr('Reset password'),
        subtitle: context.tr('Enter your email and we will send you a reset code.'),
        children: [
          Form(key: _form, child: AuthField(context.tr('Email'), Icons.mail_outline, controller: _email, enabled: !busy, keyboard: TextInputType.emailAddress, autofillHints: const [AutofillHints.email], validator: (v) => emailField(context, v))),
          if (error != null) ErrorBanner(error!),
          PillButton(context.tr(busy ? 'Please wait…' : 'Send reset code'), arrow: !busy, onPressed: busy ? null : _send),
        ],
      );
}

/// Step 2 of a reset: the emailed code and a new password.
class ResetPasswordScreen extends ConsumerStatefulWidget {
  const ResetPasswordScreen(this.email, {super.key});
  final String email;
  @override
  ConsumerState<ResetPasswordScreen> createState() => _ResetPasswordScreenState();
}

class _ResetPasswordScreenState extends ConsumerState<ResetPasswordScreen> with _Submitting {
  final _form = GlobalKey<FormState>();
  final _code = TextEditingController();
  final _password = TextEditingController();
  bool _hide = true;

  @override
  void dispose() {
    _code.dispose();
    _password.dispose();
    super.dispose();
  }

  static String _clean(String v) => v.toUpperCase().replaceAll(RegExp(r'[^A-Z0-9]'), '');

  Future<void> _reset() => submit(_form, () async {
        await ref.read(authRepositoryProvider).resetPassword(email: widget.email, code: _clean(_code.text), newPassword: _password.text);
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Password changed. Log in with your new password.'))));
        context.go('/login');
      });

  @override
  Widget build(BuildContext context) => AuthScaffold(
        title: context.tr('Enter your code'),
        subtitle: context.tr('We sent an 8-character code to {email}. It works for 30 minutes.', {'email': widget.email}),
        children: [
          Form(
            key: _form,
            child: Column(children: [
              AuthField(
                context.tr('Reset code'),
                Icons.pin_outlined,
                controller: _code,
                enabled: !busy,
                capitalization: TextCapitalization.characters,
                formatters: [FilteringTextInputFormatter.allow(RegExp(r'[A-Za-z0-9 -]')), LengthLimitingTextInputFormatter(9)],
                validator: (v) => _clean(v ?? '').length == 8 ? null : context.tr('Enter the 8-character code'),
              ),
              AuthField(
                context.tr('New password'),
                Icons.lock_outline,
                controller: _password,
                enabled: !busy,
                obscure: _hide,
                autofillHints: const [AutofillHints.newPassword],
                validator: (v) => passwordField(context, v),
                suffix: passwordToggle(context, _hide, () => setState(() => _hide = !_hide)),
              ),
            ]),
          ),
          if (error != null) ErrorBanner(error!),
          PillButton(context.tr(busy ? 'Please wait…' : 'Change password'), arrow: !busy, onPressed: busy ? null : _reset),
          Center(child: TextButton(onPressed: busy ? null : () => context.pushReplacement('/forgot'), child: Text(context.tr('Send a new code')))),
        ],
      );
}
