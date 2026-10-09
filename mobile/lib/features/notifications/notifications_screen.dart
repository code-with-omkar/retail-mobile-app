import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/polling.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/models.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_widgets.dart';
import '../orders/orders_screen.dart';
import 'notifications_controller.dart';

/// How long ago, in a few words: "Just now", "5 min ago", "3 h ago", then the date.
String timeAgo(BuildContext context, DateTime at, DateTime now) {
  final diff = now.difference(at);
  if (diff.inMinutes < 1) return context.tr('Just now');
  if (diff.inMinutes < 60) return context.tr('{n} min ago', {'n': '${diff.inMinutes}'});
  if (diff.inHours < 24) return context.tr('{n} h ago', {'n': '${diff.inHours}'});
  return formatDate(at);
}

class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key});

  @override
  ConsumerState<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  @override
  void initState() {
    super.initState();
    // What the list held when it was last opened may be old: read it again now.
    Future.microtask(() => mounted ? ref.read(notificationsProvider.notifier).poll().catchError((Object _) {}) : null);
  }

  @override
  Widget build(BuildContext context) {
    final guest = ref.watch(authProvider).status == AuthStatus.signedOut;
    final list = ref.watch(notificationsProvider);
    final anyUnread = list.value?.any((n) => !n.isRead) ?? false;
    // New ones show up while the screen is open, without pulling (every 30 seconds, never in the background).
    return Polling(
      interval: const Duration(seconds: 30),
      active: !guest && TickerMode.valuesOf(context).enabled,
      onTick: ref.read(notificationsProvider.notifier).poll,
      child: AppScaffold(
      appBar: appTopBar(context, context.tr('Notifications'), backFallback: '/', actions: [
        if (anyUnread) TextButton(onPressed: () => ref.read(notificationsProvider.notifier).markAllRead(), child: Text(context.tr('Mark all as read'))),
      ]),
      body: guest
          ? EmptyState(icon: Icons.lock_outline, title: context.tr('Sign in to see your notifications'), message: context.tr('We tell you when the store accepts or delivers your order.'), actionLabel: context.tr('Log in'), onAction: () => context.push(withNext('/welcome', '/notifications')))
          : list.when(
              skipLoadingOnReload: true,
              loading: () => ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [for (var i = 0; i < 4; i++) const Padding(padding: EdgeInsets.only(bottom: 12), child: SkeletonBox(height: 84, radius: 24))]),
              error: (e, _) => ErrorState(error: e, onRetry: () => ref.read(notificationsProvider.notifier).refresh()),
              data: (items) => items.isEmpty
                  ? EmptyState(icon: Icons.notifications_none_rounded, title: context.tr('No notifications yet'), message: context.tr('We will tell you here when the store accepts or delivers your order.'))
                  : RefreshIndicator(
                      onRefresh: () => ref.read(notificationsProvider.notifier).refresh(),
                      child: ListView.separated(
                        physics: const AlwaysScrollableScrollPhysics(),
                        padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24),
                        itemCount: items.length,
                        separatorBuilder: (_, _) => const SizedBox(height: 12),
                        itemBuilder: (_, i) => _NotificationCard(items[i]),
                      ),
                    ),
            ),
      ),
    );
  }
}

class _NotificationCard extends ConsumerWidget {
  const _NotificationCard(this.notification);
  final AppNotification notification;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final texts = notificationTexts(context, notification);
    final unread = !notification.isRead;
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: () {
        ref.read(notificationsProvider.notifier).markRead(notification);
        final orderId = notification.orderId;
        if (orderId != null) context.push('/order/$orderId');
      },
      child: Semantics(
        label: '${texts.title}. ${texts.message}${unread ? '. ${context.tr('Unread')}' : ''}',
        child: Surface(
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(color: unread ? Pal.yellow : (p.dark ? p.border : Colors.white), shape: BoxShape.circle),
              child: Icon(notification.title == 'Order cancelled' ? Icons.cancel_outlined : Icons.receipt_long_outlined, color: unread ? Pal.ink : p.onCard),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(texts.title, style: TextStyle(color: p.onCard, fontWeight: unread ? FontWeight.w900 : FontWeight.w700, fontSize: 15)),
                const SizedBox(height: 2),
                Text(texts.message, style: TextStyle(color: p.mutedOnCard, height: 1.3)),
                const SizedBox(height: 6),
                Text(timeAgo(context, notification.createdAt, DateTime.now()), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
              ]),
            ),
            if (unread) const Padding(padding: EdgeInsets.only(top: 6, left: 8), child: CircleAvatar(radius: 5, backgroundColor: Pal.pink)),
          ]),
        ),
      ),
    );
  }
}
