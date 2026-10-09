import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';

/// Shared frame for the account screens: dark ground, big title, subtitle, then the form.
class AuthScaffold extends StatelessWidget {
  const AuthScaffold({super.key, required this.title, required this.subtitle, required this.children});
  final String title, subtitle;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => AppScaffold(
        appBar: appTopBar(context, ''),
        body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 0, QC.gutter, 24), keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag, children: [
          Text(title, style: const TextStyle(color: Colors.white, fontSize: 34, fontWeight: FontWeight.w900, height: 1.1)),
          const SizedBox(height: 8),
          Text(subtitle, style: const TextStyle(color: Pal.mutedOnGround, fontSize: 16)),
          const SizedBox(height: 24),
          ...children,
        ]),
      );
}

class AuthField extends StatelessWidget {
  const AuthField(this.label, this.icon, {super.key, this.controller, this.obscure = false, this.keyboard, this.validator, this.suffix, this.autofillHints, this.textInputAction, this.enabled = true, this.formatters, this.capitalization = TextCapitalization.none, this.maxLines = 1, this.onChanged});
  final String label;
  final IconData icon;
  final TextEditingController? controller;
  final bool obscure, enabled;
  final TextInputType? keyboard;
  final String? Function(String?)? validator;
  final Widget? suffix;
  final Iterable<String>? autofillHints;
  final TextInputAction? textInputAction;
  final List<TextInputFormatter>? formatters;
  final TextCapitalization capitalization;
  final int maxLines;
  final ValueChanged<String>? onChanged;

  @override
  Widget build(BuildContext context) => LabeledField(
        label: label,
        child: TextFormField(
          controller: controller,
          obscureText: obscure,
          maxLines: obscure ? 1 : maxLines,
          onChanged: onChanged,
          enabled: enabled,
          keyboardType: keyboard,
          validator: validator,
          autofillHints: autofillHints,
          textInputAction: textInputAction,
          inputFormatters: formatters,
          textCapitalization: capitalization,
          style: TextStyle(color: context.pal.onCard, fontSize: 16),
          autovalidateMode: AutovalidateMode.onUserInteraction,
          decoration: InputDecoration(prefixIcon: Icon(icon), suffixIcon: suffix),
        ),
      );
}

/// Show/hide button for password fields.
Widget passwordToggle(BuildContext context, bool hidden, VoidCallback onToggle) => IconButton(
      tooltip: context.tr(hidden ? 'Show password' : 'Hide password'),
      icon: Icon(hidden ? Icons.visibility_outlined : Icons.visibility_off_outlined),
      onPressed: onToggle,
    );

/// A failure from the last submit, shown above the button.
class ErrorBanner extends StatelessWidget {
  const ErrorBanner(this.message, {super.key});
  final String message;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: Semantics(
          liveRegion: true,
          child: Surface(
            color: Pal.pink,
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Icon(Icons.error_outline, color: Colors.white),
              const SizedBox(width: 12),
              Expanded(child: Text(message, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800))),
            ]),
          ),
        ),
      );
}

String? requiredField(BuildContext c, String? v) => (v == null || v.trim().isEmpty) ? c.tr('Required') : null;
String? emailField(BuildContext c, String? v) => (v == null || !RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(v.trim())) ? c.tr('Enter a valid email') : null;
String? passwordField(BuildContext c, String? v) => (v == null || v.length < 8) ? c.tr('At least 8 characters') : null;

/// Optional phone: empty is fine, otherwise 10 digits (an optional +91 is accepted).
String? optionalPhoneField(BuildContext c, String? v) {
  if (v == null || v.trim().isEmpty) return null;
  final digits = v.replaceAll(RegExp(r'\D'), '');
  return (digits.length == 10 || (digits.length == 12 && digits.startsWith('91'))) ? null : c.tr('Enter a 10-digit number');
}

/// What to show for a failed account call. Messages the API wrote for customers (validation rules such as a too common
/// password) are shown as they are; everything else uses the translated keys so server internals never reach the screen.
String accountErrorText(BuildContext context, Object error, {bool signingIn = false}) {
  switch (error) {
    case ConflictException():
      return context.tr('An account with this email already exists.');
    case UnauthorizedException() when signingIn:
      return context.tr('Wrong email or password.');
    case ForbiddenException() when signingIn:
      return context.tr('This account cannot be used in the customer app.');
    case ValidationException(:final errors):
      if (errors.any((e) => e.toLowerCase().contains('invalid or expired code'))) return context.tr('That code is not valid or has expired. Check it or ask for a new one.');
      if (errors.any((e) => e.toLowerCase().contains('current password'))) return context.tr('Your current password is not correct.');
      return errors.isEmpty ? context.tr(error.userMessageKey) : errors.take(3).join('\n');
    case ApiException():
      return context.tr(error.userMessageKey);
    default:
      return context.tr('Something went wrong. Please try again.');
  }
}

/// Only in-app paths are accepted as a place to go after signing in.
String safeNext(String? next) => (next != null && next.startsWith('/') && !next.startsWith('//')) ? next : '/';

String withNext(String path, String? next) => next == null ? path : '$path?next=${Uri.encodeComponent(next)}';
