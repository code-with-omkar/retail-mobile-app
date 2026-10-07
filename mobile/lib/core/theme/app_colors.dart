import 'package:flutter/material.dart';

import 'app_palette.dart';

/// Semantic colour roles for the customer app, exposed as a [ThemeExtension]
/// so light/dark switch and animate with the rest of [ThemeData].
@immutable
final class AppColors extends ThemeExtension<AppColors> {
  const AppColors({
    required this.surface,
    required this.card,
    required this.line,
    required this.ink,
    required this.inkMuted,
    // Primary CTA — Electric Violet
    required this.action,
    required this.onAction,
    required this.actionGradientStart,
    required this.actionGradientEnd,
    // Success — Electric Mint
    required this.success,
    required this.onSuccess,
    required this.successText,
    // Brand accent — Electric Cyan
    required this.brand,
    required this.onBrand,
    required this.brandText,
    // Deal / Error — Electric Coral
    required this.deal,
    required this.onDeal,
    required this.dealText,
    // Selected chip
    required this.selected,
    required this.onSelected,
    // Tints
    required this.actionTint,
    required this.successTint,
    required this.brandTint,
    required this.dealTint,
    // Misc
    required this.focusRing,
    required this.scrim,
  });

  final Color surface;
  final Color card;
  final Color line;

  /// Primary text and icons.
  final Color ink;

  /// Secondary text: units, meta, timestamps.
  final Color inkMuted;

  /// Electric Violet: primary buttons, Add, active nav.
  final Color action;
  final Color onAction;

  /// Gradient start/end for primary CTA (violet → cyan sweep).
  final Color actionGradientStart;
  final Color actionGradientEnd;

  /// Electric Mint: in stock, completed steps, savings.
  final Color success;
  final Color onSuccess;
  final Color successText;

  /// Electric Cyan: brand moments, links, "See all".
  final Color brand;
  final Color onBrand;
  final Color brandText;

  /// Electric Coral: deals, cart count, low stock, errors.
  final Color deal;
  final Color onDeal;
  final Color dealText;

  /// Selected chip / segmented option.
  final Color selected;
  final Color onSelected;

  final Color actionTint;
  final Color successTint;
  final Color brandTint;
  final Color dealTint;

  final Color focusRing;

  /// Modal barrier behind sheets/dialogs.
  final Color scrim;

  // ── Derived gradients ───────────────────────────────────────────────────

  /// Primary CTA gradient — electric violet → cyan (left-to-right).
  LinearGradient get actionGradient => LinearGradient(
        colors: [actionGradientStart, actionGradientEnd],
        begin: Alignment.centerLeft,
        end: Alignment.centerRight,
      );

  /// AppBar / hero header gradient — deep navy-violet → surface.
  LinearGradient get headerGradient => LinearGradient(
        colors: [actionGradientStart.withValues(alpha: 0.12), surface],
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
      );

  // ── Static instances ────────────────────────────────────────────────────

  static const AppColors light = AppColors(
    surface: AppPalette.surface,
    card: AppPalette.card,
    line: AppPalette.line,
    ink: AppPalette.ink,
    inkMuted: AppPalette.inkMuted,
    action: AppPalette.electricViolet,
    onAction: Colors.white,
    actionGradientStart: AppPalette.electricViolet,
    actionGradientEnd: AppPalette.electricCyan,
    success: AppPalette.electricMint,
    onSuccess: AppPalette.ink,
    successText: AppPalette.mintText,
    brand: AppPalette.electricCyan,
    onBrand: AppPalette.ink,
    brandText: AppPalette.cyanText,
    deal: AppPalette.electricCoral,
    onDeal: Colors.white,
    dealText: AppPalette.coralText,
    selected: AppPalette.electricViolet,
    onSelected: Colors.white,
    actionTint: AppPalette.violetTint,
    successTint: AppPalette.mintTint,
    brandTint: AppPalette.cyanTint,
    dealTint: AppPalette.coralTint,
    focusRing: AppPalette.electricViolet,
    scrim: Color(0x8C0D0E2B),
  );

