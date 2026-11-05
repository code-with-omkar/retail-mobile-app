import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../data/repositories/offers_repository.dart';

/// One festival's look on an offer card, from the QuickCart design file (Festival offer cards). The key is what the API stores on the offer;
/// the portal draws the same eight.
class FestivalTheme {
  const FestivalTheme({
    required this.key,
    required this.name,
    required this.mr,
    required this.bg,
    required this.ink,
    required this.muted,
    required this.chipBg,
    required this.cta,
    required this.ctaBg,
    required this.ctaInk,
    required this.m1,
    required this.m2,
    required this.m3,
  });

  final String key, name, mr, cta;
  final Color bg, ink, muted, chipBg, ctaBg, ctaInk, m1, m2, m3;
}

const _y = Color(0xFFFFD400);
const _ink = Color(0xFF141118);
const _white24 = Color(0x29FFFFFF); // 16% white

/// In the order the API lists them.
const festivalThemes = <FestivalTheme>[
  FestivalTheme(
    key: 'diwali',
    name: 'Diwali',
    mr: 'दिवाळी',
    bg: Color(0xFF4A0E2E),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFF4D9E6),
    chipBg: _white24,
    cta: 'Shop Diwali',
    ctaBg: Color(0xFFFFC94D),
    ctaInk: Color(0xFF2A0718),
    m1: Color(0xFFFFC94D),
    m2: Color(0xFFFF8A00),
    m3: Color(0xFFFFF1A8),
  ),
  FestivalTheme(
    key: 'ganesh',
    name: 'Ganeshotsav',
    mr: 'गणेशोत्सव',
    bg: Color(0xFFFF8A00),
    ink: Color(0xFF2A1405),
    muted: Color(0xFF4A2A0A),
    chipBg: Color(0x1F2A1405),
    cta: 'Shop puja',
    ctaBg: Color(0xFF2A1405),
    ctaInk: Color(0xFFFFFFFF),
    m1: Color(0xFFFFE3B8),
    m2: Color(0xFFC25E00),
    m3: Color(0xFFFFFFFF),
  ),
  FestivalTheme(
    key: 'navratri',
    name: 'Navratri',
    mr: 'नवरात्री',
    bg: Color(0xFF6A1B9A),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFEBD7F5),
    chipBg: _white24,
    cta: 'Shop now',
    ctaBg: _y,
    ctaInk: _ink,
    m1: _y,
    m2: Color(0xFFFF7A1A),
    m3: Color(0xFFFFFFFF),
  ),
  FestivalTheme(
    key: 'holi',
    name: 'Holi',
    mr: 'होळी',
    bg: Color(0xFF0B6E79),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFD4F1F4),
    chipBg: _white24,
    cta: 'Shop Holi',
    ctaBg: _y,
    ctaInk: _ink,
    m1: _y,
    m2: Color(0xFFFF7A1A),
    m3: Color(0xFFB9FFCF),
  ),
  FestivalTheme(
    key: 'sankranti',
    name: 'Sankranti',
    mr: 'मकर संक्रांत',
    bg: Color(0xFF1565C0),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFD6E6FA),
    chipBg: _white24,
    cta: 'Shop now',
    ctaBg: _y,
    ctaInk: _ink,
    m1: _y,
    m2: Color(0xFFFFFFFF),
    m3: Color(0xFFFF7A1A),
  ),
  FestivalTheme(
    key: 'padwa',
    name: 'Gudi Padwa',
    mr: 'गुढीपाडवा',
    bg: Color(0xFF0B7A3E),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFD7F2E2),
    chipBg: _white24,
    cta: 'Shop now',
    ctaBg: _y,
    ctaInk: _ink,
    m1: _y,
    m2: Color(0xFFFFFFFF),
    m3: Color(0xFFFF7A1A),
  ),
  FestivalTheme(
    key: 'eid',
    name: 'Eid',
    mr: 'ईद',
    bg: Color(0xFF0E4D45),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFD3ECE8),
    chipBg: _white24,
    cta: 'Shop Eid',
    ctaBg: _y,
    ctaInk: _ink,
    m1: _y,
    m2: Color(0xFFFFFFFF),
    m3: _y,
  ),
  FestivalTheme(
    key: 'christmas',
    name: 'Christmas',
    mr: 'ख्रिसमस',
    bg: Color(0xFFB71C1C),
    ink: Color(0xFFFFFFFF),
    muted: Color(0xFFFBDADA),
    chipBg: Color(0x2EFFFFFF),
    cta: 'Shop now',
    ctaBg: Color(0xFFFFFFFF),
    ctaInk: Color(0xFFB71C1C),
    m1: Color(0xFFFFFFFF),
    m2: _y,
    m3: _y,
  ),
];

