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

/// The festival greeting under the Home header: a large card with a floating emoji, a slow light sweep and a few twinkles.
/// Nothing outside festival dates. The motion stops after 30 seconds, with the switch in Profile, or with the phone's reduce-motion setting.
class FestivalBanner extends ConsumerStatefulWidget {
  const FestivalBanner({super.key});

  @override
  ConsumerState<FestivalBanner> createState() => _FestivalBannerState();
}

class _FestivalBannerState extends ConsumerState<FestivalBanner> with TickerProviderStateMixin {
  /// Breathing and floating of the emoji (back and forth).
  late final _pulse = AnimationController(vsync: this, duration: const Duration(milliseconds: 2200));

  /// The light sweeping across the card (one way, then again).
  late final _sweep = AnimationController(vsync: this, duration: const Duration(milliseconds: 3200));

  @override
  void dispose() {
    _pulse.dispose();
    _sweep.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final festival = ref.watch(activeFestivalProvider);
    if (festival == null) return const SizedBox.shrink();
    final motion = festivalMotionOn(context, ref);
    if (motion && !_pulse.isAnimating) {
      _pulse.repeat(reverse: true);
      _sweep.repeat();
    } else if (!motion && _pulse.isAnimating) {
      // Settle back to rest rather than freezing mid-motion.
      _pulse.animateTo(0, duration: const Duration(milliseconds: 400));
      _sweep.stop();
      _sweep.value = 0;
    }
    final on = festival.onColor;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Semantics(
        label: '${context.tr(festival.title)}. ${context.tr(festival.subtitle)}',
        child: Container(
          height: 150,
          decoration: BoxDecoration(
            gradient: LinearGradient(colors: festival.colors, begin: Alignment.topLeft, end: Alignment.bottomRight),
            borderRadius: BorderRadius.circular(QC.rCard + 4),
            boxShadow: [BoxShadow(color: festival.colors.first.withValues(alpha: .40), blurRadius: 22, offset: const Offset(0, 8))],
          ),
          child: ClipRRect(
            borderRadius: BorderRadius.circular(QC.rCard + 4),
            child: LayoutBuilder(builder: (context, box) {
              final width = box.maxWidth;
              return Stack(clipBehavior: Clip.none, children: [
                // Soft round shapes for depth.
                Positioned(right: -40, top: -50, child: _Blob(size: 170, color: Colors.white.withValues(alpha: .13))),
                Positioned(right: 70, bottom: -60, child: _Blob(size: 120, color: Colors.white.withValues(alpha: .09))),
                // The light sweeping across.
                Positioned.fill(
                  child: IgnorePointer(
                    child: AnimatedBuilder(
                      animation: _sweep,
                      builder: (_, _) {
                        final x = -140 + (width + 280) * _sweep.value;
                        return Transform.translate(
                          offset: Offset(x, 0),
                          child: Align(
                            alignment: Alignment.centerLeft,
                            child: Transform.rotate(
                              angle: -.35,
                              child: Container(
                                width: 70,
                                height: 260,
                                decoration: BoxDecoration(
                                  gradient: LinearGradient(colors: [Colors.white.withValues(alpha: 0), Colors.white.withValues(alpha: .22), Colors.white.withValues(alpha: 0)]),
                                ),
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                ),
                // A few twinkles.
                for (final (i, spot) in const [(0.52, 0.18), (0.70, 0.62), (0.40, 0.78), (0.86, 0.14)].indexed)
                  Positioned(
                    left: width * spot.$1,
                    top: 150 * spot.$2,
                    child: AnimatedBuilder(
                      animation: _pulse,
                      builder: (_, _) {
                        final phase = (_pulse.value + i * .27) % 1.0;
                        return Opacity(opacity: .25 + .6 * (1 - (phase - .5).abs() * 2), child: Icon(Icons.auto_awesome, size: 10.0 + 4 * (i % 2), color: Colors.white));
                      },
                    ),
                  ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(18, 16, 16, 16),
                  child: Row(children: [
                    Expanded(
                      child: Column(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(context.tr(festival.title), maxLines: 3, overflow: TextOverflow.ellipsis, style: TextStyle(color: on, fontWeight: FontWeight.w900, fontSize: 24, height: 1.1)),
                        const SizedBox(height: 6),
                        Text(context.tr(festival.subtitle), maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: on.withValues(alpha: .92), fontWeight: FontWeight.w700, fontSize: 13.5, height: 1.25)),
                      ]),
                    ),
                    const SizedBox(width: 8),
                    // The emoji floats, tilts and breathes.
                    AnimatedBuilder(
                      animation: _pulse,
                      builder: (_, child) {
                        final v = Curves.easeInOut.transform(_pulse.value);
                        return Transform.translate(
                          offset: Offset(0, -8 * v),
                          child: Transform.rotate(angle: (v - .5) * .16, child: Transform.scale(scale: 1 + .10 * v, child: child)),
                        );
                      },
                      child: ExcludeSemantics(child: Text(festival.emoji, style: const TextStyle(fontSize: 74))),
                    ),
                  ]),
                ),
              ]);
            }),
          ),
        ),
      ),
    );
  }
}

class _Blob extends StatelessWidget {
  const _Blob({required this.size, required this.color});
  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(width: size, height: size, decoration: BoxDecoration(color: color, shape: BoxShape.circle));
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
