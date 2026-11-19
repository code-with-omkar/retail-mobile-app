import 'dart:async';

import 'package:sentry_flutter/sentry_flutter.dart';

/// Crash reporting with Sentry. It is off unless the app is built with `--dart-define=SENTRY_DSN=...`, and it never sends who the person
/// is: no user, no screenshots, and no trail of taps, typing or requests (they can carry phone numbers and addresses).
const crashDsn = String.fromEnvironment('SENTRY_DSN');

bool crashReportingOn([String dsn = crashDsn]) => dsn.trim().isNotEmpty;

/// Takes everything that could identify a person out of an event before it leaves the phone. The error and its stack stay.
SentryEvent? scrubCrashEvent(SentryEvent event, Hint hint) {
  event
    ..user = null
    ..request = null
    ..breadcrumbs = const []
    ..serverName = null;
  return event;
}

/// Runs [app]. With a key it runs inside Sentry (which also catches errors outside the Flutter framework); without one it just runs.
Future<void> runWithCrashReporting(FutureOr<void> Function() app, {String dsn = crashDsn}) async {
  if (!crashReportingOn(dsn)) {
    await app();
    return;
  }
  await SentryFlutter.init((options) {
    options.dsn = dsn.trim();
    options.sendDefaultPii = false;
    options.attachScreenshot = false;
    options.attachViewHierarchy = false;
    options.tracesSampleRate = 0;
    options.beforeSend = scrubCrashEvent;
    options.beforeBreadcrumb = (breadcrumb, hint) => null;
  }, appRunner: app);
}
