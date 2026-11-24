import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import 'l10n.dart';

/// "Instamart Clean" (v4) design system: a white app, light-grey tiles, ink text and one yellow action colour (see the design file in docs/design).
/// The dark theme is the same design on a near-black page. All colours come from [Pal];
/// never hardcode them in widgets. Read it with `context.pal`.
class Pal extends ThemeExtension<Pal> {
  const Pal({
    required this.dark,
    required this.gradient,
    required this.card,
    required this.onCard,
    required this.mutedOnCard,
    required this.soft,
    required this.onSoft,
    required this.tiles,
    required this.nav,
    required this.stepper,
    required this.onStepper,
    required this.minus,
    required this.chip,
    required this.onChip,
    required this.border,
    required this.onGround,
    required this.mutedOnGround,
    required this.error,
  });

  static const yellow = Color(0xFFFFD400);
  static const ink = Color(0xFF141118);
  static const pink = Color(
    0xFFC42F1D,
  ); // v4 error red (there is no pink in the design)
  static const green = Color(0xFF14A44D); // v4 success green

  final bool dark;
  final List<Color> gradient;
  final Color card,
      onCard,
      mutedOnCard,
      soft,
      onSoft,
      nav,
      stepper,
      onStepper,
      minus,
      chip,
      onChip,
      border;

  /// Text and icons that sit directly on the page (not on a card): ink on the white page, white on the dark one.
  final Color onGround, mutedOnGround;

  /// Error text and borders that read on the page.
  final Color error;

  /// Four category / feature tile colours (mint, pink, yellow, lavender in light).
  final List<Color> tiles;

  static const light = Pal(
    dark: false,
    gradient: [Colors.white, Colors.white],
    card: Color(0xFFF5F5F7),
    onCard: ink,
    mutedOnCard: Color(0xFF62606E),
    soft: Color(0xFFFFF4D6),
    onSoft: ink,
    tiles: [
      Color(0xFFE7F6E7),
      Color(0xFFFFECE6),
      Color(0xFFEAF2FF),
      Color(0xFFFFF3DC),
    ],
    nav: ink,
    stepper: yellow,
    onStepper: ink,
    minus: ink,
    chip: Colors.white,
    onChip: ink,
    border: Color(0xFFECECF0),
    onGround: ink,
    mutedOnGround: Color(0xFF62606E),
    error: Color(0xFFC42F1D),
  );

  static const darkPal = Pal(
    dark: true,
    gradient: [Color(0xFF0E0D12), Color(0xFF0E0D12)],
    card: Color(0xFF1B1A21),
    onCard: Colors.white,
    mutedOnCard: Color(0xFFA9A6B5),
    soft: Color(0xFF26242E),
    onSoft: Colors.white,
    tiles: [Color(0xFF7BE3A0), Color(0xFFFF9E91), yellow, Color(0xFFB9A8FF)],
    nav: Color(0xFF1F1E26),
    stepper: yellow,
    onStepper: ink,
    minus: ink,
    chip: Color(0xFF1B1A21),
    onChip: Colors.white,
    border: Color(0xFF2E2C38),
    onGround: Colors.white,
    mutedOnGround: Color(0xFFA9A6B5),
    error: Color(0xFFFF8A7A),
  );

  @override
  Pal copyWith() => this;

  @override
  Pal lerp(ThemeExtension<Pal>? other, double t) =>
      t < .5 ? this : (other as Pal? ?? this);
}

extension PalContext on BuildContext {
  Pal get pal => Theme.of(this).extension<Pal>()!;
}

class QC {
  static const rCard = 16.0, rField = 14.0, rPill = 999.0;

  /// The phone's status and navigation bars: transparent, so the page (white, or the dark gradient) shows through them with no band
  /// of another colour, and the icons are dark on the white page and light on the dark one.
  static SystemUiOverlayStyle overlay({required bool isDark}) =>
      SystemUiOverlayStyle(
        statusBarColor: Colors.transparent,
        statusBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
        statusBarBrightness: isDark ? Brightness.dark : Brightness.light,
        systemNavigationBarColor: Colors.transparent,
        systemNavigationBarDividerColor: Colors.transparent,
        systemNavigationBarIconBrightness: isDark
            ? Brightness.light
            : Brightness.dark,
        systemNavigationBarContrastEnforced: false,
      );

