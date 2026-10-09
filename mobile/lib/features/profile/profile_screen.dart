import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/l10n.dart';
import '../../core/language_toggle.dart';
import '../../core/prefs.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../address/address_controller.dart';
import '../auth/auth_controller.dart';
import '../festival/festival.dart';
import '../orders/orders_controller.dart';

class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final p = context.pal;
    final orderCount = ref.watch(ordersProvider).value?.length ?? 0;
    final auth = ref.watch(authProvider);
    final user = auth.user;
    final place = ref.watch(deliveryPlaceProvider);
    final mode = ref.watch(themeModeProvider);
    void soon(String what) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('{what} is coming soon', {'what': what}))));
    return SafeArea(
      bottom: false,
      child: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 20, QC.gutter, 24), children: [
        Text(context.tr('Profile'), style: const TextStyle(color: Colors.white, fontSize: 32, fontWeight: FontWeight.w900)),
        const SizedBox(height: 16),
        Surface(
          child: Row(children: [
            Container(width: 60, height: 60, alignment: Alignment.center, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: Text(user == null ? '?' : user.fullName.substring(0, 1).toUpperCase(), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 24))),
            const SizedBox(width: 14),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(user?.fullName ?? context.tr('Guest shopper'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 18)),
                Text(user == null ? context.tr('Sign in to sync orders and addresses') : user.email, style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
              ]),
            ),
          ]),
        ),
        const SizedBox(height: 16),
        Surface(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Column(children: [
            if (user != null) _Tile(Icons.person_outline, context.tr('Edit profile'), user.phoneNumber, () => context.push('/account/profile')),
            if (user != null) _Tile(Icons.lock_outline, context.tr('Change password'), null, () => context.push('/account/password')),
            _Tile(Icons.receipt_long_outlined, context.tr('My orders'), context.tr('{n} placed', {'n': '$orderCount'}), () => context.go('/orders')),
            _Tile(Icons.location_on_outlined, context.tr('Saved addresses'), place == null ? context.tr('Add an address') : '${context.tr(place.label)} · ${place.line}', () => context.push('/addresses')),
            _Tile(Icons.payments_outlined, context.tr('Payment methods'), null, () => soon(context.tr('Payment methods'))),
            _Tile(Icons.help_outline, context.tr('Help & support'), null, () => soon(context.tr('Support'))),
            if (kDebugMode) _Tile(Icons.developer_mode, 'API diagnostics (debug)', null, () => context.push('/dev/api')),
          ]),
        ),
        const SizedBox(height: 16),
        Surface(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(context.tr('Language'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
            const SizedBox(height: 10),
            const LanguageToggle(),
            const SizedBox(height: 18),
            Text(context.tr('Appearance'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
            const SizedBox(height: 10),
            Container(
              padding: const EdgeInsets.all(4),
              decoration: BoxDecoration(color: p.dark ? const Color(0xFF0B0820) : Pal.ink, borderRadius: BorderRadius.circular(QC.rPill)),
              child: Row(children: [
                for (final m in [(ThemeMode.system, 'System'), (ThemeMode.light, 'Light'), (ThemeMode.dark, 'Dark')])
                  Expanded(
                    child: Semantics(
                      button: true,
                      selected: mode == m.$1,
                      label: context.tr(m.$2),
                      child: GestureDetector(
                        behavior: HitTestBehavior.opaque,
                        onTap: () => ref.read(themeModeProvider.notifier).set(m.$1),
                        child: Container(
                          height: 44,
                          alignment: Alignment.center,
                          decoration: BoxDecoration(color: mode == m.$1 ? Pal.yellow : Colors.transparent, borderRadius: BorderRadius.circular(QC.rPill)),
                          child: Text(context.tr(m.$2), style: TextStyle(color: mode == m.$1 ? Pal.ink : Colors.white, fontWeight: FontWeight.w900)),
                        ),
                      ),
                    ),
                  ),
              ]),
            ),
            const SizedBox(height: 18),
            Material(
              type: MaterialType.transparency,
              child: SwitchListTile(
                contentPadding: EdgeInsets.zero,
                value: ref.watch(festivalAnimationProvider),
                onChanged: ref.read(festivalAnimationProvider.notifier).set,
                title: Text(context.tr('Festival animations'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900)),
                subtitle: Text(context.tr('Falling lights and petals on Home during festivals'), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
              ),
            ),
          ]),
        ),
        const SizedBox(height: 20),
        user == null
            ? PillButton(context.tr('Sign in / Register'), arrow: true, onPressed: () => context.push('/welcome'))
            : SoftPillButton(context.tr('Log out'), height: 60, onPressed: () => ref.read(authProvider.notifier).signOut()),
      ]),
    );
  }
}

class _Tile extends StatelessWidget {
  const _Tile(this.icon, this.title, this.subtitle, this.onTap);
  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Material(
      type: MaterialType.transparency,
      child: ListTile(
        minTileHeight: 60,
        leading: Container(width: 40, height: 40, decoration: BoxDecoration(color: p.dark ? p.border : Colors.white, shape: BoxShape.circle), child: Icon(icon, color: p.onCard, size: 20)),
        title: Text(title, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
        subtitle: subtitle == null ? null : Text(subtitle!, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard)),
        trailing: Icon(Icons.chevron_right, color: p.mutedOnCard),
        onTap: onTap,
      ),
    );
  }
}
