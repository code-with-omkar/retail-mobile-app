import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import 'l10n.dart';

/// "Electric Yellow" design system (see docs/design). All colours come from [Pal];
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
  });

  static const yellow = Color(0xFFFFD400);
  static const ink = Color(0xFF141118);
  static const pink = Color(0xFFFB1A8E);
  static const green = Color(0xFF02F34C);
  static const onGround = Colors.white;
  static const mutedOnGround = Color(0xFFD8CCFF);

  final bool dark;
  final List<Color> gradient;
  final Color card, onCard, mutedOnCard, soft, onSoft, nav, stepper, onStepper, minus, chip, onChip, border;

  /// Four category / feature tile colours (mint, pink, yellow, lavender in light).
  final List<Color> tiles;

  static const light = Pal(
    dark: false,
    gradient: [Color(0xFF2A1BFF), Color(0xFF6A11E8), Color(0xFFB00699)],
    card: Color(0xFFE9E4FF),
    onCard: ink,
    mutedOnCard: Color(0xFF5B5575),
    soft: Color(0xFFFFEEAA),
    onSoft: ink,
    tiles: [Color(0xFFB8FFD2), Color(0xFFFFC4E1), Color(0xFFFFEEAA), Color(0xFFE9E4FF)],
    nav: ink,
    stepper: ink,
    onStepper: Colors.white,
    minus: Color(0xFF3A3640),
    chip: Colors.white,
    onChip: ink,
    border: Color(0xFFD3C9FA),
  );

  static const darkPal = Pal(
    dark: true,
    gradient: [Color(0xFF0E0A3A), Color(0xFF24105E), Color(0xFF4A0A4A)],
    card: Color(0xFF1E1846),
    onCard: Colors.white,
    mutedOnCard: Color(0xFFC7BDF0),
    soft: Color(0xFF2A2260),
    onSoft: Colors.white,
    tiles: [green, pink, yellow, Color(0xFFB9A8FF)],
    nav: Color(0xFF0B0820),
    stepper: Color(0xFFE9E4FF),
    onStepper: ink,
    minus: Color(0xFFB9A8FF),
    chip: Color(0xFF0B0820),
    onChip: Colors.white,
    border: Color(0xFF332A73),
  );

  @override
  Pal copyWith() => this;

  @override
  Pal lerp(ThemeExtension<Pal>? other, double t) => t < .5 ? this : (other as Pal? ?? this);
}

extension PalContext on BuildContext {
  Pal get pal => Theme.of(this).extension<Pal>()!;
}

class QC {
  static const rCard = 28.0, rField = 20.0, rPill = 999.0;
  static const gutter = 20.0;
  static const minTap = 48.0;

  static ThemeData theme(Brightness b) {
    final pal = b == Brightness.dark ? Pal.darkPal : Pal.light;
    OutlineInputBorder border(Color c, [double w = 0]) =>
        OutlineInputBorder(borderRadius: BorderRadius.circular(rField), borderSide: w == 0 ? BorderSide.none : BorderSide(color: c, width: w));
    return ThemeData(
      useMaterial3: true,
      brightness: b,
      fontFamily: 'Arial',
      extensions: [pal],
      colorScheme: ColorScheme.fromSeed(seedColor: Pal.yellow, brightness: b).copyWith(
        primary: Pal.yellow,
        onPrimary: Pal.ink,
        surface: pal.card,
        onSurface: pal.onCard,
        error: Pal.pink,
      ),
      scaffoldBackgroundColor: Colors.transparent,
      canvasColor: pal.card,
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        foregroundColor: Colors.white,
        titleTextStyle: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w800, fontFamily: 'Arial'),
        iconTheme: IconThemeData(color: Colors.white),
        systemOverlayStyle: SystemUiOverlayStyle.light,
      ),
      textTheme: ThemeData(brightness: b, fontFamily: 'Arial').textTheme.apply(bodyColor: Pal.onGround, displayColor: Pal.onGround),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: pal.card,
        contentPadding: const EdgeInsets.symmetric(vertical: 18, horizontal: 20),
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
        errorStyle: const TextStyle(color: Color(0xFFFFB3D9), fontWeight: FontWeight.bold),
      ),
      textSelectionTheme: const TextSelectionThemeData(cursorColor: Pal.yellow),
      dividerTheme: DividerThemeData(color: pal.border, space: 1, thickness: 1),
      snackBarTheme: SnackBarThemeData(backgroundColor: Pal.ink, contentTextStyle: const TextStyle(color: Colors.white), behavior: SnackBarBehavior.floating, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
      checkboxTheme: CheckboxThemeData(
        fillColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? Pal.yellow : Colors.transparent),
        checkColor: const WidgetStatePropertyAll(Pal.ink),
        side: const BorderSide(color: Colors.white70, width: 2),
      ),
      textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(foregroundColor: Pal.yellow, textStyle: const TextStyle(fontWeight: FontWeight.w800))),
    );
  }
}

/// Page background: the electric gradient behind a transparent Scaffold.
class AppScaffold extends StatelessWidget {
  const AppScaffold({super.key, required this.body, this.appBar, this.bottom, this.resizeToAvoidBottomInset = true});
  final Widget body;
  final PreferredSizeWidget? appBar;
  final Widget? bottom;
  final bool resizeToAvoidBottomInset;

  @override
  Widget build(BuildContext context) => AnnotatedRegion<SystemUiOverlayStyle>(
        value: SystemUiOverlayStyle.light,
        child: Container(
          decoration: BoxDecoration(gradient: LinearGradient(begin: Alignment.topCenter, end: Alignment.bottomCenter, colors: context.pal.gradient)),
          child: Scaffold(backgroundColor: Colors.transparent, appBar: appBar, body: body, bottomNavigationBar: bottom, resizeToAvoidBottomInset: resizeToAvoidBottomInset),
        ),
      );
}

/// Round icon button used for back / favourite / notifications.
class CircleIconButton extends StatelessWidget {
  const CircleIconButton({super.key, required this.icon, required this.tooltip, required this.onPressed, this.background, this.color, this.badge = false});
  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;
  final Color? background, color;
  final bool badge;

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
          decoration: BoxDecoration(color: background ?? p.card, shape: BoxShape.circle),
          child: Stack(alignment: Alignment.center, children: [
            Icon(icon, color: color ?? p.onCard),
            if (badge) const Positioned(top: 11, right: 12, child: CircleAvatar(radius: 5, backgroundColor: Pal.pink)),
          ]),
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

PreferredSizeWidget appTopBar(BuildContext context, String title, {List<Widget>? actions, bool back = true, String backFallback = '/'}) => AppBar(
      automaticallyImplyLeading: false,
      toolbarHeight: 72,
      leadingWidth: back ? 68 : 0,
      leading: back ? Padding(padding: const EdgeInsets.only(left: QC.gutter), child: CircleIconButton(icon: Icons.arrow_back_ios_new_rounded, tooltip: context.tr('Back'), onPressed: () => goBack(context, backFallback))) : null,
      title: Text(title),
      titleSpacing: back ? 12 : QC.gutter,
      actions: [...?actions, const SizedBox(width: 12)],
    );

/// Plain amount: no decimals for whole numbers, two decimals otherwise (34 or 34.50).
String amount(num v) => v == v.roundToDouble() ? '${v.round()}' : v.toStringAsFixed(2);

String rupees(num v) => '₹${amount(v)}';
