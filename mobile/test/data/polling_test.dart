import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/polling.dart';

class _Harness extends StatefulWidget {
  const _Harness({super.key, required this.onTick, this.active = true, this.interval = const Duration(seconds: 10)});
  final Future<void> Function() onTick;
  final bool active;
  final Duration interval;

  @override
  State<_Harness> createState() => _HarnessState();
}

class _HarnessState extends State<_Harness> {
  late bool active = widget.active;

  void setActive(bool value) => setState(() => active = value);

  @override
  Widget build(BuildContext context) => Directionality(
        textDirection: TextDirection.ltr,
        child: Polling(interval: widget.interval, active: active, onTick: widget.onTick, child: const SizedBox()),
      );
}

Future<void> _wait(WidgetTester tester, int seconds) => tester.pump(Duration(seconds: seconds));

void main() {
  testWidgets('it checks once per interval while active, and not before the first interval has passed', (tester) async {
    var ticks = 0;
    await tester.pumpWidget(_Harness(onTick: () async => ticks++));

    await _wait(tester, 9);
    expect(ticks, 0);
    await _wait(tester, 1);
    expect(ticks, 1);
    await _wait(tester, 10);
    await _wait(tester, 10);
    expect(ticks, 3);
  });

  testWidgets('it does nothing while not active, starts when it becomes active and stops when it stops being active', (tester) async {
    var ticks = 0;
    final key = GlobalKey<_HarnessState>();
    await tester.pumpWidget(_Harness(key: key, active: false, onTick: () async => ticks++));
    await _wait(tester, 60);
    expect(ticks, 0);

    key.currentState!.setActive(true);
    await tester.pump();
    await _wait(tester, 10);
    expect(ticks, 1);

    key.currentState!.setActive(false);
    await tester.pump();
    await _wait(tester, 60);
    expect(ticks, 1, reason: 'stopped');
  });

  testWidgets('after a failure it waits twice as long, then four times, then no longer than the limit, and a success brings it back', (tester) async {
    final times = <int>[];
    var elapsed = 0;
    var fail = true;
    await tester.pumpWidget(_Harness(onTick: () async {
      times.add(elapsed);
      if (fail) throw Exception('offline');
    }));

    for (var second = 1; second <= 200; second++) {
      elapsed = second;
      await _wait(tester, 1);
      if (second == 100) fail = false;
    }

    // 10 s, then 20 s, then 40 s, then the limit of 4 x 10 = 40 s, until it works; then every 10 s again.
    expect(times.first, 10);
    final gaps = [for (var i = 1; i < times.length; i++) times[i] - times[i - 1]];
    expect(gaps.take(3), [20, 40, 40]);
    expect(gaps.last, 10, reason: 'back to normal after a success');
  });

  testWidgets('two checks never run at the same time: the next waits for the last to finish', (tester) async {
    var running = 0;
    var maxRunning = 0;
    var starts = 0;
    await tester.pumpWidget(_Harness(
      interval: const Duration(seconds: 1),
      onTick: () async {
        starts++;
        running++;
        if (running > maxRunning) maxRunning = running;
        await Future<void>.delayed(const Duration(seconds: 5));
        running--;
      },
    ));

    for (var i = 0; i < 30; i++) {
      await _wait(tester, 1);
    }

    expect(maxRunning, 1);
    expect(starts, greaterThan(1));
  });

  testWidgets('in the background it stops, and coming back runs a check at once', (tester) async {
    var ticks = 0;
    await tester.pumpWidget(_Harness(onTick: () async => ticks++));
    await _wait(tester, 10);
    expect(ticks, 1);

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await _wait(tester, 120);
    expect(ticks, 1, reason: 'nothing while the app is not in front');

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pump(const Duration(milliseconds: 1));
    expect(ticks, 2, reason: 'a check as soon as the customer is back');
    await _wait(tester, 10);
    expect(ticks, 3);
  });

  testWidgets('removing it stops it for good, even in the middle of a check', (tester) async {
    var ticks = 0;
    await tester.pumpWidget(_Harness(onTick: () async {
      ticks++;
      await Future<void>.delayed(const Duration(seconds: 3));
    }));
    await _wait(tester, 10);
    expect(ticks, 1);

    await tester.pumpWidget(const SizedBox());
    await _wait(tester, 120);

    expect(ticks, 1);
    expect(tester.takeException(), isNull);
  });

  testWidgets('changing the interval takes effect and starts counting again', (tester) async {
    var ticks = 0;
    await tester.pumpWidget(_Harness(onTick: () async => ticks++));
    await _wait(tester, 5);

    await tester.pumpWidget(_Harness(interval: const Duration(seconds: 2), onTick: () async => ticks++));
    await _wait(tester, 2);

    expect(ticks, 1);
  });
}
