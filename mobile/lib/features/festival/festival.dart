import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/prefs.dart';

/// What falls over the Home screen during a festival.
enum FestivalParticle {
  /// Small golden four-point sparkles (lamps and lights).
  sparkle,

  /// Orange and yellow marigold petals.
  petal,

  /// Small coloured paper pieces.
  confetti,
}

/// A festival the app celebrates on Home, between [start] and [end] (both days included).
///
/// The titles are English UI strings (translated with `context.tr`); every one of them needs a Marathi translation.
class Festival {
  const Festival({
    required this.id,
    required this.title,
    required this.subtitle,
    required this.emoji,
    required this.start,
    required this.end,
    required this.colors,
    required this.onColor,
    required this.particle,
    required this.particleColors,
  });

  final String id, title, subtitle, emoji;
  final DateTime start, end;

  /// Banner gradient, and the text colour that reads on it.
  final List<Color> colors;
  final Color onColor;
  final FestivalParticle particle;
  final List<Color> particleColors;

  bool includes(DateTime day) {
    final d = DateTime(day.year, day.month, day.day);
    return !d.isBefore(start) && !d.isAfter(end);
  }
}

const _festiveSubtitle = 'Fresh groceries for your festive table';

/// The festivals, in date order. Dates are inclusive. Festivals that follow the moon (Navratri, Diwali, Holi) move each year:
/// check them against the calendar when adding the next year. Change this list, and nothing else, to add or move a festival.
final festivals = <Festival>[
  Festival(
    id: 'navratri-dussehra-2026',
    title: 'Happy Navratri & Dussehra!',
    subtitle: _festiveSubtitle,
    emoji: '🌼',
    start: DateTime(2026, 10, 09),
    end: DateTime(2026, 10, 21),
    colors: const [Color(0xFFE91E63), Color(0xFFFF8F00)],
    onColor: Colors.white,
    particle: FestivalParticle.petal,
    particleColors: const [
      Color(0xFFFF9800),
      Color(0xFFFFC107),
      Color(0xFFFFB300),
      Color(0xFFFF6F00),
    ],
  ),
  Festival(
    id: 'diwali-2026',
    title: 'Happy Diwali!',
    subtitle: _festiveSubtitle,
    emoji: '🪔',
    start: DateTime(2026, 11, 1),
    end: DateTime(2026, 11, 12),
    colors: const [Color(0xFFFF6F00), Color(0xFFFFC400)],
    onColor: Color(0xFF14111E),
    particle: FestivalParticle.sparkle,
    particleColors: const [
      Color(0xFFFFD54F),
      Color(0xFFFFE082),
      Color(0xFFFFB300),
      Colors.white,
    ],
  ),
  Festival(
    id: 'christmas-2026',
    title: 'Merry Christmas!',
    subtitle: _festiveSubtitle,
    emoji: '🎄',
    start: DateTime(2026, 12, 20),
    end: DateTime(2026, 12, 26),
    colors: const [Color(0xFFC62828), Color(0xFF1B5E20)],
    onColor: Colors.white,
    particle: FestivalParticle.confetti,
    particleColors: const [
      Color(0xFFEF5350),
      Color(0xFF66BB6A),
      Colors.white,
      Color(0xFFFFD54F),
    ],
  ),
  Festival(
    id: 'new-year-2027',
    title: 'Happy New Year!',
    subtitle: _festiveSubtitle,
    emoji: '🎆',
    start: DateTime(2026, 12, 29),
    end: DateTime(2027, 1, 2),
    colors: const [Color(0xFF283593), Color(0xFF7E57C2)],
    onColor: Colors.white,
    particle: FestivalParticle.sparkle,
    particleColors: const [
      Color(0xFFFFD54F),
      Colors.white,
      Color(0xFF80DEEA),
      Color(0xFFF48FB1),
    ],
  ),
  Festival(
    id: 'sankranti-2027',
    title: 'Happy Makar Sankranti!',
    subtitle: _festiveSubtitle,
    emoji: '🪁',
    start: DateTime(2027, 1, 12),
    end: DateTime(2027, 1, 16),
    colors: const [Color(0xFFFFCA28), Color(0xFFFF7043)],
    onColor: Color(0xFF14111E),
    particle: FestivalParticle.confetti,
    particleColors: const [
      Color(0xFFE53935),
      Color(0xFF1E88E5),
      Color(0xFF43A047),
      Color(0xFFFFFFFF),
    ],
  ),
  Festival(
    id: 'republic-day-2027',
    title: 'Happy Republic Day!',
    subtitle: _festiveSubtitle,
    emoji: '🇮🇳',
    start: DateTime(2027, 1, 25),
    end: DateTime(2027, 1, 26),
    colors: const [Color(0xFFFF9933), Color(0xFF138808)],
    onColor: Colors.white,
    particle: FestivalParticle.confetti,
    particleColors: const [
      Color(0xFFFF9933),
      Colors.white,
      Color(0xFF138808),
      Color(0xFF1E3A8A),
    ],
  ),
  Festival(
    id: 'holi-2027',
    title: 'Happy Holi!',
    subtitle: _festiveSubtitle,
    emoji: '🎨',
    start: DateTime(2027, 3, 20),
    end: DateTime(2027, 3, 23),
    colors: const [Color(0xFFEC407A), Color(0xFF7E57C2)],
    onColor: Colors.white,
    particle: FestivalParticle.confetti,
    particleColors: const [
      Color(0xFFEC407A),
      Color(0xFFFFEB3B),
      Color(0xFF29B6F6),
      Color(0xFF66BB6A),
      Color(0xFFAB47BC),
    ],
  ),
];