  static const AppColors dark = AppColors(
    surface: AppPalette.night,
    card: AppPalette.nightCard,
    line: AppPalette.nightLine,
    ink: AppPalette.nightInk,
    inkMuted: AppPalette.nightMuted,
    action: AppPalette.electricViolet,
    onAction: Colors.white,
    actionGradientStart: AppPalette.electricViolet,
    actionGradientEnd: AppPalette.electricCyan,
    success: AppPalette.electricMint,
    onSuccess: AppPalette.ink,
    successText: AppPalette.electricMint,
    brand: AppPalette.electricCyan,
    onBrand: AppPalette.ink,
    // Cyan is 4.9:1 on night surface — readable.
    brandText: AppPalette.electricCyan,
    deal: AppPalette.electricCoral,
    onDeal: Colors.white,
    dealText: AppPalette.electricCoral,
    selected: AppPalette.electricViolet,
    onSelected: Colors.white,
    actionTint: AppPalette.violetTintDark,
    successTint: AppPalette.mintTintDark,
    brandTint: AppPalette.cyanTintDark,
    dealTint: AppPalette.coralTintDark,
    focusRing: AppPalette.electricCyan,
    scrim: Color(0xCC000000),
  );

  // ── ThemeExtension boilerplate ──────────────────────────────────────────

  @override
  AppColors copyWith({
    Color? surface,
    Color? card,
    Color? line,
    Color? ink,
    Color? inkMuted,
    Color? action,
    Color? onAction,
    Color? actionGradientStart,
    Color? actionGradientEnd,
    Color? success,
    Color? onSuccess,
    Color? successText,
    Color? brand,
    Color? onBrand,
    Color? brandText,
    Color? deal,
    Color? onDeal,
    Color? dealText,
    Color? selected,
    Color? onSelected,
    Color? actionTint,
    Color? successTint,
    Color? brandTint,
    Color? dealTint,
    Color? focusRing,
    Color? scrim,
  }) {
    return AppColors(
      surface: surface ?? this.surface,
      card: card ?? this.card,
      line: line ?? this.line,
      ink: ink ?? this.ink,
      inkMuted: inkMuted ?? this.inkMuted,
      action: action ?? this.action,
      onAction: onAction ?? this.onAction,
      actionGradientStart: actionGradientStart ?? this.actionGradientStart,
      actionGradientEnd: actionGradientEnd ?? this.actionGradientEnd,
      success: success ?? this.success,
      onSuccess: onSuccess ?? this.onSuccess,
      successText: successText ?? this.successText,
      brand: brand ?? this.brand,
      onBrand: onBrand ?? this.onBrand,
      brandText: brandText ?? this.brandText,
      deal: deal ?? this.deal,
      onDeal: onDeal ?? this.onDeal,
      dealText: dealText ?? this.dealText,
      selected: selected ?? this.selected,
      onSelected: onSelected ?? this.onSelected,
      actionTint: actionTint ?? this.actionTint,
      successTint: successTint ?? this.successTint,
      brandTint: brandTint ?? this.brandTint,
      dealTint: dealTint ?? this.dealTint,
      focusRing: focusRing ?? this.focusRing,
      scrim: scrim ?? this.scrim,
    );
  }

  @override
  AppColors lerp(covariant ThemeExtension<AppColors>? other, double t) {
    if (other is! AppColors) return this;
    Color mix(Color a, Color b) => Color.lerp(a, b, t)!;
    return AppColors(
      surface: mix(surface, other.surface),
      card: mix(card, other.card),
      line: mix(line, other.line),
      ink: mix(ink, other.ink),
      inkMuted: mix(inkMuted, other.inkMuted),
      action: mix(action, other.action),
      onAction: mix(onAction, other.onAction),
      actionGradientStart: mix(actionGradientStart, other.actionGradientStart),
      actionGradientEnd: mix(actionGradientEnd, other.actionGradientEnd),
      success: mix(success, other.success),
      onSuccess: mix(onSuccess, other.onSuccess),
      successText: mix(successText, other.successText),
      brand: mix(brand, other.brand),
      onBrand: mix(onBrand, other.onBrand),
      brandText: mix(brandText, other.brandText),
      deal: mix(deal, other.deal),
      onDeal: mix(onDeal, other.onDeal),
      dealText: mix(dealText, other.dealText),
      selected: mix(selected, other.selected),
      onSelected: mix(onSelected, other.onSelected),
      actionTint: mix(actionTint, other.actionTint),
      successTint: mix(successTint, other.successTint),
      brandTint: mix(brandTint, other.brandTint),
      dealTint: mix(dealTint, other.dealTint),
      focusRing: mix(focusRing, other.focusRing),
      scrim: mix(scrim, other.scrim),
    );
  }
}

extension AppColorsContext on BuildContext {
  /// Semantic colours for the current theme. Throws in debug if the theme was
  /// not built with [AppTheme], which is a wiring bug.
  AppColors get appColors {
    final AppColors? colors = Theme.of(this).extension<AppColors>();
    assert(colors != null, 'AppColors extension missing: build ThemeData with AppTheme.');
    return colors ?? AppColors.light;
  }
}