  /// v4 type: Bricolage Grotesque for headings and prices, Plus Jakarta Sans for everything else, Mukta behind both for Marathi.
  static const displayFont = 'Bricolage',
      bodyFont = 'PlusJakartaSans',
      marathiFont = 'Mukta';

  /// A heading style (Bricolage, 800). Colour comes from the caller or the surrounding text style.
  static TextStyle display(
    double size, {
    Color? color,
    FontWeight weight = FontWeight.w800,
    double? height,
  }) => TextStyle(
    fontFamily: displayFont,
    fontFamilyFallback: const [marathiFont],
    fontSize: size,
    fontWeight: weight,
    color: color,
    height: height,
    letterSpacing: -size * .02,
  );
  static const gutter = 16.0;
  static const minTap = 48.0;

  static ThemeData theme(Brightness b) {
    final pal = b == Brightness.dark ? Pal.darkPal : Pal.light;
    OutlineInputBorder border(Color c, [double w = 0]) => OutlineInputBorder(
      borderRadius: BorderRadius.circular(rField),
      borderSide: w == 0 ? BorderSide.none : BorderSide(color: c, width: w),
    );
    return ThemeData(
      useMaterial3: true,
      brightness: b,
      fontFamily: QC.bodyFont,
      fontFamilyFallback: const [QC.marathiFont],
      extensions: [pal],
      colorScheme: ColorScheme.fromSeed(seedColor: Pal.yellow, brightness: b)
          .copyWith(
            primary: Pal.yellow,
            onPrimary: Pal.ink,
            surface: pal.card,
            onSurface: pal.onCard,
            error: pal.dark ? Pal.pink : pal.error,
          ),
      scaffoldBackgroundColor: Colors.transparent,
      canvasColor: pal.card,
      appBarTheme: AppBarTheme(
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        foregroundColor: pal.onGround,
        titleTextStyle: TextStyle(
          color: pal.onGround,
          fontSize: 20,
          fontWeight: FontWeight.w800,
          fontFamily: QC.displayFont,
          fontFamilyFallback: const [QC.marathiFont],
        ),
        iconTheme: IconThemeData(color: pal.onGround),
        systemOverlayStyle: QC.overlay(isDark: pal.dark),
      ),
      textTheme: ThemeData(
        brightness: b,
        fontFamily: QC.bodyFont,
      ).textTheme.apply(bodyColor: pal.onGround, displayColor: pal.onGround),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: pal.card,
        contentPadding: const EdgeInsets.symmetric(
          vertical: 18,
          horizontal: 20,
        ),
        hintStyle: TextStyle(color: pal.mutedOnCard),
        labelStyle: TextStyle(color: pal.mutedOnCard),
        // A floating label would straddle the rounded border and get cut in half; fields carry their label above instead (LabeledField).
        floatingLabelBehavior: FloatingLabelBehavior.never,
        prefixIconColor: pal.mutedOnCard,
        suffixIconColor: pal.mutedOnCard,
        border: border(Colors.transparent),
        enabledBorder: border(Colors.transparent),
        focusedBorder: border(Pal.yellow, 2),
        errorBorder: border(Pal.pink, 2),
        focusedErrorBorder: border(Pal.pink, 2),
        errorStyle: TextStyle(color: pal.error, fontWeight: FontWeight.bold),
      ),
      textSelectionTheme: const TextSelectionThemeData(cursorColor: Pal.yellow),
      dividerTheme: DividerThemeData(color: pal.border, space: 1, thickness: 1),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: Pal.ink,
        contentTextStyle: const TextStyle(color: Colors.white),
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
      checkboxTheme: CheckboxThemeData(
        fillColor: WidgetStateProperty.resolveWith(
          (s) => s.contains(WidgetState.selected)
              ? Pal.yellow
              : Colors.transparent,
        ),
        checkColor: const WidgetStatePropertyAll(Pal.ink),
        side: BorderSide(
          color: pal.dark ? Colors.white70 : pal.mutedOnGround,
          width: 2,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: pal.dark ? Pal.yellow : Pal.ink,
          textStyle: const TextStyle(
            fontFamily: bodyFont,
            fontFamilyFallback: [marathiFont],
            fontWeight: FontWeight.w800,
          ),
        ),
      ),
    );
  }
}

