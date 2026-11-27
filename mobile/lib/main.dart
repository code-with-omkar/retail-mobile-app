import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app.dart';
import 'core/crash_reporting.dart';
import 'core/prefs.dart';
import 'core/riverpod_config.dart';

Future<void> main() => runWithCrashReporting(_start);

Future<void> _start() async {
  WidgetsFlutterBinding.ensureInitialized();
  // Draw behind the status and navigation bars (QC.overlay makes them transparent), so there is no band of another colour at the top or bottom.
  await SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
  final prefs = await SharedPreferences.getInstance();
  runApp(
    ProviderScope(
      retry: noAutomaticRetry,
      overrides: [sharedPrefsProvider.overrideWithValue(prefs)],
      child: const QuickCartApp(),
    ),
  );
}
