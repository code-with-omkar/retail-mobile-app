import 'package:flutter/material.dart';

import 'app_colors.dart';
import 'app_dimens.dart';
import 'app_typography.dart';

export 'app_category_gradients.dart';
export 'app_colors.dart';
export 'app_dimens.dart';
export 'app_typography.dart';

/// Electric Market theme for the customer app.
///
/// ```dart
/// MaterialApp(
///   theme: AppTheme.light(),
///   darkTheme: AppTheme.dark(),
///   themeMode: ThemeMode.system,
/// );
/// ```
abstract final class AppTheme {
  static ThemeData light({bool useGoogleFonts = true}) =>
      _build(Brightness.light, AppColors.light, useGoogleFonts: useGoogleFonts);

  static ThemeData dark({bool useGoogleFonts = true}) =>
      _build(Brightness.dark, AppColors.dark, useGoogleFonts: useGoogleFonts);

  static ThemeData _build(Brightness brightness, AppColors c, {required bool useGoogleFonts}) {
    final ColorScheme scheme = ColorScheme(
      brightness: brightness,
      primary: c.action,
      onPrimary: c.onAction,
      primaryContainer: c.actionTint,
      onPrimaryContainer: c.ink,
      secondary: c.brand,
      onSecondary: c.onBrand,
      secondaryContainer: c.brandTint,
      onSecondaryContainer: c.ink,
      tertiary: c.deal,
      onTertiary: c.onDeal,
      tertiaryContainer: c.dealTint,
      onTertiaryContainer: c.ink,
      error: c.deal,
      onError: c.onDeal,
      errorContainer: c.dealTint,
      onErrorContainer: c.ink,
      surface: c.card,
      onSurface: c.ink,
      onSurfaceVariant: c.inkMuted,
      surfaceContainerLowest: c.card,
      surfaceContainerLow: c.card,
      surfaceContainer: c.surface,
      surfaceContainerHigh: c.surface,
      surfaceContainerHighest: c.line,
      outline: c.inkMuted,
      outlineVariant: c.line,
      shadow: const Color(0xFF000000),
      scrim: c.scrim,
      inverseSurface: c.ink,
      onInverseSurface: c.surface,
      inversePrimary: c.action,
      surfaceTint: Colors.transparent,
    );

    final TextTheme text = AppTypography.textTheme(
      ink: c.ink,
      inkMuted: c.inkMuted,
      useGoogleFonts: useGoogleFonts,
    );

    const StadiumBorder pill = StadiumBorder();
    const Size actionSize = Size.fromHeight(56);

    return ThemeData(
      useMaterial3: true,
      brightness: brightness,
      colorScheme: scheme,
      scaffoldBackgroundColor: c.surface,
      canvasColor: c.surface,
      textTheme: text,
      focusColor: c.focusRing.withValues(alpha: 0.20),
      splashFactory: InkSparkle.splashFactory,
      materialTapTargetSize: MaterialTapTargetSize.padded,
      extensions: <ThemeExtension<dynamic>>[c],

      // ── AppBar ──────────────────────────────────────────────────────────
      appBarTheme: AppBarTheme(
        backgroundColor: c.surface,
        foregroundColor: c.ink,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleTextStyle: text.headlineMedium,
      ),

      // ── Cards ───────────────────────────────────────────────────────────
      cardTheme: CardThemeData(
        color: c.card,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: const RoundedRectangleBorder(borderRadius: AppRadius.cardAll),
      ),

      dividerTheme: DividerThemeData(color: c.line, thickness: 1, space: 1),

      // ── Buttons ─────────────────────────────────────────────────────────
      // Primary: use gradient via GradientFilledButton wrapper; solid fallback.
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: c.action,
          foregroundColor: c.onAction,
          disabledBackgroundColor: c.line,
          disabledForegroundColor: c.inkMuted,
          minimumSize: actionSize,
          shape: pill,
          textStyle: text.labelLarge,
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.x6),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: c.ink,
          foregroundColor: c.surface,
          elevation: 0,
          minimumSize: const Size.fromHeight(48),
          shape: pill,
          textStyle: text.labelLarge?.copyWith(fontSize: 14),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: c.action,
          minimumSize: const Size.fromHeight(48),
          side: BorderSide(color: c.action, width: 2),
          shape: pill,
          textStyle: text.labelLarge?.copyWith(fontSize: 15, fontWeight: FontWeight.w700),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: c.brandText,
          minimumSize: const Size(AppSpacing.minTarget, AppSpacing.minTarget),
          textStyle: text.labelMedium,
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          foregroundColor: c.ink,
          backgroundColor: c.card,
          minimumSize: const Size(AppSpacing.minTarget, AppSpacing.minTarget),
          shape: const CircleBorder(),
          side: BorderSide(color: c.line),
        ),
      ),

      // ── Input ───────────────────────────────────────────────────────────
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: c.card,
        hintStyle: text.bodyMedium?.copyWith(color: c.inkMuted),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.x4,
          vertical: AppSpacing.x4,
        ),
        border: OutlineInputBorder(
          borderRadius: AppRadius.fieldAll,
          borderSide: BorderSide(color: c.line),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: AppRadius.fieldAll,
          borderSide: BorderSide(color: c.line),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: AppRadius.fieldAll,
          borderSide: BorderSide(color: c.focusRing, width: 2),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: AppRadius.fieldAll,
          borderSide: BorderSide(color: c.deal, width: 2),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: AppRadius.fieldAll,
          borderSide: BorderSide(color: c.deal, width: 2),
        ),
        errorStyle: text.bodySmall?.copyWith(
          color: c.dealText,
          fontWeight: FontWeight.w700,
        ),
      ),

      // ── Chips ───────────────────────────────────────────────────────────
      chipTheme: ChipThemeData(
        backgroundColor: c.card,
        selectedColor: c.selected,
        disabledColor: c.line,
        side: BorderSide(color: c.line),
        shape: pill,
        showCheckmark: false,
        labelStyle: text.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
        secondaryLabelStyle: text.bodyMedium?.copyWith(
          fontWeight: FontWeight.w800,
          color: c.onSelected,
        ),
        padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.x3,
          vertical: AppSpacing.x2,
        ),
      ),

      // ── Badge ───────────────────────────────────────────────────────────
      badgeTheme: BadgeThemeData(
        backgroundColor: c.deal,
        textColor: c.onDeal,
        textStyle: text.labelSmall,
      ),

      // ── Bottom sheet ────────────────────────────────────────────────────
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: c.card,
        modalBackgroundColor: c.card,
        modalBarrierColor: c.scrim,
        showDragHandle: true,
        dragHandleColor: c.line,
        shape: const RoundedRectangleBorder(borderRadius: AppRadius.sheetTop),
      ),

      // ── Dialog ──────────────────────────────────────────────────────────
      dialogTheme: DialogThemeData(
        backgroundColor: c.card,
        barrierColor: c.scrim,
        shape: const RoundedRectangleBorder(borderRadius: AppRadius.tileAll),
        titleTextStyle: text.titleLarge,
        contentTextStyle: text.bodyMedium,
      ),

      // ── Snack bar ───────────────────────────────────────────────────────
      snackBarTheme: SnackBarThemeData(
        backgroundColor: c.ink,
        contentTextStyle: text.bodyMedium?.copyWith(color: c.surface),
        actionTextColor: c.brand,
        behavior: SnackBarBehavior.floating,
        shape: const RoundedRectangleBorder(borderRadius: AppRadius.cardAll),
      ),

      // ── Progress ────────────────────────────────────────────────────────
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: c.action,
        linearTrackColor: c.line,
        circularTrackColor: c.line,
      ),

      // ── Radio / Switch ──────────────────────────────────────────────────
      radioTheme: RadioThemeData(
        fillColor: WidgetStateProperty.resolveWith<Color>(
          (Set<WidgetState> states) =>
              states.contains(WidgetState.selected) ? c.action : c.inkMuted,
        ),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith<Color>(
          (Set<WidgetState> states) =>
              states.contains(WidgetState.selected) ? c.onSelected : c.card,
        ),
        trackColor: WidgetStateProperty.resolveWith<Color>(
          (Set<WidgetState> states) =>
              states.contains(WidgetState.selected) ? c.selected : c.line,
        ),
      ),
    );
  }
}

