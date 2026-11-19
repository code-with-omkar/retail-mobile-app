import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// One window a customer can choose (`GET api/stores/{id}/delivery-slots`). Times are UTC; [day] is the India date (yyyy-MM-dd) to group by.
class DeliverySlot {
  const DeliverySlot({required this.start, required this.end, required this.day, required this.dayLabel, required this.timeLabel, required this.available, required this.left});
  final DateTime start, end;
  final String day, dayLabel, timeLabel;
  final bool available;
  final int left;

  factory DeliverySlot.fromJson(Map<String, dynamic> j) => DeliverySlot(
        start: DateTime.parse(j['startUtc'] as String).toUtc(),
        end: DateTime.parse(j['endUtc'] as String).toUtc(),
        day: (j['day'] as String?) ?? '',
        dayLabel: (j['dayLabel'] as String?) ?? '',
        timeLabel: (j['timeLabel'] as String?) ?? '',
        available: j['available'] != false,
        left: (j['left'] as num?)?.toInt() ?? 0,
      );
}

/// What the customer can choose in a store. [enabled] false means no choice: every order is as soon as possible.
class DeliverySlots {
  const DeliverySlots({required this.enabled, required this.asapAllowed, this.windows = const []});
  final bool enabled, asapAllowed;
  final List<DeliverySlot> windows;

  static const off = DeliverySlots(enabled: false, asapAllowed: true);

  factory DeliverySlots.fromJson(Map<String, dynamic> j) => DeliverySlots(
        enabled: j['enabled'] == true,
        asapAllowed: j['asapAllowed'] != false,
        windows: j['windows'] == null ? const [] : asList(j['windows'], DeliverySlot.fromJson),
      );

  /// The windows by day, in order, for showing a row of days and the windows of the chosen one.
  Map<String, List<DeliverySlot>> get byDay {
    final days = <String, List<DeliverySlot>>{};
    for (final w in windows) {
      days.putIfAbsent(w.day, () => []).add(w);
    }
    return days;
  }
}

/// Which delivery times a store offers right now. The server checks the chosen one again when the order is placed.
abstract interface class DeliverySlotsRepository {
  Future<DeliverySlots> list(String storeId);
}

class ApiDeliverySlotsRepository implements DeliverySlotsRepository {
  ApiDeliverySlotsRepository(this._api);
  final ApiClient _api;

  @override
  Future<DeliverySlots> list(String storeId) => _api.get('/api/stores/$storeId/delivery-slots', parse: (d) => DeliverySlots.fromJson(asObject(d)));
}

/// Offline demo data has no scheduled delivery.
class LocalDeliverySlotsRepository implements DeliverySlotsRepository {
  @override
  Future<DeliverySlots> list(String storeId) async => DeliverySlots.off;
}

/// The words for a window in India time, the way the server words them: ("Today", "2 pm - 3 pm"). The day is "Today", "Tomorrow" or
/// a short date such as "Wed 4 Nov"; the screen translates the first two.
({String day, String time}) slotText(DateTime startUtc, DateTime endUtc, DateTime nowUtc) {
  const india = Duration(hours: 5, minutes: 30);
  final start = startUtc.toUtc().add(india);
  final end = endUtc.toUtc().add(india);
  final now = nowUtc.toUtc().add(india);
  final days = DateTime.utc(start.year, start.month, start.day).difference(DateTime.utc(now.year, now.month, now.day)).inDays;
  const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  String clock(DateTime t) {
    final h = t.hour % 12 == 0 ? 12 : t.hour % 12;
    return '$h${t.minute == 0 ? '' : ':${t.minute.toString().padLeft(2, '0')}'} ${t.hour < 12 ? 'am' : 'pm'}';
  }

  final day = days == 0 ? 'Today' : (days == 1 ? 'Tomorrow' : '${weekdays[start.weekday - 1]} ${start.day} ${months[start.month - 1]}');
  return (day: day, time: '${clock(start)} - ${clock(end)}');
}
