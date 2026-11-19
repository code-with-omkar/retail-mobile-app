import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_delivery/crash_reporting.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

void main() {
  test('crash reporting is off until a key is given', () {
    expect(crashReportingOn(''), isFalse);
    expect(crashReportingOn('   '), isFalse);
    expect(crashReportingOn('https://key@example.test/1'), isTrue);
    expect(crashReportingOn(), isFalse); // no --dart-define in tests
  });

  test('without a key the app simply runs, and nothing else starts', () async {
    var ran = false;
    await runWithCrashReporting(() => ran = true, dsn: '');
    expect(ran, isTrue);
  });

  test('an event loses the person, the request and the trail of taps, and keeps the error', () {
    final event = SentryEvent(
      throwable: StateError('boom'),
      user: SentryUser(id: 'u1', email: 'a@b.test', ipAddress: '1.2.3.4'),
      request: SentryRequest(url: 'https://x/orders/1', data: {'phone': '9876543210'}),
      breadcrumbs: [Breadcrumb(message: 'tapped Pay', data: {'phone': '9876543210'})],
      serverName: 'my-phone',
    );

    final scrubbed = scrubCrashEvent(event, Hint())!;

    expect(scrubbed.user, isNull);
    expect(scrubbed.request, isNull);
    expect(scrubbed.breadcrumbs, isEmpty);
    expect(scrubbed.serverName, isNull);
    expect(scrubbed.throwable, isA<StateError>());
  });
}
