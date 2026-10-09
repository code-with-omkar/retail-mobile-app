import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import 'festival.dart';

/// Whether festival motion is allowed: the customer has not switched it off and the phone is not set to reduce motion.
bool festivalMotionAllowed(BuildContext context, WidgetRef ref) => ref.watch(festivalAnimationProvider) && !MediaQuery.of(context).disableAnimations;

/// Whether the motion runs right now: allowed, and still within its 30 seconds.
bool festivalMotionOn(BuildContext context, WidgetRef ref) => festivalMotionAllowed(context, ref) && ref.watch(festivalMotionProvider);

/// The festival greeting under the Home header. Nothing outside festival dates.
class FestivalBanner extends ConsumerStatefulWidget {
  const FestivalBanner({super.key});

  @override
  ConsumerState<FestivalBanner> createState() => _FestivalBannerState();
}

class _FestivalBannerState extends ConsumerState<FestivalBanner> with SingleTickerProviderStateMixin {
  late final _pulse = AnimationController(vsync: this, duration: const Duration(milliseconds: 1800));

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final festival = ref.watch(activeFestivalProvider);
    if (festival == null) return const SizedBox.shrink();
    final motion = festivalMotionOn(context, ref);
    if (motion && !_pulse.isAnimating) {
      _pulse.repeat(reverse: true);
    } else if (!motion && _pulse.isAnimating) {
      // Settle back to normal size rather than freezing mid-breath.
      _pulse.animateTo(0, duration: const Duration(milliseconds: 400));
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: Semantics(
      label: context.tr(festival.title),
      child: Container(
        padding: const EdgeInsets.fromLTRB(18, 14, 14, 14),
        decoration: BoxDecoration(
          gradient: LinearGradient(colors: festival.colors, begin: Alignment.centerLeft, end: Alignment.centerRight),
          borderRadius: BorderRadius.circular(QC.rCard),
          boxShadow: [BoxShadow(color: festival.colors.first.withValues(alpha: .35), blurRadius: 18, offset: const Offset(0, 6))],
        ),
        child: Row(children: [
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(context.tr(festival.title), style: TextStyle(color: festival.onColor, fontWeight: FontWeight.w900, fontSize: 20, height: 1.15)),
              const SizedBox(height: 4),
              Text(context.tr(festival.subtitle), style: TextStyle(color: festival.onColor.withValues(alpha: .9), fontWeight: FontWeight.w600, fontSize: 13)),
            ]),
          ),
          const SizedBox(width: 10),
          // The emoji breathes slowly, like a lamp flickering.
          AnimatedBuilder(
            animation: _pulse,
            builder: (_, child) => Transform.scale(scale: 1 + .12 * _pulse.value, child: child),
            child: ExcludeSemantics(child: Text(festival.emoji, style: const TextStyle(fontSize: 44))),
          ),
        ]),
      ),
      ),
    );
  }
}

/// A few slow falling sparkles, petals or confetti pieces over the whole screen. Never takes a tap, and stops with the switch in Profile
/// or the phone's reduce-motion setting.
class FestivalParticles extends ConsumerStatefulWidget {
  const FestivalParticles({super.key});

  @override
  ConsumerState<FestivalParticles> createState() => _FestivalParticlesState();
}

class _FestivalParticlesState extends ConsumerState<FestivalParticles> with SingleTickerProviderStateMixin {
  late final _clock = AnimationController(vsync: this, duration: const Duration(seconds: 16));
  late final List<_Piece> _pieces = _makePieces();

  static List<_Piece> _makePieces() {
    final random = math.Random(2026); // fixed, so the pieces are the same on every run
    return List.generate(
      22,
      (_) => _Piece(
        x: random.nextDouble(),
        phase: random.nextDouble(),
        speed: 1 + random.nextInt(3),
        size: 5 + random.nextDouble() * 7,
        sway: 8 + random.nextDouble() * 18,
        swayPhase: random.nextDouble() * math.pi * 2,
        spin: (random.nextDouble() - .5) * 6,
        color: random.nextInt(1 << 20),
      ),
    );
  }

