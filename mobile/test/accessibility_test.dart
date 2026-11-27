import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/app.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/prefs.dart';
import 'package:quickcart_customer/core/riverpod_config.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/data/providers.dart';
import 'package:quickcart_customer/data/session_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/auth_fakes.dart';

/// Flutter's own accessibility checks (tap target size, labels on tappable things) on the main screens, in the light and
/// dark theme. A new screen or control that breaks them fails here. Text contrast is checked on the palette itself further
/// down, because the test font draws solid blocks and makes the screen-based contrast check unreliable.
Future<void> _pump(WidgetTester tester, {Map<String, Object> prefs = const {}}) async {
  final auth = AuthHarness();
  SharedPreferences.setMockInitialValues(prefs);
  final sp = await SharedPreferences.getInstance();
  tester.view.physicalSize = const Size(390, 844) * 3;
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(ProviderScope(retry: noAutomaticRetry, overrides: [sharedPrefsProvider.overrideWithValue(sp), sessionStoreProvider.overrideWithValue(MemorySessionStore()), authRepositoryProvider.overrideWithValue(auth.repo), appConfigProvider.overrideWithValue(const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://test.invalid', useSeedData: true))], child: const QuickCartApp()));
  await tester.pumpAndSettle();
}

Future<void> _tab(WidgetTester tester, String tooltip) async {
  await tester.tap(find.byTooltip(tooltip));
  await tester.pumpAndSettle();
}

Future<void> _checkAll(WidgetTester tester) async {
  await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
  await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
}

double _ratio(Color a, Color b) {
  final la = a.computeLuminance(), lb = b.computeLuminance();
  final hi = la > lb ? la : lb, lo = la > lb ? lb : la;
  return (hi + .05) / (lo + .05);
}

void main() {
  group('palette contrast (WCAG AA, 4.5 for text)', () {
    for (final pal in [Pal.light, Pal.darkPal]) {
      final name = pal.dark ? 'dark' : 'light';

      test('text on the screen background, $name', () {
        // The background is a gradient: every stop must work with each text colour used on it.
        for (final stop in pal.gradient) {
          expect(_ratio(pal.onGround, stop), greaterThanOrEqualTo(4.5), reason: 'white on $stop');
          expect(_ratio(pal.mutedOnGround, stop), greaterThanOrEqualTo(4.5), reason: 'muted on $stop');
          expect(_ratio(pal.dark ? Pal.yellow : Pal.ink, stop), greaterThanOrEqualTo(4.5), reason: 'links on $stop'); // light text buttons are ink (yellow is for button fills)
        }
      });

      test('text on cards, soft panels, chips, steppers and tiles, $name', () {
        expect(_ratio(pal.onCard, pal.card), greaterThanOrEqualTo(4.5), reason: 'on card');
        expect(_ratio(pal.mutedOnCard, pal.card), greaterThanOrEqualTo(4.5), reason: 'muted on card');
        expect(_ratio(pal.onSoft, pal.soft), greaterThanOrEqualTo(4.5), reason: 'on soft');
        expect(_ratio(pal.onChip, pal.chip), greaterThanOrEqualTo(4.5), reason: 'on chip');
        expect(_ratio(pal.onStepper, pal.stepper), greaterThanOrEqualTo(4.5), reason: 'on stepper');
        for (final tile in pal.tiles) {
          expect(_ratio(Pal.ink, tile), greaterThanOrEqualTo(4.5), reason: 'dark text on $tile');
        }
      });
    }
  });

  for (final dark in [false, true]) {
    final theme = dark ? 'dark' : 'light';
    final prefs = <String, Object>{if (dark) 'theme_mode': 'dark'};

    group('accessibility ($theme)', () {
      testWidgets('home', (tester) async {
        final handle = tester.ensureSemantics();
        await _pump(tester, prefs: prefs);
        await _checkAll(tester);
        handle.dispose();
      });

      testWidgets('profile', (tester) async {
        final handle = tester.ensureSemantics();
        await _pump(tester, prefs: prefs);
        await _tab(tester, 'Profile');
        await _checkAll(tester);
        handle.dispose();
      });

      testWidgets('cart', (tester) async {
        final handle = tester.ensureSemantics();
        await _pump(tester, prefs: prefs);
        await _tab(tester, 'Cart');
        await _checkAll(tester);
        handle.dispose();
      });
    });
  }
}