FestivalTheme? festivalThemeOf(String? key) =>
    festivalThemes.where((t) => t.key == key).firstOrNull;

/// The countdown chip: "FESTIVE OFFER", or with the days left ("ENDS IN 3 DAYS", "ENDS TODAY").
String festivalChip(BuildContext context, DateTime? endsAt, {DateTime? now}) {
  final base = context.tr('FESTIVE OFFER');
  if (endsAt == null) return base;
  final left = endsAt.difference(now ?? DateTime.now());
  final days = (left.inSeconds / 86400).ceil();
  if (days <= 0) return '$base · ${context.tr('ENDS TODAY')}';
  return '$base · ${days == 1 ? context.tr('ENDS IN 1 DAY') : context.tr('ENDS IN {n} DAYS', {'n': '$days'})}';
}

/// An offer shown as its festival card: the festival's colours and motif, a countdown chip, the English and Marathi name, the offer's own
/// title as the line and a button. With an uploaded picture the picture is the card and only the chip and the button sit on it.
class FestivalCard extends StatelessWidget {
  const FestivalCard(this.offer, {super.key, this.height = 176});
  final Offer offer;
  final double height;

  @override
  Widget build(BuildContext context) {
    final theme = festivalThemeOf(offer.cardDesign);
    if (theme == null) return const SizedBox.shrink();
    final marathi = context.isMarathi;
    final picture = offer.imageUrl;
    final chip = Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(
        color: picture != null ? const Color(0xB8141118) : theme.chipBg,
        borderRadius: BorderRadius.circular(QC.rPill),
      ),
      child: Text(
        festivalChip(context, offer.endsAt),
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(
          color: picture != null ? Colors.white : theme.ink,
          fontSize: 10,
          fontWeight: FontWeight.w800,
          letterSpacing: .6,
        ),
      ),
    );
    final button = Container(
      height: 36,
      padding: const EdgeInsets.symmetric(horizontal: 16),
      decoration: BoxDecoration(
        color: theme.ctaBg,
        borderRadius: BorderRadius.circular(QC.rPill),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          // Flexible so a long button text in a narrow card is cut with an ellipsis instead of overflowing.
          Flexible(
            child: Text(
              context.tr(theme.cta),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: theme.ctaInk,
                fontSize: 13,
                fontWeight: FontWeight.w800,
              ),
            ),
          ),
          const SizedBox(width: 6),
          Icon(Icons.chevron_right_rounded, size: 16, color: theme.ctaInk),
        ],
      ),
    );

    return Semantics(
      button: true,
      label: offer.titleIn(marathi),
      // The whole card is the control: its parts are not separate stops for a screen reader.
      excludeSemantics: true,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(20),
        child: Container(
          height: height,
          color: picture != null ? const Color(0xFF141118) : theme.bg,
          child: Stack(
            fit: StackFit.expand,
            children: [
              if (picture != null)
                CachedNetworkImage(
                  imageUrl: picture,
                  fit: BoxFit.cover,
                  placeholder: (_, _) => ColoredBox(color: theme.bg),
                  errorWidget: (_, _, _) => ColoredBox(color: theme.bg),
                )
              else
                Positioned(
                  right: 12,
                  bottom: 12,
                  child: SizedBox(
                    width: 104,
                    height: 104,
                    child: CustomPaint(painter: FestivalMotifPainter(theme)),
                  ),
                ),
              Padding(
                padding: const EdgeInsets.fromLTRB(18, 16, 18, 16),
                // The text area takes what the button leaves and its lines give way (ellipsis, then clipped), so a long festival name, a long
                // offer line or large text can never push the card past its height.
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: ClipRect(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            chip,
                            if (picture == null) ...[
                              const SizedBox(height: 6),
                              Flexible(
                                child: ConstrainedBox(
                                  constraints: const BoxConstraints(
                                    maxWidth: 220,
                                  ),
                                  child: Text.rich(
                                    TextSpan(
                                      children: [
                                        TextSpan(
                                          text: theme.name,
                                          style: TextStyle(
                                            color: theme.ink,
                                            fontSize: 24,
                                            height: 28 / 24,
                                            fontWeight: FontWeight.w800,
                                            letterSpacing: -.5,
                                          ),
                                        ),
                                        TextSpan(
                                          text: '  ${theme.mr}',
                                          style: TextStyle(
                                            color: theme.muted,
                                            fontSize: 17,
                                            fontWeight: FontWeight.w600,
                                          ),
                                        ),
                                      ],
                                    ),
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                              ),
                              const SizedBox(height: 4),
                              Flexible(
                                child: ConstrainedBox(
                                  constraints: const BoxConstraints(
                                    maxWidth: 220,
                                  ),
                                  child: Text(
                                    offer.titleIn(marathi),
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      color: theme.muted,
                                      fontSize: 13,
                                      height: 18 / 13,
                                    ),
                                  ),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 8),
                    button,
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The festival's motif (a diya, a canopy, a kite ...) drawn from the design's shapes, in the theme's three accent colours. Decorative.
class FestivalMotifPainter extends CustomPainter {
  FestivalMotifPainter(this.theme);
  final FestivalTheme theme;

  Paint _fill(Color c) => Paint()..color = c;
  Paint _stroke(Color c, double w) => Paint()
    ..color = c
    ..style = PaintingStyle.stroke
    ..strokeWidth = w
    ..strokeCap = StrokeCap.round;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.save();
    canvas.scale(size.width / 96, size.height / 96);
    final t = theme;
    switch (t.key) {
      case 'diwali':
        canvas.drawPath(
          Path()
            ..moveTo(48, 16)
            ..cubicTo(56, 30, 58, 40, 48, 50)
            ..cubicTo(38, 40, 40, 30, 48, 16)
            ..close(),
          _fill(t.m2),
        );
        canvas.drawPath(
          Path()
            ..moveTo(48, 30)
            ..cubicTo(52, 38, 52, 44, 48, 48)
            ..cubicTo(44, 44, 44, 38, 48, 30)
            ..close(),
          _fill(t.m3),
        );
        canvas.drawPath(
          Path()
            ..moveTo(12, 54)
            ..quadraticBezierTo(48, 92, 84, 54)
            ..close(),
          _fill(t.m1),
        );
        canvas.drawOval(
          Rect.fromCenter(center: const Offset(48, 80), width: 32, height: 10),
          _fill(t.m1),
        );
      case 'ganesh':
        canvas.drawPath(
          Path()
            ..moveTo(48, 10)
            ..cubicTo(62, 30, 76, 46, 76, 62)
            ..arcToPoint(
              const Offset(20, 62),
              radius: const Radius.elliptical(28, 22),
              clockwise: true,
            )
            ..cubicTo(20, 46, 34, 30, 48, 10)
            ..close(),
          _fill(t.m1),
        );
        final rib = _stroke(t.m2, 3);
        for (final end in const [
          Offset(38, 82),
          Offset(58, 82),
          Offset(26, 76),
          Offset(70, 76),
        ]) {
          canvas.drawLine(const Offset(48, 12), end, rib);
        }
        canvas.drawCircle(const Offset(48, 12), 4, _fill(t.m3));
      case 'navratri':
        canvas.drawLine(
          const Offset(18, 82),
          const Offset(78, 14),
          _stroke(t.m1, 9),
        );
        canvas.drawLine(
          const Offset(18, 14),
          const Offset(78, 82),
          _stroke(t.m2, 9),
        );
        canvas.drawCircle(const Offset(30, 68), 5, _fill(t.m3));
        canvas.drawCircle(const Offset(66, 68), 5, _fill(t.m3));
        canvas.drawCircle(const Offset(48, 48), 7, _fill(t.m3));
      case 'holi':
        canvas.drawCircle(const Offset(36, 38), 24, _fill(t.m1));
        canvas.drawCircle(const Offset(64, 58), 22, _fill(t.m2));
        canvas.drawCircle(const Offset(38, 72), 13, _fill(t.m3));
        canvas.drawCircle(const Offset(74, 24), 8, _fill(t.m1));
        canvas.drawCircle(const Offset(16, 66), 5, _fill(t.m2));
      case 'sankranti':
        canvas.drawPath(
          Path()
            ..moveTo(48, 6)
            ..lineTo(78, 38)
            ..lineTo(48, 70)
            ..lineTo(18, 38)
            ..close(),
          _fill(t.m1),
        );
        final spar = _stroke(t.m2, 2.5);
        canvas.drawLine(const Offset(48, 6), const Offset(48, 70), spar);
        canvas.drawLine(const Offset(18, 38), const Offset(78, 38), spar);
        canvas.drawPath(
          Path()
            ..moveTo(48, 70)
            ..cubicTo(38, 78, 58, 82, 48, 92),
          _stroke(t.m3, 3),
        );
        canvas.drawLine(
          const Offset(44, 80),
          const Offset(38, 84),
          _stroke(t.m3, 3),
        );
        canvas.drawLine(
          const Offset(52, 86),
          const Offset(58, 82),
          _stroke(t.m3, 3),
        );
      case 'padwa':
        canvas.drawLine(
          const Offset(48, 18),
          const Offset(48, 94),
          _stroke(t.m2, 4),
        );
        canvas.drawPath(
          Path()
            ..moveTo(38, 26)
            ..lineTo(58, 26)
            ..lineTo(66, 62)
            ..lineTo(30, 62)
            ..close(),
          _fill(t.m1),
        );
        canvas.drawCircle(const Offset(48, 16), 10, _fill(t.m3));
        for (final c in const [
          Offset(36, 30),
          Offset(48, 32),
          Offset(60, 30),
        ]) {
          canvas.drawCircle(c, 3, _fill(t.m2));
        }
      case 'eid':
        canvas.drawCircle(const Offset(42, 50), 32, _fill(t.m1));
        canvas.drawCircle(
          const Offset(56, 42),
          28,
          _fill(t.bg),
        ); // cuts the crescent out of the disc
        canvas.drawPath(
          Path()
            ..moveTo(72, 30)
            ..lineTo(75, 38)
            ..lineTo(84, 38)
            ..lineTo(77, 43)
            ..lineTo(80, 52)
            ..lineTo(72, 47)
            ..lineTo(64, 52)
            ..lineTo(67, 43)
            ..lineTo(60, 38)
            ..lineTo(69, 38)
            ..close(),
          _fill(t.m2),
        );
      case 'christmas':
        canvas.drawPath(
          Path()
            ..moveTo(48, 12)
            ..lineTo(72, 44)
            ..lineTo(58, 44)
            ..lineTo(80, 76)
            ..lineTo(16, 76)
            ..lineTo(38, 44)
            ..lineTo(24, 44)
            ..close(),
          _fill(t.m1),
        );
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            const Rect.fromLTWH(42, 76, 12, 12),
            const Radius.circular(2),
          ),
          _fill(t.m2),
        );
        canvas.drawCircle(const Offset(48, 10), 6, _fill(t.m3));
        canvas.drawCircle(const Offset(40, 58), 3, _fill(t.m3));
        canvas.drawCircle(const Offset(58, 66), 3, _fill(t.m3));
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(FestivalMotifPainter old) => old.theme.key != theme.key;
}

/// Short tag shown on a product that belongs to a running festival offer, and the Marathi greeting shown on Home.
const festivalTags = <String, String>{
  'diwali': 'DIWALI DEAL',
  'ganesh': 'BAPPA SPECIAL',
  'navratri': 'NAVRATRI SPECIAL',
  'holi': 'HOLI DEAL',
  'sankranti': 'TIL-GUL DEAL',
  'padwa': 'PADWA SPECIAL',
  'eid': 'EID SPECIAL',
  'christmas': 'XMAS DEAL',
};

const festivalGreetings = <String, String>{
  'diwali': 'शुभ दीपावली',
  'ganesh': 'गणपती बाप्पा मोरया',
  'navratri': 'शुभ नवरात्री',
  'holi': 'होळीच्या शुभेच्छा',
  'sankranti': 'तिळगूळ घ्या, गोड गोड बोला',
  'padwa': 'गुढीपाडव्याच्या शुभेच्छा',
  'eid': 'ईद मुबारक',
  'christmas': 'नाताळच्या शुभेच्छा',
};

/// Product id -> festival design, from the running offers that have a festival card design (the first offer wins).
Map<String, FestivalTheme> festivalProductThemes(Iterable<Offer> offers) {
  final out = <String, FestivalTheme>{};
  for (final o in offers) {
    final t = festivalThemeOf(o.cardDesign);
    if (t == null) continue;
    for (final id in o.productIds) {
      out.putIfAbsent(id, () => t);
    }
  }
  return out;
}

/// The design of the first running festival offer, for the Home greeting; null when there is none.
FestivalTheme? festivalGreetingTheme(Iterable<Offer> offers) {
  for (final o in offers) {
    final t = festivalThemeOf(o.cardDesign);
    if (t != null) return t;
  }
  return null;
}

/// A small festive tag drawn on a product picture.
class FestivalProductTag extends StatelessWidget {
  const FestivalProductTag(this.theme, {super.key});
  final FestivalTheme theme;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(color: theme.bg, borderRadius: BorderRadius.circular(6)),
      child: Text(festivalTags[theme.key] ?? '', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: theme.ink, fontSize: 9, fontWeight: FontWeight.w900)),
    );
  }
}

/// The Home greeting chip, in Marathi.
class FestivalGreetingChip extends StatelessWidget {
  const FestivalGreetingChip(this.theme, {super.key});
  final FestivalTheme theme;

  @override
  Widget build(BuildContext context) {
    final text = festivalGreetings[theme.key] ?? '';
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Align(
        alignment: Alignment.centerLeft,
        child: Semantics(
          label: text,
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(color: theme.bg, borderRadius: BorderRadius.circular(20)),
            child: ExcludeSemantics(child: Text(text, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: theme.ink, fontWeight: FontWeight.w800, fontSize: 13))),
          ),
        ),
      ),
    );
  }
}
