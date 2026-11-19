import 'dart:async';
import 'dart:math';

import '../data/api/api_client.dart';

/// The steps the app records. They match the server's list; an unknown name is refused there.
abstract final class Track {
  static const appOpen = 'app_open';
  static const search = 'search';
  static const productView = 'product_view';
  static const addToCart = 'add_to_cart';
  static const cartOpen = 'cart_open';
  static const checkoutStart = 'checkout_start';
  static const couponApplied = 'coupon_applied';
  static const couponRefused = 'coupon_refused';
  static const slotChosen = 'slot_chosen';
  static const orderPlaced = 'order_placed';
  static const storeChosen = 'store_chosen';
  static const outOfServiceArea = 'out_of_service_area';
}

class AnalyticsEvent {
  const AnalyticsEvent(this.name, {this.detail, this.storeId});
  final String name;
  final String? detail;
  final String? storeId;

  Map<String, dynamic> toJson() => {'name': name, if (detail != null) 'detail': detail, if (storeId != null) 'storeId': storeId};
}

/// Where batches of steps go. Tests replace it; failures are the caller's to ignore.
abstract interface class AnalyticsSink {
  Future<void> send(String sessionId, List<AnalyticsEvent> events);
}

/// `POST api/analytics/events`: anonymous, nothing but the random session id identifies the sender.
class ApiAnalyticsSink implements AnalyticsSink {
  ApiAnalyticsSink(this._api);
  final ApiClient _api;

  @override
  Future<void> send(String sessionId, List<AnalyticsEvent> events) =>
      _api.post<void>('/api/analytics/events', body: {'sessionId': sessionId, 'events': [for (final e in events) e.toJson()]}, parse: (_) {});
}

/// Offline demo data records nothing.
class NoAnalyticsSink implements AnalyticsSink {
  @override
  Future<void> send(String sessionId, List<AnalyticsEvent> events) async {}
}

/// Records what customers do, for the store's and the business's own funnels, without knowing who they are: events carry a random session
/// id made each time the app starts, never an account, phone, email or address. Steps are queued and sent in small batches (every 20, or after
/// 15 seconds, or when the app goes to the background). Sending is best effort: a failure drops the batch, and nothing here ever throws or
/// waits for the network, so it cannot slow or break a screen.
class Analytics {
  Analytics(this._sink, {this.storeId, String? sessionId, this.batchSize = 20, this.flushAfter = const Duration(seconds: 15), this.queueLimit = 100})
      : sessionId = sessionId ?? _newSessionId();

  final AnalyticsSink _sink;

  /// The store the customer is shopping in right now, added to each step that does not name one.
  final String? Function()? storeId;
  final String sessionId;
  final int batchSize, queueLimit;
  final Duration flushAfter;
  final _queue = <AnalyticsEvent>[];
  Timer? _timer;
  var _sending = false;

  static String _newSessionId() {
    final random = Random.secure();
    return List.generate(24, (_) => random.nextInt(16).toRadixString(16)).join();
  }

  /// A search term the server could accept: 2 to 40 characters, nothing that looks like an email or a phone number.
  static String? safeTerm(String? text) {
    final t = text?.trim().toLowerCase().replaceAll(RegExp(r'\s+'), ' ');
    if (t == null || t.length < 2 || t.contains('@') || t.replaceAll(RegExp(r'\D'), '').length >= 7) return null;
    return t.length > 40 ? t.substring(0, 40).trimRight() : t;
  }

  /// Notes a step. Returns at once.
  void track(String name, {String? detail, String? store}) {
    try {
      if (_queue.length >= queueLimit) _queue.removeAt(0);
      _queue.add(AnalyticsEvent(name, detail: detail, storeId: store ?? storeId?.call()));
      if (_queue.length >= batchSize) {
        unawaited(flush());
      } else {
        _timer ??= Timer(flushAfter, () {
          _timer = null;
          unawaited(flush());
        });
      }
    } catch (_) {
      // Recording must never be the reason something fails.
    }
  }

  /// Sends what is queued (up to 25 at a time). Safe to call any time.
  Future<void> flush() async {
    _timer?.cancel();
    _timer = null;
    if (_sending || _queue.isEmpty) return;
    _sending = true;
    try {
      while (_queue.isNotEmpty) {
        final batch = _queue.take(25).toList();
        _queue.removeRange(0, batch.length);
        try {
          await _sink.send(sessionId, batch);
        } catch (_) {
          // Best effort: a lost batch is only a lost count.
        }
      }
    } finally {
      _sending = false;
    }
  }

  void dispose() {
    _timer?.cancel();
    _timer = null;
  }
}
