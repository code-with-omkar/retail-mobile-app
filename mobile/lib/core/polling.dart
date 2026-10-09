import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/widgets.dart';

/// Runs [onTick] again and again while it is [active] and the app is in front, and does nothing otherwise.
///
/// The next run is [interval] after the last one finished (never two at once). When [onTick] throws, the wait doubles each time up to
/// [maxBackoff] times the interval, and goes back to normal after the next success. Coming back to the app runs it at once.
/// It is meant for screens that must notice a change made elsewhere (an order the shop has accepted) without the customer pulling to refresh.
class Polling extends StatefulWidget {
  const Polling({super.key, required this.interval, required this.onTick, required this.child, this.active = true, this.maxBackoff = 4});

  final Duration interval;

  /// One check. Throw when it failed, so the polling slows down.
  final Future<void> Function() onTick;
  final bool active;
  final int maxBackoff;
  final Widget child;

  @override
  State<Polling> createState() => _PollingState();
}

class _PollingState extends State<Polling> with WidgetsBindingObserver {
  Timer? _timer;
  var _failures = 0;
  var _running = false;
  var _inFront = true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _schedule();
  }

  @override
  void didUpdateWidget(Polling old) {
    super.didUpdateWidget(old);
    if (old.active != widget.active || old.interval != widget.interval) {
      _failures = 0;
      _schedule();
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final front = state == AppLifecycleState.resumed;
    if (front == _inFront) return;
    _inFront = front;
    _schedule(now: front);
  }

  /// The interval, doubled for each failure in a row, up to maxBackoff times the interval.
  Duration get _wait => widget.interval * math.min(1 << math.min(_failures, 10), widget.maxBackoff);

  void _schedule({bool now = false}) {
    _timer?.cancel();
    _timer = null;
    if (!widget.active || !_inFront) return;
    _timer = Timer(now ? Duration.zero : _wait, _fire);
  }

  Future<void> _fire() async {
    if (_running) return;
    _running = true;
    try {
      await widget.onTick();
      _failures = 0;
    } catch (_) {
      _failures++;
    } finally {
      _running = false;
    }
    if (mounted) _schedule();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
