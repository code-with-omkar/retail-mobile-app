import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/theme/app_palette.dart';
import 'package:quickcart_customer/core/theme/app_theme.dart';

double contrast(Color a, Color b) {
  final double la = a.computeLuminance();
  final double lb = b.computeLuminance();
  return (math.max(la, lb) + 0.05) / (math.min(la, lb) + 0.05);
}

void main() {
  group('AppPalette contrast (WCAG AA)', () {
    final Map<String, (Color fg, Color bg, double min)> pairs = <String, (Color, Color, double)>{
      'ink on lime': (AppPalette.ink, AppPalette.lime, 4.5),
      'ink on fresh green': (AppPalette.ink, AppPalette.freshGreen, 4.5),
      'ink on hot pink': (AppPalette.ink, AppPalette.hotPink, 4.5),
      'white on magenta': (AppPalette.card, AppPalette.magenta, 4.5),
      'magentaText on card': (AppPalette.magentaText, AppPalette.card, 4.5),
      'magentaText on surface': (AppPalette.magentaText, AppPalette.surface, 4.5),
      'greenText on card': (AppPalette.greenText, AppPalette.card, 4.5),
      'inkMuted on surface': (AppPalette.inkMuted, AppPalette.surface, 4.5),
      'nightInk on night': (AppPalette.nightInk, AppPalette.night, 4.5),
      'nightMuted on nightCard': (AppPalette.nightMuted, AppPalette.nightCard, 4.5),
      'hotPink on night': (AppPalette.hotPink, AppPalette.night, 4.5),
      'lime on ink': (AppPalette.lime, AppPalette.ink, 4.5),
    };

    pairs.forEach((String name, (Color, Color, double) p) {
      test(name, () {
        expect(contrast(p.$1, p.$2), greaterThanOrEqualTo(p.$3));
      });
    });

    test('neon lime is never safe as text on light ground', () {
      expect(contrast(AppPalette.lime, AppPalette.card), lessThan(3));
    });
  });

  group('AppTheme', () {
    test('light theme exposes AppColors and lime primary', () {
      final ThemeData theme = AppTheme.light(useGoogleFonts: false);
      expect(theme.extension<AppColors>(), AppColors.light);
      expect(theme.colorScheme.primary, AppPalette.lime);
      expect(theme.colorScheme.onPrimary, AppPalette.ink);
      expect(theme.scaffoldBackgroundColor, AppPalette.surface);
      expect(theme.brightness, Brightness.light);
    });

    test('dark theme uses night ground and readable brand text', () {
      final ThemeData theme = AppTheme.dark(useGoogleFonts: false);
      final AppColors colors = theme.extension<AppColors>()!;
      expect(theme.scaffoldBackgroundColor, AppPalette.night);
      expect(colors.brandText, AppPalette.hotPink);
      expect(contrast(colors.brandText, colors.surface), greaterThanOrEqualTo(4.5));
    });

    test('text theme uses brand families without runtime fetching', () {
      final TextTheme text = AppTheme.light(useGoogleFonts: false).textTheme;
      expect(text.headlineMedium?.fontFamily, AppFonts.display);
      expect(text.bodyMedium?.fontFamily, AppFonts.body);
      expect(text.bodyMedium?.fontFamilyFallback, contains(AppFonts.devanagari));
    });

    test('price style uses tabular figures', () {
      final TextStyle style = AppTypography.price(color: AppPalette.ink, useGoogleFonts: false);
      expect(style.fontFeatures, contains(const FontFeature.tabularFigures()));
    });
  });

  group('AppColors', () {
    test('lerp endpoints return each theme', () {
      expect(AppColors.light.lerp(AppColors.dark, 0).surface, AppColors.light.surface);
      expect(AppColors.light.lerp(AppColors.dark, 1).surface, AppColors.dark.surface);
      expect(AppColors.light.lerp(null, 0.5), same(AppColors.light));
    });

    test('copyWith overrides one role only', () {
      final AppColors changed = AppColors.light.copyWith(action: AppPalette.freshGreen);
      expect(changed.action, AppPalette.freshGreen);
      expect(changed.brand, AppColors.light.brand);
    });

    testWidgets('context.appColors resolves from theme', (WidgetTester tester) async {
      late AppColors resolved;
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.dark(useGoogleFonts: false),
          home: Builder(
            builder: (BuildContext context) {
              resolved = context.appColors;
              return const SizedBox.shrink();
            },
          ),
        ),
      );
      expect(resolved.surface, AppPalette.night);
    });
  });
}
