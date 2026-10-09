import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

/// Opens the phone app with a number ready to dial. Tests replace it; a failure is not an error, the screen just says it could not.
abstract interface class PhoneLauncher {
  /// True when the phone app was opened.
  Future<bool> dial(String number);
}

class UrlPhoneLauncher implements PhoneLauncher {
  const UrlPhoneLauncher();

  @override
  Future<bool> dial(String number) async {
    final cleaned = number.replaceAll(RegExp(r'[^0-9+]'), '');
    if (cleaned.isEmpty) return false;
    try {
      return await launchUrl(Uri(scheme: 'tel', path: cleaned));
    } catch (_) {
      return false;
    }
  }
}

final phoneLauncherProvider = Provider<PhoneLauncher>((ref) => const UrlPhoneLauncher());
