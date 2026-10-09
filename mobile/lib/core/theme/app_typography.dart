import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Font families used by the app.
abstract final class AppFonts {
  static const String display = 'Bricolage Grotesque';
  static const String body = 'Plus Jakarta Sans';
  static const String devanagari = 'Mukta';
}

/// Builds the app [TextTheme].
///
/// Pass `useGoogleFonts: false` in widget/golden tests so no font is fetched
/// at runtime; styles then carry the plain family names instead.
abstract final class AppTypography {
  static const List<FontFeature> _tabular = <FontFeature>[FontFeature.tabularFigures()];

  static TextStyle _font(String family, TextStyle base, {required bool useGoogleFonts}) {
    final TextStyle withFallback = base.copyWith(
      fontFamilyFallback: const <String>[AppFonts.devanagari],
    );
    if (!useGoogleFonts) {
      return withFallback.copyWith(fontFamily: family);
    }
    return GoogleFonts.getFont(family, textStyle: withFallback);
  }

  static TextTheme textTheme({
    required Color ink,
    required Color inkMuted,
    bool useGoogleFonts = true,
  }) {
    TextStyle display(TextStyle s) => _font(AppFonts.display, s, useGoogleFonts: useGoogleFonts);
    TextStyle body(TextStyle s) => _font(AppFonts.body, s, useGoogleFonts: useGoogleFonts);

    return TextTheme(
      // Display: hero moments ("Order placed", ETA).
      displayLarge: display(TextStyle(fontSize: 56, height: 54 / 56, fontWeight: FontWeight.w800, letterSpacing: -2.24, color: ink)),
      displayMedium: display(TextStyle(fontSize: 40, height: 44 / 40, fontWeight: FontWeight.w800, letterSpacing: -1.2, color: ink)),
      displaySmall: display(TextStyle(fontSize: 34, height: 36 / 34, fontWeight: FontWeight.w800, letterSpacing: -1.02, color: ink)),
      // H1: screen titles.
      headlineMedium: display(TextStyle(fontSize: 28, height: 32 / 28, fontWeight: FontWeight.w800, letterSpacing: -0.84, color: ink)),
      // H2: section titles.
      titleLarge: display(TextStyle(fontSize: 20, height: 26 / 20, fontWeight: FontWeight.w700, color: ink)),
      // Card/row titles.
      titleMedium: body(TextStyle(fontSize: 16, height: 22 / 16, fontWeight: FontWeight.w700, color: ink)),
      titleSmall: body(TextStyle(fontSize: 14, height: 20 / 14, fontWeight: FontWeight.w800, color: ink)),
      bodyLarge: body(TextStyle(fontSize: 16, height: 24 / 16, fontWeight: FontWeight.w500, color: ink)),
      bodyMedium: body(TextStyle(fontSize: 14, height: 20 / 14, fontWeight: FontWeight.w500, color: ink)),
      bodySmall: body(TextStyle(fontSize: 12, height: 16 / 12, fontWeight: FontWeight.w500, color: inkMuted)),
      // Buttons.
      labelLarge: body(TextStyle(fontSize: 16, height: 20 / 16, fontWeight: FontWeight.w800, color: ink)),
      labelMedium: body(TextStyle(fontSize: 13, height: 16 / 13, fontWeight: FontWeight.w700, color: ink)),
      // Badges, nav labels.
      labelSmall: body(TextStyle(fontSize: 11, height: 14 / 11, fontWeight: FontWeight.w700, color: ink)),
    );
  }

  /// Price figure: display face, tabular numerals. Use for every ₹ amount.
  static TextStyle price({
    required Color color,
    double size = 18,
    bool useGoogleFonts = true,
  }) {
    return _font(
      AppFonts.display,
      TextStyle(
        fontSize: size,
        height: 1.1,
        fontWeight: FontWeight.w800,
        letterSpacing: size >= 32 ? -0.02 * size : 0.0,
        color: color,
        fontFeatures: _tabular,
      ),
      useGoogleFonts: useGoogleFonts,
    );
  }

  /// Marathi/Hindi copy: Mukta, one step larger and looser than Latin body.
  static TextStyle devanagari(TextStyle base, {bool useGoogleFonts = true}) {
    final TextStyle sized = base.copyWith(
      fontSize: (base.fontSize ?? 14) + 2,
      height: 1.45,
      letterSpacing: 0,
    );
    if (!useGoogleFonts) {
      return sized.copyWith(fontFamily: AppFonts.devanagari);
    }
    return GoogleFonts.getFont(AppFonts.devanagari, textStyle: sized);
  }
}
