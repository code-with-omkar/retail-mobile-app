import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_widgets.dart';

class EditProfileScreen extends ConsumerStatefulWidget {
  const EditProfileScreen({super.key});
  @override
  ConsumerState<EditProfileScreen> createState() => _EditProfileScreenState();
}

class _EditProfileScreenState extends ConsumerState<EditProfileScreen> {
  final _form = GlobalKey<FormState>();
  late final _name = TextEditingController(text: ref.read(authProvider).user?.fullName);
  late final _phone = TextEditingController(text: ref.read(authProvider).user?.phoneNumber);
  late final _email = TextEditingController(text: ref.read(authProvider).user?.email);
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _phone.dispose();
    _email.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final updated = await ref.read(authRepositoryProvider).updateProfile(fullName: _name.text, phoneNumber: _phone.text.trim().isEmpty ? null : _phone.text);
      ref.read(authProvider.notifier).profileChanged(updated);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Profile saved'))));
      context.pop();
    } catch (e) {
      if (mounted) setState(() => _error = accountErrorText(context, e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AuthScaffold(
      title: context.tr('Edit profile'),
      subtitle: context.tr('Your email cannot be changed here.'),
      children: [
        Form(
          key: _form,
          child: Column(children: [
            AuthField(context.tr('Full name'), Icons.person_outline, controller: _name, enabled: !_busy, capitalization: TextCapitalization.words, validator: (v) => requiredField(context, v)),
            AuthField(context.tr('Mobile number (optional)'), Icons.phone_outlined, controller: _phone, enabled: !_busy, keyboard: TextInputType.phone, formatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9+ ]'))], validator: (v) => optionalPhoneField(context, v)),
            AuthField(context.tr('Email'), Icons.mail_outline, controller: _email, enabled: false),
          ]),
        ),
        if (_error != null) ErrorBanner(_error!),
        PillButton(context.tr(_busy ? 'Please wait…' : 'Save'), arrow: !_busy, onPressed: _busy ? null : _save),
      ],
    );
  }
}

class ChangePasswordScreen extends ConsumerStatefulWidget {
  const ChangePasswordScreen({super.key});
  @override
  ConsumerState<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends ConsumerState<ChangePasswordScreen> {
  final _form = GlobalKey<FormState>();
  final _current = TextEditingController();
  final _next = TextEditingController();
  bool _hide = true;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _current.dispose();
    _next.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(authRepositoryProvider).changePassword(currentPassword: _current.text, newPassword: _next.text);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Password changed. Your other devices were signed out.'))));
      context.pop();
    } catch (e) {
      if (mounted) setState(() => _error = accountErrorText(context, e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AuthScaffold(
        title: context.tr('Change password'),
        subtitle: context.tr('Other devices will be signed out.'),
        children: [
          Form(
            key: _form,
            child: Column(children: [
              AuthField(context.tr('Current password'), Icons.lock_outline, controller: _current, enabled: !_busy, obscure: _hide, autofillHints: const [AutofillHints.password], validator: (v) => requiredField(context, v)),
              AuthField(
                context.tr('New password'),
                Icons.lock_reset_outlined,
                controller: _next,
                enabled: !_busy,
                obscure: _hide,
                autofillHints: const [AutofillHints.newPassword],
                validator: (v) => passwordField(context, v),
                suffix: passwordToggle(context, _hide, () => setState(() => _hide = !_hide)),
              ),
            ]),
          ),
          if (_error != null) ErrorBanner(_error!),
          PillButton(context.tr(_busy ? 'Please wait…' : 'Save'), arrow: !_busy, onPressed: _busy ? null : _save),
        ],
      );
}

/// Deleting the account for good. Google Play requires that customers can do this from inside the app.
class DeleteAccountScreen extends ConsumerStatefulWidget {
  const DeleteAccountScreen({super.key});
  @override
  ConsumerState<DeleteAccountScreen> createState() => _DeleteAccountScreenState();
}

class _DeleteAccountScreenState extends ConsumerState<DeleteAccountScreen> {
  final _form = GlobalKey<FormState>();
  final _password = TextEditingController();
  final _confirm = TextEditingController();
  bool _hide = true;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _password.dispose();
    _confirm.dispose();
    super.dispose();
  }

  Future<void> _delete() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(authProvider.notifier).deleteAccount(password: _password.text);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Your account was deleted.'))));
      context.go('/');
    } catch (e) {
      if (mounted) {
        setState(() => _error = switch (e) {
              ConflictException() => context.tr('Finish or cancel your open orders first.'),
              ValidationException() => context.tr('Your password is not correct.'),
              _ => accountErrorText(context, e),
            });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final hasPassword = ref.watch(authProvider).user?.hasPassword ?? true;
    return AuthScaffold(
      title: context.tr('Delete account'),
      subtitle: context.tr('This erases your name, phone, email, addresses and notifications for good. Past orders stay with the store without your details.'),
      children: [
        Form(
          key: _form,
          child: Column(children: [
            if (hasPassword)
              AuthField(
                context.tr('Password'),
                Icons.lock_outline,
                controller: _password,
                enabled: !_busy,
                obscure: _hide,
                autofillHints: const [AutofillHints.password],
                validator: (v) => requiredField(context, v),
                suffix: passwordToggle(context, _hide, () => setState(() => _hide = !_hide)),
              ),
            AuthField(
              context.tr('Type DELETE to confirm'),
              Icons.delete_outline,
              controller: _confirm,
              enabled: !_busy,
              capitalization: TextCapitalization.characters,
              validator: (v) => (v ?? '').trim().toUpperCase() == 'DELETE' ? null : context.tr('Type DELETE to confirm'),
            ),
          ]),
        ),
        if (_error != null) ErrorBanner(_error!),
        PillButton(context.tr(_busy ? 'Please wait…' : 'Delete my account'), arrow: false, onPressed: _busy ? null : _delete),
      ],
    );
  }
}