/// Page background: the electric gradient behind a transparent Scaffold.
class AppScaffold extends StatelessWidget {
  const AppScaffold({
    super.key,
    required this.body,
    this.appBar,
    this.bottom,
    this.resizeToAvoidBottomInset = true,
  });
  final Widget body;
  final PreferredSizeWidget? appBar;
  final Widget? bottom;
  final bool resizeToAvoidBottomInset;

  @override
  Widget build(BuildContext context) => AnnotatedRegion<SystemUiOverlayStyle>(
    value: QC.overlay(isDark: context.pal.dark),
    child: Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: context.pal.gradient,
        ),
      ),
      child: Scaffold(
        backgroundColor: Colors.transparent,
        appBar: appBar,
        body: body,
        bottomNavigationBar: bottom,
        resizeToAvoidBottomInset: resizeToAvoidBottomInset,
      ),
    ),
  );
}

/// Round icon button used for back / favourite / notifications.
class CircleIconButton extends StatelessWidget {
  const CircleIconButton({
    super.key,
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.background,
    this.color,
    this.badge = false,
    this.badgeCount = 0,
  });
  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;
  final Color? background, color;
  final bool badge;

  /// A number on the button (for example unread notifications); nothing when zero. Over 9 reads "9+".
  final int badgeCount;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Tooltip(
      message: tooltip,
      child: InkResponse(
        onTap: onPressed,
        child: Container(
          width: 48,
          height: 48,
          decoration: BoxDecoration(
            color: background ?? p.card,
            shape: BoxShape.circle,
          ),
          child: Stack(
            alignment: Alignment.center,
            children: [
              Icon(icon, color: color ?? p.onCard),
              if (badgeCount > 0)
                Positioned(
                  top: 4,
                  right: 4,
                  child: Container(
                    constraints: const BoxConstraints(
                      minWidth: 18,
                      minHeight: 18,
                    ),
                    padding: const EdgeInsets.symmetric(horizontal: 4),
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: Pal.pink,
                      borderRadius: BorderRadius.circular(9),
                    ),
                    child: Text(
                      badgeCount > 9 ? '9+' : '$badgeCount',
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 11,
                        fontWeight: FontWeight.w900,
                        height: 1,
                      ),
                    ),
                  ),
                )
              else if (badge)
                const Positioned(
                  top: 11,
                  right: 12,
                  child: CircleAvatar(radius: 5, backgroundColor: Pal.pink),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Back that works when the screen was reached with `go` (nothing to pop): it then goes to [backFallback].
void goBack(BuildContext context, [String backFallback = '/']) {
  final router = GoRouter.maybeOf(context);
  if (router == null) {
    Navigator.of(context).maybePop();
  } else if (router.canPop()) {
    router.pop();
  } else {
    router.go(backFallback);
  }
}

PreferredSizeWidget appTopBar(
  BuildContext context,
  String title, {
  List<Widget>? actions,
  bool back = true,
  String backFallback = '/',
}) => AppBar(
  automaticallyImplyLeading: false,
  toolbarHeight: 72,
  leadingWidth: back ? 68 : 0,
  leading: back
      ? Padding(
          padding: const EdgeInsets.only(left: QC.gutter),
          child: CircleIconButton(
            icon: LucideIcons.chevronLeft,
            tooltip: context.tr('Back'),
            onPressed: () => goBack(context, backFallback),
          ),
        )
      : null,
  title: Text(title),
  titleSpacing: back ? 12 : QC.gutter,
  actions: [...?actions, const SizedBox(width: 12)],
);

/// Plain amount: no decimals for whole numbers, two decimals otherwise (34 or 34.50).
String amount(num v) =>
    v == v.roundToDouble() ? '${v.round()}' : v.toStringAsFixed(2);

String rupees(num v) => '₹${amount(v)}';