/// The festival on [day], or null outside every festival.
Festival? festivalOn(DateTime day, [List<Festival>? calendar]) {
  for (final f in calendar ?? festivals) {
    if (f.includes(day)) return f;
  }
  return null;
}

/// Where "now" comes from for festivals. The test setup (`test/flutter_test_config.dart`) points it at a day with no festival, so the
/// falling animation (which never stops) cannot keep an unrelated test from settling; a test that wants a festival overrides
/// [festivalClockProvider].
DateTime Function() festivalNow = DateTime.now;

/// Where "now" comes from; tests replace it.
final festivalClockProvider = Provider<DateTime Function()>(
  (ref) => festivalNow,
);

/// Today's festival, or null.
final activeFestivalProvider = Provider<Festival?>(
  (ref) => festivalOn(ref.watch(festivalClockProvider)()),
);

/// The customer's choice to see festival animations (a switch in Profile). The greeting banner stays either way; only motion stops.
class FestivalAnimationNotifier extends Notifier<bool> {
  static const _key = 'festival_animation';

  @override
  bool build() => ref.read(sharedPrefsProvider).getBool(_key) ?? true;

  void set(bool on) {
    state = on;
    ref.read(sharedPrefsProvider).setBool(_key, on);
    // Turning it back on is asking to see it: the 30 seconds start again.
    if (on) ref.read(festivalMotionProvider.notifier).replay();
  }
}

final festivalAnimationProvider =
    NotifierProvider<FestivalAnimationNotifier, bool>(
      FestivalAnimationNotifier.new,
    );


/// How long the falling and the breathing last, counted from when Home first shows them since the app was opened.
/// After that only the greeting banner stays. Tests change it with [festivalMotionDurationProvider].
const festivalMotionDuration = Duration(seconds: 30);

final festivalMotionDurationProvider = Provider<Duration>((ref) => festivalMotionDuration);

/// True while the festival motion is still within its 30 seconds. It starts counting the first time it is read, which is when a festival
/// is on and Home shows the banner.
class FestivalMotionNotifier extends Notifier<bool> {
  Timer? _timer;

  @override
  bool build() {
    ref.onDispose(() => _timer?.cancel());
    _start();
    return true;
  }

  void _start() {
    _timer?.cancel();
    _timer = Timer(ref.read(festivalMotionDurationProvider), () {
      if (ref.mounted) state = false;
    });
  }

  /// Another 30 seconds (the customer switched the animation back on).
  void replay() {
    state = true;
    _start();
  }
}

final festivalMotionProvider = NotifierProvider<FestivalMotionNotifier, bool>(FestivalMotionNotifier.new);
