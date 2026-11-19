import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../data/models.dart';
import '../../core/prefs.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';

/// How many notifications are unread: the number on the bell. Zero for a guest.
class UnreadCountNotifier extends AsyncNotifier<int> {
  @override
  Future<int> build() async {
    final signedIn = ref.watch(authProvider.select((a) => a.isSignedIn));
    if (!signedIn) return 0;
    return ref.watch(notificationRepositoryProvider).unreadCount();
  }

  /// A quiet check. When it fails the number on screen stays and the failure is thrown, so the caller can slow down.
  Future<void> poll() async {
    if (!ref.read(authProvider).isSignedIn) return;
    final count = await ref.read(notificationRepositoryProvider).unreadCount();
    if (ref.mounted) state = AsyncData(count);
  }

  void set(int count) => state = AsyncData(count < 0 ? 0 : count);
}

final unreadCountProvider = AsyncNotifierProvider<UnreadCountNotifier, int>(UnreadCountNotifier.new);

/// The notifications, newest first. A guest has none.
class NotificationsNotifier extends AsyncNotifier<List<AppNotification>> {
  @override
  Future<List<AppNotification>> build() async {
    final signedIn = ref.watch(authProvider.select((a) => a.isSignedIn));
    if (!signedIn) return const [];
    // Read again (in the new language) when the customer switches language.
    final language = ref.watch(localeProvider).languageCode;
    return ref.watch(notificationRepositoryProvider).list(language: language);
  }

  String get _language => ref.read(localeProvider).languageCode;

  Future<void> refresh() async {
    if (!ref.read(authProvider).isSignedIn) return;
    state = await AsyncValue.guard(() => ref.read(notificationRepositoryProvider).list(language: _language));
    await _syncCount();
  }

  /// A quiet check while the screen is open (and when it opens): the list on screen is replaced only when it worked. A failure keeps
  /// what is shown and is thrown, so the caller can slow down.
  Future<void> poll() async {
    if (!ref.read(authProvider).isSignedIn) return;
    final list = await ref.read(notificationRepositoryProvider).list(language: _language);
    if (!ref.mounted) return;
    state = AsyncData(list);
    ref.read(unreadCountProvider.notifier).set(list.where((n) => !n.isRead).length);
  }

  /// Shows it as read at once and tells the server; if the server refuses, the list is read again so it shows the truth.
  Future<void> markRead(AppNotification notification) async {
    if (notification.isRead) return;
    _update((n) => n.id == notification.id ? n.asRead() : n);
    ref.read(unreadCountProvider.notifier).set((ref.read(unreadCountProvider).value ?? 1) - 1);
    try {
      await ref.read(notificationRepositoryProvider).markRead(notification.id);
    } catch (_) {
      await refresh();
    }
  }

  Future<void> markAllRead() async {
    _update((n) => n.asRead());
    ref.read(unreadCountProvider.notifier).set(0);
    try {
      await ref.read(notificationRepositoryProvider).markAllRead();
    } catch (_) {
      await refresh();
    }
  }

  void _update(AppNotification Function(AppNotification) change) {
    final current = state.value;
    if (current != null) state = AsyncData([for (final n in current) change(n)]);
  }

  Future<void> _syncCount() async {
    try {
      await ref.read(unreadCountProvider.notifier).poll();
    } catch (_) {
      // the bell keeps its number until the next check
    }
  }
}

final notificationsProvider = AsyncNotifierProvider<NotificationsNotifier, List<AppNotification>>(NotificationsNotifier.new);

/// Whether the customer wants offers and announcements. Switching shows at once and is undone if the server refuses.
class OffersNotifier extends AsyncNotifier<bool> {
  @override
  Future<bool> build() async {
    if (!ref.watch(authProvider.select((a) => a.isSignedIn))) return true;
    return ref.watch(notificationRepositoryProvider).offersEnabled();
  }

  Future<void> set(bool enabled) async {
    final before = state.value ?? true;
    state = AsyncData(enabled);
    try {
      await ref.read(notificationRepositoryProvider).setOffers(enabled);
    } catch (_) {
      if (ref.mounted) state = AsyncData(before);
      rethrow;
    }
  }
}

final offersProvider = AsyncNotifierProvider<OffersNotifier, bool>(OffersNotifier.new);

/// The icon for a notification: by what happened to the order, and for offers and payments by their category.
IconData notificationIcon(AppNotification n) {
  if (n.category == 'Offer') return Icons.local_offer_outlined;
  return switch (n.type) {
    'OrderPlaced' => Icons.receipt_long_outlined,
    'OrderAccepted' => Icons.storefront_outlined,
    'OrderPacking' => Icons.inventory_2_outlined,
    'OutForDelivery' => Icons.delivery_dining_outlined,
    'OrderDelivered' => Icons.check_circle_outline,
    'OrderRejected' => Icons.block,
    'OrderCancelled' => Icons.cancel_outlined,
    _ when n.title == 'Order cancelled' => Icons.cancel_outlined,
    _ when n.category == 'Payment' => Icons.account_balance_wallet_outlined,
    _ => Icons.receipt_long_outlined,
  };
}

/// The server writes its notifications in English. This shows the ones it knows in the customer's language and any other as it came.
({String title, String message}) notificationTexts(BuildContext context, AppNotification n) {
  if (n.type != 'OrderStatusChanged') return (title: n.title, message: n.message);
  final title = switch (n.title) {
    'Order status updated' => context.tr('Order status updated'),
    'Order cancelled' => context.tr('Order cancelled'),
    _ => n.title,
  };
  if (n.message == 'Your order was cancelled.') return (title: title, message: context.tr('Your order was cancelled.'));
  final match = RegExp(r'^Your order is now (\w+)\.$').firstMatch(n.message);
  if (match != null) {
    const phrases = {
      'Accepted': 'accepted by the store',
      'Preparing': 'being prepared',
      'Ready': 'ready for delivery',
      'Confirmed': 'confirmed',
      'OutForDelivery': 'on its way',
      'Completed': 'delivered',
      'Delivered': 'delivered',
      'Rejected': 'declined by the store',
      'Cancelled': 'cancelled',
    };
    final phrase = phrases[match.group(1)];
    if (phrase != null) return (title: title, message: context.tr('Your order is now {status}.', {'status': context.tr(phrase)}));
  }
  return (title: title, message: n.message);
}
