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
        Text(context.tr('Profile'), style: QC.display(32, color: context.pal.onGround)),
        const SizedBox(height: 16),
        Surface(
          bordered: true,
          child: Row(children: [
            Container(width: 56, height: 56, alignment: Alignment.center, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: Text(user == null ? '?' : user.fullName.characters.first.toUpperCase(), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: 22))),
            const SizedBox(width: 14),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(user?.fullName ?? context.tr('Guest shopper'), maxLines: 1, overflow: TextOverflow.ellipsis, style: QC.display(18, color: p.onCard)),
                Text(user == null ? context.tr('Sign in to sync orders and addresses') : ((user.phoneNumber ?? '').isNotEmpty ? user.phoneNumber! : user.email), maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.mutedOnCard, fontSize: 13)),
              ]),
            ),
            if (user != null)
              GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => context.push('/account/profile'),
                child: SizedBox(
                  height: QC.minTap,
                  child: Center(child: Container(height: 32, padding: const EdgeInsets.symmetric(horizontal: 14), alignment: Alignment.center, decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(QC.rPill)), child: Text(context.tr('Edit'), style: const TextStyle(color: Pal.ink, fontWeight: FontWeight.w800, fontSize: 12)))),
                ),
              ),
          ]),
        ),
        const SizedBox(height: 14),
        IntrinsicHeight(child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Expanded(child: _QuickCard(Icons.receipt_long_outlined, context.tr('Orders'), context.tr('{n} placed', {'n': '$orderCount'}), () => context.go('/orders'))),
          if (user != null) ...[
            const SizedBox(width: 10),
            Expanded(child: _QuickCard(Icons.favorite_border, context.tr('Saved'), null, () => context.push('/favourites'))),
          ],
          const SizedBox(width: 10),
          Expanded(child: _QuickCard(Icons.location_on_outlined, context.tr('Addresses'), null, () => context.push('/addresses'))),
        ])),
        const SizedBox(height: 18),
        if (user != null) _GroupTitle(context.tr('Account')),
        if (user != null) Surface(
          bordered: true,
          padding: const EdgeInsets.symmetric(horizontal: 14),
          child: Column(children: [
            _Tile(Icons.person_outline, context.tr('Edit profile'), user.phoneNumber, () => context.push('/account/profile')),
            if (!user.phoneVerified) _Tile(Icons.verified_user_outlined, context.tr('Verify phone number'), context.tr('So you can sign in with a code'), () => context.push('/phone?verify=1')),
            if (user.phoneVerified) _Tile(Icons.verified_outlined, context.tr('Phone number verified'), user.phoneNumber, () => context.push('/phone?verify=1')),
            if (user.email.isNotEmpty) _Tile(Icons.lock_outline, context.tr('Change password'), null, () => context.push('/account/password')),
          ]),
        ),
        const SizedBox(height: 16),
        _GroupTitle(context.tr('Orders and places')),
        Surface(
          bordered: true,
          padding: const EdgeInsets.symmetric(horizontal: 14),
          child: Column(children: [
            if (user != null) _Tile(Icons.favorite_border, context.tr('Saved items'), null, () => context.push('/favourites')),
            _Tile(Icons.receipt_long_outlined, context.tr('My orders'), context.tr('{n} placed', {'n': '$orderCount'}), () => context.go('/orders')),
            _Tile(Icons.location_on_outlined, context.tr('Saved addresses'), place == null ? context.tr('Add an address') : '${context.tr(place.label)} · ${place.line}', () => context.push('/addresses')),
          ]),
        ),
        const SizedBox(height: 16),
        _GroupTitle(context.tr('Payments and support')),
        Surface(
          bordered: true,
          padding: const EdgeInsets.symmetric(horizontal: 14),
          child: Column(children: [
            _Tile(Icons.payments_outlined, context.tr('Payment methods'), null, () => soon(context.tr('Payment methods'))),
            _Tile(Icons.help_outline, context.tr('Help & support'), null, () => soon(context.tr('Support'))),
            if (user != null) _Tile(Icons.delete_outline, context.tr('Delete account'), null, () => context.push('/account/delete')),
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

/// A small heading above a group of rows.
class _GroupTitle extends StatelessWidget {
  const _GroupTitle(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
        child: Text(text.toUpperCase(), style: TextStyle(color: context.pal.mutedOnGround, fontSize: 11, fontWeight: FontWeight.w800, letterSpacing: 1)),
      );
}

/// A white card with an icon, a label and an optional second line: one tap to the places used most.
class _QuickCard extends StatelessWidget {
  const _QuickCard(this.icon, this.title, this.subtitle, this.onTap);
  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Semantics(
      button: true,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          constraints: const BoxConstraints(minHeight: 72),
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 10),
          decoration: BoxDecoration(color: p.dark ? p.card : Colors.white, borderRadius: BorderRadius.circular(14), border: Border.all(color: p.border)),
          child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
            Container(width: 32, height: 32, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: Icon(icon, size: 18, color: Pal.ink)),
            const SizedBox(height: 4),
            Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 11)),
            if (subtitle != null) Text(subtitle!, maxLines: 1, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center, style: TextStyle(color: p.mutedOnCard, fontSize: 10)),
          ]),
        ),
      ),
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
      child: InkWell(
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: QC.minTap),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Row(children: [
              Container(width: 30, height: 30, decoration: BoxDecoration(color: p.dark ? p.border : p.card, borderRadius: BorderRadius.circular(9)), child: Icon(icon, color: p.onCard, size: 17)),
              const SizedBox(width: 12),
              Expanded(child: Text(title, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w700, fontSize: 14))),
              if (subtitle != null) ...[
                const SizedBox(width: 8),
                Flexible(child: Text(subtitle!, maxLines: 1, overflow: TextOverflow.ellipsis, textAlign: TextAlign.end, style: TextStyle(color: p.mutedOnCard, fontSize: 12))),
              ],
              const SizedBox(width: 4),
              Icon(Icons.chevron_right, color: p.mutedOnCard, size: 20),
            ]),
          ),
        ),
      ),
    );
  }
}
