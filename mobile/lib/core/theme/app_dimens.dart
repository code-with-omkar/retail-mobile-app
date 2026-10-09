import 'package:flutter/widgets.dart';

/// Spacing scale (px). Screen gutter is [gutter].
abstract final class AppSpacing {
  static const double x1 = 4;
  static const double x2 = 8;
  static const double x3 = 12;
  static const double x4 = 16;
  static const double x5 = 20;
  static const double x6 = 24;
  static const double x8 = 32;

  static const double gutter = x5;
  static const EdgeInsets screen = EdgeInsets.symmetric(horizontal: gutter);

  /// Minimum touch target.
  static const double minTarget = 44;
}

/// Corner radii: badge 10 · field 16 · card 24 · tile/sheet 32 · pill.
abstract final class AppRadius {
  static const double badge = 10;
  static const double field = 16;
  static const double card = 24;
  static const double tile = 32;
  static const double pill = 999;

  static const BorderRadius badgeAll = BorderRadius.all(Radius.circular(badge));
  static const BorderRadius fieldAll = BorderRadius.all(Radius.circular(field));
  static const BorderRadius cardAll = BorderRadius.all(Radius.circular(card));
  static const BorderRadius tileAll = BorderRadius.all(Radius.circular(tile));
  static const BorderRadius pillAll = BorderRadius.all(Radius.circular(pill));
  static const BorderRadius sheetTop = BorderRadius.vertical(top: Radius.circular(tile));
}

/// Shadow for floating elements only (nav capsule, cart pill, sheets).
abstract final class AppElevation {
  static const List<BoxShadow> floating = <BoxShadow>[
    BoxShadow(color: Color(0x2E141118), blurRadius: 32, offset: Offset(0, 12)),
  ];
}

/// Motion tokens.
abstract final class AppMotion {
  static const Duration fast = Duration(milliseconds: 200);
  static const Curve standard = Curves.easeOut;
}
