import 'package:flutter/material.dart';

import 'strings_mr.dart';

/// Lightweight localization: the English text is the key. English returns the key itself,
/// Marathi looks it up in [marathi] and falls back to English when a string is missing.
/// Placeholders use `{name}` and are filled from [args].
class AppStrings {
  const AppStrings(this.locale);
  final Locale locale;

  static const supported = [Locale('en'), Locale('mr')];
  static const delegate = _Delegate();

  static AppStrings of(BuildContext context) => Localizations.of<AppStrings>(context, AppStrings) ?? const AppStrings(Locale('en'));

  String get(String key, [Map<String, String>? args]) {
    var text = locale.languageCode == 'mr' ? (marathi[key] ?? key) : key;
    args?.forEach((k, v) => text = text.replaceAll('{$k}', v));
    return text;
  }

  bool get isMarathi => locale.languageCode == 'mr';
}

class _Delegate extends LocalizationsDelegate<AppStrings> {
  const _Delegate();
  @override
  bool isSupported(Locale locale) => const ['en', 'mr'].contains(locale.languageCode);
  @override
  Future<AppStrings> load(Locale locale) async => AppStrings(locale);
  @override
  bool shouldReload(covariant LocalizationsDelegate<AppStrings> old) => false;
}

extension TrContext on BuildContext {
  String tr(String key, [Map<String, String>? args]) => AppStrings.of(this).get(key, args);
  bool get isMarathi => AppStrings.of(this).isMarathi;
}