  @override
  void dispose() {
    _clock.dispose();
    super.dispose();
  }

  /// The falling has faded out after its 30 seconds: nothing is drawn and nothing redraws.
  var _finished = false;

  @override
  Widget build(BuildContext context) {
    final festival = ref.watch(activeFestivalProvider);
    final allowed = festival != null && festivalMotionAllowed(context, ref);
    final within = allowed && ref.watch(festivalMotionProvider);
    if (!allowed) {
      // Nothing to draw: do not keep the screen redrawing.
      if (_clock.isAnimating) _clock.stop();
      _finished = false;
      return const SizedBox.shrink();
    }
    if (within) _finished = false; // switched back on: it falls again
    if (_finished) return const SizedBox.shrink();
    if (!_clock.isAnimating) _clock.repeat();
    // After its 30 seconds it fades out gently instead of vanishing, then the clock stops.
    return IgnorePointer(
      child: ExcludeSemantics(
        child: AnimatedOpacity(
          opacity: within ? 1 : 0,
          duration: const Duration(milliseconds: 1500),
          onEnd: () {
            if (!within && mounted) {
              _clock.stop();
              setState(() => _finished = true);
            }
          },
          child: RepaintBoundary(
            child: AnimatedBuilder(
              animation: _clock,
              builder: (_, _) => CustomPaint(painter: _ParticlePainter(_pieces, festival.particle, festival.particleColors, _clock.value), size: Size.infinite),
            ),
          ),
        ),
      ),
    );
  }
}

class _Piece {
  const _Piece({required this.x, required this.phase, required this.speed, required this.size, required this.sway, required this.swayPhase, required this.spin, required this.color});
  final double x, phase, size, sway, swayPhase, spin;

  /// Whole laps of the screen per cycle, so the loop is seamless.
  final int speed;
  final int color;
}

class _ParticlePainter extends CustomPainter {
  _ParticlePainter(this.pieces, this.kind, this.colors, this.t);
  final List<_Piece> pieces;
  final FestivalParticle kind;
  final List<Color> colors;
  final double t;

  @override
  void paint(Canvas canvas, Size size) {
    for (final p in pieces) {
      final fall = (p.phase + t * p.speed) % 1;
      final y = -20 + fall * (size.height + 40);
      final x = p.x * size.width + math.sin(t * math.pi * 2 * p.speed + p.swayPhase) * p.sway;
      // Fade in at the top and out at the bottom so nothing pops.
      final fade = math.min(1.0, math.min(fall * 8, (1 - fall) * 8));
      final color = colors[p.color % colors.length].withValues(alpha: .85 * fade);
      final paint = Paint()..color = color;
      canvas.save();
      canvas.translate(x, y);
      canvas.rotate(t * math.pi * 2 * p.spin);
      switch (kind) {
        case FestivalParticle.sparkle:
          // Twinkle: the star grows and shrinks as it falls.
          final s = p.size * (.6 + .4 * math.sin(t * math.pi * 12 + p.swayPhase).abs());
          canvas.drawPath(_star(s), paint);
        case FestivalParticle.petal:
          canvas.drawOval(Rect.fromCenter(center: Offset.zero, width: p.size * 1.7, height: p.size * .9), paint);
        case FestivalParticle.confetti:
          canvas.drawRRect(RRect.fromRectAndRadius(Rect.fromCenter(center: Offset.zero, width: p.size, height: p.size * .5), const Radius.circular(1.5)), paint);
      }
      canvas.restore();
    }
  }

  /// A four-point star with radius [r].
  Path _star(double r) {
    final path = Path();
    for (var i = 0; i < 8; i++) {
      final radius = i.isEven ? r : r * .32;
      final angle = i * math.pi / 4;
      final point = Offset(math.cos(angle) * radius, math.sin(angle) * radius);
      i == 0 ? path.moveTo(point.dx, point.dy) : path.lineTo(point.dx, point.dy);
    }
    return path..close();
  }

  @override
  bool shouldRepaint(_ParticlePainter old) => old.t != t || old.kind != kind;
}
