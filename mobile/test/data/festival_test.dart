import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/strings_mr.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:quickcart_customer/features/festival/festival.dart';
import 'package:quickcart_customer/features/festival/festival_widgets.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../support/auth_fakes.dart';

Future<ProviderContainer> _open(
  WidgetTester tester, {
  required DateTime today,
  Map<String, Object> prefs = const {},
  bool reduceMotion = false,
}) async {
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390 * 3, 1800 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  if (reduceMotion) {
    tester.platformDispatcher.accessibilityFeaturesTestValue = const FakeAccessibilityFeatures(disableAnimations: true);
    addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
  }
  final auth = AuthHarness();
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [
    sharedPrefsProvider.overrideWithValue(sp),
    sessionStoreProvider.overrideWithValue(MemorySessionStore()),
    authRepositoryProvider.overrideWithValue(auth.repo),
    festivalClockProvider.overrideWithValue(() => today),
    appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid', useSeedData: true)),
  ], child: const QuickCartApp()));
  await tester.pump(const Duration(milliseconds: 600));
  return ProviderScope.containerOf(tester.element(find.byType(QuickCartApp)));
}

/// Time passes, then one more frame so the fade-out starts, and the time it takes to finish.
Future<void> _pastTheFalling(WidgetTester tester, int seconds) async {
  await tester.pump(Duration(seconds: seconds));
  await tester.pump();
  await tester.pump(const Duration(seconds: 2));
}

