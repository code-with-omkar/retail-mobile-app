import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'prefs.dart';
import 'theme.dart';

/// Black pill with EN / मराठी, as in the Welcome reference.
class LanguageToggle extends ConsumerWidget {
  const LanguageToggle({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final current = ref.watch(localeProvider).languageCode;
    Widget seg(String code, String label) {
      final on = current == code;
      return Semantics(
        button: true,
        selected: on,
        label: label,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => ref.read(localeProvider.notifier).set(Locale(code)),
          child: Container(
            height: 44,
            padding: const EdgeInsets.symmetric(horizontal: 18),
            alignment: Alignment.center,
            decoration: BoxDecoration(color: on ? Pal.yellow : Colors.transparent, borderRadius: BorderRadius.circular(QC.rPill)),
            child: Text(label, style: TextStyle(color: on ? Pal.ink : Colors.white, fontWeight: FontWeight.w900, fontSize: 16)),
          ),
        ),
      );
    }

    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(color: context.pal.dark ? const Color(0xFF0B0820) : Pal.ink, borderRadius: BorderRadius.circular(QC.rPill)),
      child: Row(mainAxisSize: MainAxisSize.min, children: [seg('en', 'EN'), seg('mr', 'मराठी')]),
    );
  }
}
