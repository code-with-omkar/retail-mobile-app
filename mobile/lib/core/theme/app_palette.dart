import 'package:flutter/painting.dart';

/// Raw brand palette — "Electric Market" theme.
///
/// Never use these directly in feature code; read semantic roles from
/// [AppColors] via `context.appColors` and gradients via [AppCategoryGradients].
abstract final class AppPalette {
  // ── Electric brand primaries ──────────────────────────────────────────────
  static const Color electricViolet = Color(0xFF667EEA);   // primary CTA, nav active
  static const Color electricCyan   = Color(0xFF00D2FF);   // secondary accent
  static const Color electricMint   = Color(0xFF00F5A0);   // success / in-stock
  static const Color electricCoral  = Color(0xFFFF6B6B);   // error / deal / low-stock
  static const Color electricAmber  = Color(0xFFFFD200);   // badge / cart count
  static const Color electricPink   = Color(0xFFFA709A);   // brand highlight

  // ── Text-safe steps (AA ≥ 4.5:1 on white / #F5F6FF surface) ─────────────
  static const Color violetText  = Color(0xFF3D4FBB);   // 5.1:1 on white
  static const Color cyanText    = Color(0xFF0066A8);   // 5.3:1 on white
  static const Color mintText    = Color(0xFF007848);   // 5.6:1 on white
  static const Color coralText   = Color(0xFFB83B3B);   // 5.0:1 on white
  static const Color amberText   = Color(0xFF856800);   // 4.9:1 on white
  static const Color pinkText    = Color(0xFFB53060);   // 5.2:1 on white

  // ── Light tints (8 % opacity equivalent on white) ────────────────────────
  static const Color violetTint  = Color(0xFFECEEFD);
  static const Color cyanTint    = Color(0xFFDEF7FF);
  static const Color mintTint    = Color(0xFFD9FDF0);
  static const Color coralTint   = Color(0xFFFFEEEE);
  static const Color amberTint   = Color(0xFFFFF8D6);
  static const Color pinkTint    = Color(0xFFFFE4EE);

  // ── Light neutrals ────────────────────────────────────────────────────────
  static const Color ink        = Color(0xFF0D0E2B);   // deep navy-black text
  static const Color inkMuted   = Color(0xFF5E6080);
  static const Color surface    = Color(0xFFF5F6FF);   // faint electric tint
  static const Color card       = Color(0xFFFFFFFF);
  static const Color line       = Color(0xFFE4E6F2);

  // ── Dark neutrals ─────────────────────────────────────────────────────────
  static const Color night       = Color(0xFF08091A);
  static const Color nightCard   = Color(0xFF121328);
  static const Color nightLine   = Color(0xFF222440);
  static const Color nightInk    = Color(0xFFF0F1FF);
  static const Color nightMuted  = Color(0xFF8B8DAE);

  // ── Dark tints ────────────────────────────────────────────────────────────
  static const Color violetTintDark = Color(0xFF161A3C);
  static const Color cyanTintDark   = Color(0xFF0A1E30);
  static const Color mintTintDark   = Color(0xFF0A2118);
  static const Color coralTintDark  = Color(0xFF2A1010);
  static const Color amberTintDark  = Color(0xFF261E00);
  static const Color pinkTintDark   = Color(0xFF250B1A);
}
