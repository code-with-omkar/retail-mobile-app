import '../api/api_client.dart';
import '../dto/catalog_dto.dart';
import '../dto/order_dto.dart';
import '../models.dart';
import 'local_shop.dart';

/// The customer's notifications (messages about their orders). Screens depend on this interface, not on HTTP.
abstract interface class NotificationRepository {
  /// Newest first.
  Future<List<AppNotification>> list();

  /// How many are unread. Cheap enough to ask every minute (the bell).
  Future<int> unreadCount();

  Future<void> markRead(String id);

  /// Marks every unread notification as read.
  Future<void> markAllRead();
}

/// `api/customer/notifications`.
class ApiNotificationRepository implements NotificationRepository {
  ApiNotificationRepository(this._api);
  final ApiClient _api;

  @override
  Future<List<AppNotification>> list() async {
    final items = await _api.get('/api/customer/notifications', parse: notificationsFromJson);
    return items..sort((a, b) => b.createdAt.compareTo(a.createdAt));
  }

  @override
  Future<int> unreadCount() => _api.get('/api/customer/notifications/unread-count', parse: (d) => (asObject(d)['count'] as num).toInt());

  @override
  Future<void> markRead(String id) => _api.put('/api/customer/notifications/$id/read', body: {'isRead': true}, parse: (_) {});

  @override
  Future<void> markAllRead() => _api.put('/api/customer/notifications/read-all', parse: (_) {});
}

/// Notifications without a server (offline seed mode).
class LocalNotificationRepository implements NotificationRepository {
  LocalNotificationRepository(this._shop);
  final LocalShop _shop;

  @override
  Future<List<AppNotification>> list() async => List.of(_shop.notifications);

  @override
  Future<int> unreadCount() async => _shop.notifications.where((n) => !n.isRead).length;

  @override
  Future<void> markRead(String id) async {
    final at = _shop.notifications.indexWhere((n) => n.id == id);
    if (at >= 0) _shop.notifications[at] = _shop.notifications[at].asRead();
  }

  @override
  Future<void> markAllRead() async {
    for (var i = 0; i < _shop.notifications.length; i++) {
      _shop.notifications[i] = _shop.notifications[i].asRead();
    }
  }
}
