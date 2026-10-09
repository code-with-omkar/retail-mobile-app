import 'package:flutter/material.dart';

/// Product / section categories used to drive per-tile gradient fills.
///
/// Usage in widgets:
/// ```dart
/// final grad = AppCategoryGradients.of(AppCategory.vegetables);
/// Container(
///   decoration: BoxDecoration(gradient: grad.background),
///   child: ...,
/// );
/// ```
enum AppCategory {
  vegetables,   // Leafy greens, roots, gourds
  fruits,       // Seasonal & tropical fruits
  grains,       // Rice, wheat, pulses, lentils
  dairy,        // Milk, paneer, curd, ghee
  spices,       // Masalas, dry spices, herbs
  personalCare, // Soap, shampoo, skincare
  construction, // Cement, steel, sand (NirmaanBhav)
  bazaar,       // General mandi / agriculture (BazaarBhav)
  general,      // Fallback / uncategorised
}

/// Gradient pair + accent for one [AppCategory].
@immutable
class CategoryGradient {
  const CategoryGradient({
    required this.name,
    required this.start,
    required this.end,
    required this.accent,   // solid color for icons, badges, chips
    required this.onAccent, // readable text/icon on top of `accent`
    required this.textColor,// readable label on gradient background (light)
    this.begin = Alignment.topLeft,
    this.end2 = Alignment.bottomRight,
  });

  final String name;
  final Color start;
  final Color end;
  final Color accent;
  final Color onAccent;
  final Color textColor;
  final AlignmentGeometry begin;
  final AlignmentGeometry end2;

  /// Full-bleed background gradient (for hero tiles, category banners).
  LinearGradient get background => LinearGradient(
        begin: begin,
        end: end2,
        colors: [start, end],
      );

  /// Soft pastel tint for card backgrounds (12 % opacity blended to white).
  LinearGradient get cardTint => LinearGradient(
        begin: begin,
        end: end2,
        colors: [
          Color.lerp(start, Colors.white, 0.82)!,
          Color.lerp(end, Colors.white, 0.82)!,
        ],
      );

  /// Subtle strip for chip / tag fills (5 % blend).
  LinearGradient get chipTint => LinearGradient(
        begin: Alignment.centerLeft,
        end: Alignment.centerRight,
        colors: [
          Color.lerp(start, Colors.white, 0.94)!,
          Color.lerp(end, Colors.white, 0.94)!,
        ],
      );

  /// Glassy shimmer pair used for skeleton loaders.
  LinearGradient get shimmer => LinearGradient(
        begin: Alignment.centerLeft,
        end: Alignment.centerRight,
        stops: const [0.0, 0.5, 1.0],
        colors: [
          Color.lerp(start, Colors.white, 0.90)!,
          Color.lerp(end, Colors.white, 0.70)!,
          Color.lerp(start, Colors.white, 0.90)!,
        ],
      );

  /// Bottom-to-top scrim overlay for image tiles (text legibility).
  LinearGradient get scrim => const LinearGradient(
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
        stops: [0.0, 0.55, 1.0],
        colors: [Colors.transparent, Colors.transparent, Color(0xCC000000)],
      );
}

/// Catalogue of all category gradient definitions.
abstract final class AppCategoryGradients {
  static const CategoryGradient vegetables = CategoryGradient(
    name: 'Vegetables',
    start: Color(0xFF00F5A0),   // Electric Mint
    end:   Color(0xFF00D2FF),   // Electric Cyan
    accent: Color(0xFF00A878),
    onAccent: Colors.white,
    textColor: Color(0xFF00563F),
  );

  static const CategoryGradient fruits = CategoryGradient(
    name: 'Fruits',
    start: Color(0xFFFF6B6B),   // Electric Coral
    end:   Color(0xFFFFBE76),   // Warm Peach
    accent: Color(0xFFE84040),
    onAccent: Colors.white,
    textColor: Color(0xFF8C1C1C),
  );

  static const CategoryGradient grains = CategoryGradient(
    name: 'Grains & Pulses',
    start: Color(0xFFFFD200),   // Electric Amber
    end:   Color(0xFFF7971E),   // Warm Orange
    accent: Color(0xFFD4A000),
    onAccent: Color(0xFF3A2800),
    textColor: Color(0xFF6B4400),
  );

  static const CategoryGradient dairy = CategoryGradient(
    name: 'Dairy',
    start: Color(0xFF4FACFE),   // Sky Blue
    end:   Color(0xFF00D2FF),   // Electric Cyan
    accent: Color(0xFF2589D8),
    onAccent: Colors.white,
    textColor: Color(0xFF0D4F85),
  );

  static const CategoryGradient spices = CategoryGradient(
    name: 'Spices',
    start: Color(0xFFFA709A),   // Electric Pink
    end:   Color(0xFFBB6BD9),   // Electric Purple
    accent: Color(0xFFB53060),
    onAccent: Colors.white,
    textColor: Color(0xFF6B1040),
  );

  static const CategoryGradient personalCare = CategoryGradient(
    name: 'Personal Care',
    start: Color(0xFFA18CD1),   // Lavender
    end:   Color(0xFFFBC2EB),   // Electric Blush
    accent: Color(0xFF7C5CBF),
    onAccent: Colors.white,
    textColor: Color(0xFF3D2280),
  );

  static const CategoryGradient construction = CategoryGradient(
    name: 'Construction',
    start: Color(0xFFFA709A),   // Electric Coral-Pink
    end:   Color(0xFFFEE140),   // Electric Yellow
    accent: Color(0xFFD45F00),
    onAccent: Colors.white,
    textColor: Color(0xFF6B2800),
  );

  static const CategoryGradient bazaar = CategoryGradient(
    name: 'Bazaar / Mandi',
    start: Color(0xFF11998E),   // Deep Teal
    end:   Color(0xFF38EF7D),   // Electric Green
    accent: Color(0xFF0D7A70),
    onAccent: Colors.white,
    textColor: Color(0xFF063E38),
  );

  static const CategoryGradient general = CategoryGradient(
    name: 'General',
    start: Color(0xFF667EEA),   // Electric Violet
    end:   Color(0xFF764BA2),   // Deep Purple
    accent: Color(0xFF4A5EBB),
    onAccent: Colors.white,
    textColor: Color(0xFF1E2A7A),
  );

  /// Look up the gradient for a given category.
  static CategoryGradient of(AppCategory category) => switch (category) {
        AppCategory.vegetables   => vegetables,
        AppCategory.fruits       => fruits,
        AppCategory.grains       => grains,
        AppCategory.dairy        => dairy,
        AppCategory.spices       => spices,
        AppCategory.personalCare => personalCare,
        AppCategory.construction => construction,
        AppCategory.bazaar       => bazaar,
        AppCategory.general      => general,
      };

  /// All categories, in display order.
  static const List<AppCategory> all = [
    AppCategory.vegetables,
    AppCategory.fruits,
    AppCategory.grains,
    AppCategory.dairy,
    AppCategory.spices,
    AppCategory.personalCare,
    AppCategory.construction,
    AppCategory.bazaar,
    AppCategory.general,
  ];
}
