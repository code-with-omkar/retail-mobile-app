import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../core/theme.dart';

/// A few words across the top corner of a coupon or an offer card. Shows [child] alone when there is no ribbon.
class Ribbon extends StatelessWidget {
  const Ribbon(this.text, {super.key, required this.child});
  final String? text;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final words = text?.trim() ?? '';
    if (words.isEmpty) return child;
    return Stack(
      fit: StackFit.passthrough,
      children: [
        child,
        Positioned(
          top: 0,
          right: 18,
          child: Container(
            padding: const EdgeInsets.fromLTRB(10, 3, 10, 5),
            decoration: const BoxDecoration(
              color: Color(0xFFC42F1D),
              border: Border(left: BorderSide(color: Pal.yellow, width: 2), right: BorderSide(color: Pal.yellow, width: 2), bottom: BorderSide(color: Pal.yellow, width: 2)),
              borderRadius: BorderRadius.vertical(bottom: Radius.circular(8)),
            ),
            child: Text(words.toUpperCase(), style: const TextStyle(color: Colors.white, fontSize: 10, fontWeight: FontWeight.w900, letterSpacing: .6)),
          ),
        ),
      ],
    );
  }
}

/// The animations this app can play, by the key the server stores. An unknown key (a newer one) plays nothing.
const celebrationKeys = {'confetti', 'sparkle', 'gift', 'balloons'};

/// Plays an animation once over the whole screen, without blocking touches. Nothing happens for no or an unknown key, or when the phone
/// asks for animations to be removed.
void playCelebration(BuildContext context, String? key) {
  if (key == null || !celebrationKeys.contains(key)) return;
  if (MediaQuery.maybeDisableAnimationsOf(context) ?? false) return;
  final overlay = Overlay.maybeOf(context, rootOverlay: true);
  if (overlay == null) return;
  late final OverlayEntry entry;
  entry = OverlayEntry(
    builder: (_) => Celebration(
      key,
      onDone: () {
        entry.remove();
        entry.dispose();
      },
    ),
  );
  overlay.insert(entry);
}

/// Plays [kind] when this first appears, such as when an offer is opened.
class CelebrateOnce extends StatefulWidget {
  const CelebrateOnce(this.kind, {super.key, required this.child});
  final String? kind;
  final Widget child;

  @override
  State<CelebrateOnce> createState() => _CelebrateOnceState();
}

class _CelebrateOnceState extends State<CelebrateOnce> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) playCelebration(context, widget.kind);
    });
  }

  @override
  Widget build(BuildContext context) => widget.child;
}

class Celebration extends StatefulWidget {
  const Celebration(this.kind, {super.key, required this.onDone});
  final String kind;
  final VoidCallback onDone;

  @override
  State<Celebration> createState() => _CelebrationState();
}

class _CelebrationState extends State<Celebration> with SingleTickerProviderStateMixin {
  late final AnimationController _time = AnimationController(vsync: this, duration: const Duration(milliseconds: 2200));

  @override
  void initState() {
    super.initState();
    _time.forward().whenComplete(() {
      if (mounted) widget.onDone();
    });
  }

  @override
  void dispose() {
    _time.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Positioned.fill(
        child: IgnorePointer(
          child: ExcludeSemantics(child: CustomPaint(painter: CelebrationPainter(widget.kind, _time))),
        ),
      );
}

class CelebrationPainter extends CustomPainter {
  CelebrationPainter(this.kind, this.time) : super(repaint: time);
  final String kind;
  final Animation<double> time;

  static const _colors = [Pal.yellow, Color(0xFFC42F1D), Color(0xFF14A44D), Color(0xFF3E7BFA), Color(0xFFFF6FAE)];

  @override
  void paint(Canvas canvas, Size size) {
    final random = math.Random(7); // the same pieces on every frame
    final count = kind == 'balloons' || kind == 'gift' ? 18 : 60;
    for (var i = 0; i < count; i++) {
      final x = random.nextDouble() * size.width;
      final delay = random.nextDouble() * .35;
      final speed = .8 + random.nextDouble() * .5;
      final spin = (random.nextDouble() - .5) * 10;
      final t = ((time.value - delay) / (1 - delay)).clamp(0.0, 1.0);
      if (t <= 0) continue;
      // Balloons rise from the bottom; everything else falls from the top.
      final travel = t * speed * (size.height + 80);
      final y = kind == 'balloons' ? size.height + 40 - travel : travel - 40;
      final fade = t > .8 ? (1 - t) / .2 : 1.0;
      final paint = Paint()..color = _colors[i % _colors.length].withValues(alpha: fade.clamp(0.0, 1.0));
      canvas.save();
      canvas.translate(x + math.sin(t * 8 + i) * 14, y);
      switch (kind) {
        case 'confetti':
          canvas.rotate(spin * t);
          canvas.drawRect(const Rect.fromLTWH(-4, -7, 8, 14), paint);
        case 'sparkle':
          canvas.rotate(spin * t * .3);
          final r = 5.0 + random.nextDouble() * 8;
          canvas.drawPath(
            Path()
              ..moveTo(0, -r)
              ..quadraticBezierTo(0, 0, r, 0)
              ..quadraticBezierTo(0, 0, 0, r)
              ..quadraticBezierTo(0, 0, -r, 0)
              ..quadraticBezierTo(0, 0, 0, -r),
            paint..color = (i.isEven ? Pal.yellow : const Color(0xFFFFB02E)).withValues(alpha: fade.clamp(0.0, 1.0)),
          );
        case 'gift':
          canvas.rotate(math.sin(t * 6 + i) * .4);
          canvas.drawRRect(RRect.fromRectAndRadius(const Rect.fromLTWH(-13, -10, 26, 22), const Radius.circular(4)), paint);
          final bow = Paint()..color = Colors.white.withValues(alpha: fade.clamp(0.0, 1.0));
          canvas.drawRect(const Rect.fromLTWH(-2.5, -10, 5, 22), bow);
          canvas.drawRect(const Rect.fromLTWH(-13, -3, 26, 4), bow);
          canvas.drawCircle(const Offset(-5, -13), 4, bow);
          canvas.drawCircle(const Offset(5, -13), 4, bow);
        case 'balloons':
          canvas.drawOval(const Rect.fromLTWH(-15, -20, 30, 38), paint);
          canvas.drawLine(
            const Offset(0, 18),
            const Offset(3, 42),
            Paint()
              ..color = const Color(0xFF8A8694).withValues(alpha: fade.clamp(0.0, 1.0))
              ..strokeWidth = 1.2,
          );
      }
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(CelebrationPainter old) => old.kind != kind;
}