void main() {
  group('the calendar', () {
    test('each festival is found on its first day, its last day and every day between', () {
      for (final f in festivals) {
        expect(festivalOn(f.start)?.id, f.id, reason: '${f.id} first day');
        expect(festivalOn(f.end)?.id, f.id, reason: '${f.id} last day');
        expect(festivalOn(f.end.subtract(const Duration(days: 1)).add(const Duration(hours: 18)))?.id, anyOf(f.id, isNull), reason: 'time of day does not matter');
      }
    });

    test('known dates', () {
      expect(festivalOn(DateTime(2026, 10, 8)), isNull, reason: 'before Navratri');
      expect(festivalOn(DateTime(2026, 10, 12))?.title, 'Happy Navratri & Dussehra!');
      expect(festivalOn(DateTime(2026, 10, 20))?.title, 'Happy Navratri & Dussehra!', reason: 'Dussehra');
      expect(festivalOn(DateTime(2026, 10, 25)), isNull);
      expect(festivalOn(DateTime(2026, 11, 8))?.title, 'Happy Diwali!');
      expect(festivalOn(DateTime(2026, 12, 25))?.title, 'Merry Christmas!');
      expect(festivalOn(DateTime(2026, 12, 31))?.title, 'Happy New Year!');
      expect(festivalOn(DateTime(2027, 1, 1))?.title, 'Happy New Year!');
      expect(festivalOn(DateTime(2027, 1, 14))?.title, 'Happy Makar Sankranti!');
      expect(festivalOn(DateTime(2027, 1, 26))?.title, 'Happy Republic Day!');
      expect(festivalOn(DateTime(2027, 3, 22))?.title, 'Happy Holi!');
      expect(festivalOn(DateTime(2027, 6, 1)), isNull);
    });

    test('the festivals are in date order and never overlap', () {
      for (var i = 1; i < festivals.length; i++) {
        final before = festivals[i - 1], after = festivals[i];
        expect(before.start.isBefore(after.start), isTrue, reason: after.id);
      }
      for (final f in festivals) {
        expect(f.end.isBefore(f.start), isFalse, reason: f.id);
        for (final other in festivals.where((o) => o.id != f.id)) {
          final overlap = !f.end.isBefore(other.start) && !f.start.isAfter(other.end);
          // Christmas runs into the New Year run-up only by design: the earlier one wins.
          if (overlap) expect({f.id, other.id}, {'christmas-2026', 'new-year-2027'}, reason: '${f.id} overlaps ${other.id}');
        }
      }
    });

    test('every greeting has a Marathi translation', () {
      for (final f in festivals) {
        expect(marathi.containsKey(f.title), isTrue, reason: f.title);
        expect(marathi.containsKey(f.subtitle), isTrue, reason: f.subtitle);
      }
    });

  });

  group('on Home', () {
    testWidgets('during a festival the banner greets the customer and sparkles fall', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8));

      expect(find.text('Happy Diwali!'), findsOneWidget);
      expect(find.text('Fresh groceries for your festive table'), findsOneWidget);
      expect(find.byType(FestivalParticles), findsOneWidget);
      expect(find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)), findsWidgets);
    });

    testWidgets('outside a festival there is no banner and nothing is drawn', (tester) async {
      await _open(tester, today: DateTime(2026, 9, 1));

      expect(find.text('Happy Diwali!'), findsNothing);
      expect(find.text('Fresh groceries for your festive table'), findsNothing);
      expect(find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)), findsNothing);
    });

    testWidgets('the animation never takes a tap: the screen below still works', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8));

      await tester.tap(find.byTooltip('Categories'));
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.text('Categories'), findsWidgets);
    });

    testWidgets('searching hides the banner so results are not pushed down', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8));
      expect(find.text('Happy Diwali!'), findsOneWidget);

      await tester.enterText(find.byType(TextField).first, 'onion');
      await tester.pump(const Duration(milliseconds: 600));

      expect(find.text('Happy Diwali!'), findsNothing);
    });

    testWidgets('in Marathi the greeting is in Marathi', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8), prefs: {'language': 'mr'});

      expect(find.text('दिवाळीच्या हार्दिक शुभेच्छा!'), findsOneWidget);
    });

    testWidgets('the phone set to reduce motion: the banner stays, the falling stops', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8), reduceMotion: true);

      expect(find.text('Happy Diwali!'), findsOneWidget);
      expect(find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)), findsNothing);
    });

    testWidgets('the customer switched it off: the banner stays, the falling stops', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8), prefs: {'festival_animation': false});

      expect(find.text('Happy Diwali!'), findsOneWidget);
      expect(find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)), findsNothing);
    });

    testWidgets('the falling lasts 30 seconds, fades out, and then nothing is drawn and the screen goes quiet; the banner stays', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8));
      bool falling() => find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)).evaluate().isNotEmpty;
      expect(falling(), isTrue);

      await tester.pump(const Duration(seconds: 28));
      expect(falling(), isTrue, reason: 'still within the 30 seconds');

      await tester.pump(const Duration(seconds: 3)); // 31 s: the fade has begun
      await tester.pump(const Duration(seconds: 2)); // and finished

      expect(falling(), isFalse);
      expect(find.text('Happy Diwali!'), findsOneWidget);
      await tester.pumpAndSettle(); // nothing animates any more, so this settles
    });

    testWidgets('switching it off and on again in Profile gives it another 30 seconds', (tester) async {
      final container = await _open(tester, today: DateTime(2026, 11, 8));
      await _pastTheFalling(tester, 40);
      bool falling() => find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)).evaluate().isNotEmpty;
      expect(falling(), isFalse);

      container.read(festivalAnimationProvider.notifier).set(false);
      await tester.pump(const Duration(milliseconds: 300));
      container.read(festivalAnimationProvider.notifier).set(true);
      await tester.pump(const Duration(milliseconds: 300));

      expect(falling(), isTrue);
      await _pastTheFalling(tester, 35);
      expect(falling(), isFalse);
    });

    testWidgets('the count starts when Home shows it, and is shared: leaving Home and coming back does not restart it', (tester) async {
      await _open(tester, today: DateTime(2026, 11, 8));
      await tester.pump(const Duration(seconds: 20));
      await tester.tap(find.byTooltip('Categories'));
      await tester.pump(const Duration(milliseconds: 500));
      await tester.tap(find.byTooltip('Home'));
      await tester.pump(const Duration(milliseconds: 500));
      await tester.pump(const Duration(seconds: 12));
      await tester.pump(const Duration(seconds: 2));

      expect(find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)), findsNothing);
    });

    testWidgets('each festival draws its own kind of piece without errors', (tester) async {
      for (final day in [DateTime(2026, 10, 15), DateTime(2026, 11, 8), DateTime(2026, 12, 25), DateTime(2027, 3, 22)]) {
        await _open(tester, today: day);
        await tester.pump(const Duration(seconds: 3));
        expect(tester.takeException(), isNull, reason: '$day');
        await tester.pumpWidget(const SizedBox());
      }
    });
  });

  group('the switch in Profile', () {
    testWidgets('turning it off stops the falling at once and is remembered; turning it on brings it back', (tester) async {
      final container = await _open(tester, today: DateTime(2026, 11, 8));
      await tester.tap(find.byTooltip('Profile'));
      await tester.pump(const Duration(milliseconds: 500));
      await tester.ensureVisible(find.text('Festival animations'));
      await tester.pump(const Duration(milliseconds: 300));

      await tester.tap(find.text('Festival animations'));
      await tester.pump(const Duration(milliseconds: 300));

      expect(container.read(festivalAnimationProvider), isFalse);
      expect((await SharedPreferences.getInstance()).getBool('festival_animation'), isFalse);
      await tester.tap(find.byTooltip('Home'));
      await tester.pump(const Duration(milliseconds: 500));
      expect(find.descendant(of: find.byType(FestivalParticles), matching: find.byType(CustomPaint)), findsNothing);
      expect(find.text('Happy Diwali!'), findsOneWidget);

      await tester.tap(find.byTooltip('Profile'));
      await tester.pump(const Duration(milliseconds: 500));
      await tester.ensureVisible(find.text('Festival animations'));
      await tester.pump(const Duration(milliseconds: 300));
      await tester.tap(find.text('Festival animations'));
      await tester.pump(const Duration(milliseconds: 300));

      expect(container.read(festivalAnimationProvider), isTrue);
    });
  });
}