// ── Gradient CTA button ──────────────────────────────────────────────────────

/// Drop-in replacement for [FilledButton] that paints the [AppColors.actionGradient].
///
/// Usage:
/// ```dart
/// GradientFilledButton(
///   onPressed: () {},
///   child: const Text('Add to Cart'),
/// )
/// ```
class GradientFilledButton extends StatelessWidget {
  const GradientFilledButton({
    super.key,
    required this.onPressed,
    required this.child,
    this.minimumSize = const Size.fromHeight(56),
  });

  final VoidCallback? onPressed;
  final Widget child;
  final Size minimumSize;

  @override
  Widget build(BuildContext context) {
    final AppColors c = context.appColors;
    final bool disabled = onPressed == null;
    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: disabled ? null : c.actionGradient,
        color: disabled ? c.line : null,
        borderRadius: BorderRadius.circular(minimumSize.height / 2),
      ),
      child: FilledButton(
        onPressed: onPressed,
        style: FilledButton.styleFrom(
          backgroundColor: Colors.transparent,
          shadowColor: Colors.transparent,
          foregroundColor: disabled ? c.inkMuted : c.onAction,
          minimumSize: minimumSize,
          shape: StadiumBorder(),
          padding: const EdgeInsets.symmetric(horizontal: 24),
        ).copyWith(
          backgroundColor: WidgetStateProperty.all(Colors.transparent),
        ),
        child: child,
      ),
    );
  }
}
