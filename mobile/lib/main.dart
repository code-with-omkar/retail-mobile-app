import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app.dart';
import 'core/crash_reporting.dart';
import 'core/prefs.dart';
import 'core/riverpod_config.dart';

Future<void> main() => runWithCrashReporting(_start);

Future<void> _start() async {
  WidgetsFlutterBinding.ensureInitialized();
  final prefs = await SharedPreferences.getInstance();
  runApp(ProviderScope(retry: noAutomaticRetry, overrides: [sharedPrefsProvider.overrideWithValue(prefs)], child: const QuickCartApp()));
}
