import 'package:lucide_icons_flutter/lucide_icons.dart';

import 'dart:math' as math;

import 'package:cached_network_image/cached_network_image.dart';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../data/api/api_exception.dart';
import '../data/models.dart';
import '../features/cart/cart_controller.dart';
import '../features/offers/festival_card.dart';
import '../features/offers/offers_controller.dart';
import 'l10n.dart';
import 'theme.dart';

String productName(BuildContext context, Product p) =>
    context.isMarathi ? p.nameMr : p.name;

/// Category name in the current language. Uses the API translation when present, otherwise the app's own
/// string table (for English names such as "Dairy"), otherwise English.
String categoryLabel(BuildContext context, Category c) => context.isMarathi
    ? (c.hasMarathi ? c.labelMr : context.tr(c.label))
    : c.label;

String productDescription(BuildContext context, Product p) =>
    context.isMarathi ? p.descriptionMr : p.description;

/// Rounded card on the gradient (lavender in light, deep indigo in dark).
/// A form field with its label above it. Used instead of a floating label, which gets cut by the rounded field border.
class LabeledField extends StatelessWidget {
  const LabeledField({
    super.key,
    required this.label,
    required this.child,
    this.onGround = true,
  });
  final String label;
  final Widget child;

  /// Label colour for fields on the dark ground (white) or on a card (card text colour).
  final bool onGround;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 14),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 6, bottom: 6),
          child: ExcludeSemantics(
            child: Text(
              label,
              style: TextStyle(
                color: onGround
                    ? context.pal.mutedOnGround
                    : context.pal.mutedOnCard,
                fontSize: 13,
                fontWeight: FontWeight.w800,
              ),
            ),
          ),
        ),
        Semantics(label: label, child: child),
      ],
    ),
  );
}

class Surface extends StatelessWidget {
  const Surface({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(12),
    this.color,
    this.radius = QC.rCard,
    this.bordered = false,
  });
  final Widget child;
  final EdgeInsets padding;
  final Color? color;
  final double radius;

  /// v4 list cards: white with a thin line instead of the grey fill (the dark theme keeps its filled card).
  final bool bordered;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: padding,
      decoration: BoxDecoration(
        color: color ?? (bordered && !p.dark ? Colors.white : p.card),
        borderRadius: BorderRadius.circular(radius),
        border: bordered ? Border.all(color: p.border) : null,
      ),
      child: child,
    );
  }
}

/// Primary yellow pill. With [arrow] it shows the ink arrow circle on the right (Get OTP, Checkout).
class PillButton extends StatelessWidget {
  const PillButton(
    this.label, {
    super.key,
    required this.onPressed,
    this.arrow = false,
    this.height = 60,
  });
  final String label;
  final VoidCallback? onPressed;
  final bool arrow;
  final double height;

  @override
  Widget build(BuildContext context) {
    final enabled = onPressed != null;
    return Semantics(
      button: true,
      enabled: enabled,
      label: label,
      excludeSemantics: true,
      child: Opacity(
        opacity: enabled ? 1 : .5,
        child: Material(
          color: Pal.yellow,
          borderRadius: BorderRadius.circular(QC.rPill),
          child: InkWell(
            borderRadius: BorderRadius.circular(QC.rPill),
            onTap: onPressed,
            child: Container(
              height: height,
              padding: EdgeInsets.only(
                left: arrow ? 28 : 16,
                right: arrow ? 8 : 16,
              ),
              child: Row(
                mainAxisAlignment: arrow
                    ? MainAxisAlignment.spaceBetween
                    : MainAxisAlignment.center,
                children: [
                  Flexible(
                    child: Text(
                      label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: Pal.ink,
                        fontSize: 18,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                  if (arrow)
                    Container(
                      width: height - 16,
                      height: height - 16,
                      decoration: const BoxDecoration(
                        color: Pal.ink,
                        shape: BoxShape.circle,
                      ),
                      child: const Icon(
                        LucideIcons.arrowRight,
                        color: Pal.yellow,
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Secondary pill (social sign-in, secondary actions).
class SoftPillButton extends StatelessWidget {
  const SoftPillButton(
    this.label, {
    super.key,
    required this.onPressed,
    this.leading,
    this.height = 56,
  });
  final String label;
  final VoidCallback onPressed;
  final Widget? leading;
  final double height;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Material(
      color: p.card,
      borderRadius: BorderRadius.circular(QC.rPill),
      child: InkWell(
        borderRadius: BorderRadius.circular(QC.rPill),
        onTap: onPressed,
        child: Container(
          height: height,
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              if (leading != null) ...[leading!, const SizedBox(width: 10)],
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    color: p.onCard,
                    fontWeight: FontWeight.w800,
                    fontSize: 15,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// − qty + pill: dark pill with a yellow plus, as in the reference.
class StepperPill extends StatelessWidget {
  const StepperPill({
    super.key,
    required this.qty,
    required this.onMinus,
    required this.onPlus,
    this.minusLabel = 'Remove one',
    this.plusLabel = 'Add one',
    this.size = 36,
    this.stretch = false,
  });
  final int qty;
  final VoidCallback onMinus, onPlus;
  final String minusLabel, plusLabel;
  final double size;
  final bool stretch;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    Widget dot(
      IconData icon,
      Color bg,
      Color fg,
      String label,
      VoidCallback onTap,
    ) => Semantics(
      button: true,
      label: label,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          width: size,
          height: size,
          margin: const EdgeInsets.all(2),
          decoration: BoxDecoration(color: bg, shape: BoxShape.circle),
          child: Icon(icon, size: 18, color: fg),
        ),
      ),
    );
    return Container(
      padding: const EdgeInsets.all(2),
      decoration: BoxDecoration(
        color: p.stepper,
        borderRadius: BorderRadius.circular(QC.rPill),
      ),
      child: Row(
        mainAxisSize: stretch ? MainAxisSize.max : MainAxisSize.min,
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          dot(LucideIcons.minus, p.minus, Colors.white, minusLabel, onMinus),
          Padding(
            padding: EdgeInsets.symmetric(horizontal: size < 34 ? 8 : 10),
            child: Text(
              '$qty',
              style: TextStyle(
                color: p.onStepper,
                fontWeight: FontWeight.w800,
                fontSize: 15,
              ),
            ),
          ),
          dot(LucideIcons.plus, Pal.yellow, Pal.ink, plusLabel, onPlus),
        ],
      ),
    );
  }
}

/// Yellow "+" that becomes a stepper once the product is in the cart.
class QuantityControl extends ConsumerWidget {
  const QuantityControl(this.product, {super.key, this.line, this.size = 36});
  final Product product;

  /// Cart line to control (for non-default pack sizes). Defaults to the product's default size.
  final CartLine? line;

  /// Diameter of the stepper buttons. Product cards use a smaller one so the price still fits next to it.
  final double size;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final key = line?.key ?? cartKey(product);
    final qty = ref.watch(cartProvider)[key] ?? 0;
    final cart = ref.read(cartProvider.notifier);
    final name = productName(context, product);
    if (!product.inStock) {
      return Flexible(
        child: Text(
          context.tr('Out of stock'),
          textAlign: TextAlign.end,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            color: Pal.pink,
            fontSize: 12,
            fontWeight: FontWeight.w800,
          ),
        ),
      );
    }
    if (qty == 0) {
      return Semantics(
        button: true,
        label: '${context.tr('Add')} $name',
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => cart.add(key),
          child: SizedBox(
            width: QC.minTap,
            height: QC.minTap,
            child: Center(
              child: Container(
                width: 40,
                height: 40,
                decoration: const BoxDecoration(
                  color: Pal.yellow,
                  shape: BoxShape.circle,
                ),
                child: const Icon(LucideIcons.plus, color: Pal.ink),
              ),
            ),
          ),
        ),
      );
    }
    return StepperPill(
      size: size,
      qty: qty,
      onMinus: () => cart.remove(key),
      onPlus: () => cart.add(key),
      minusLabel: '${context.tr('Remove one')} $name',
      plusLabel: '${context.tr('Add one')} $name',
    );
  }
}

/// Fills its parent with the product photo, or a generated category icon when no image exists.
class ProductVisual extends StatelessWidget {
  const ProductVisual(this.product, {super.key});
  final Product product;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final side = c.biggest.shortestSide.isFinite
          ? c.biggest.shortestSide
          : 64.0;
      final fallback = Icon(
        product.icon,
        size: side * .5,
        color: Pal.ink.withValues(alpha: .7),
      );
      final url = product.imageUrl;
      // v4: soft tints. The saved colours are strong, so they are blended towards white on the light theme.
      final tint = context.pal.dark
          ? product.color
          : Color.lerp(product.color, Colors.white, .5)!;
      return ColoredBox(
        color: tint,
        child: Center(
          child: url == null
              ? fallback
              : CachedNetworkImage(
                  imageUrl: url,
                  fit: BoxFit.cover,
                  width: double.infinity,
                  height: double.infinity,
                  memCacheWidth: (side * MediaQuery.devicePixelRatioOf(context))
                      .round(),
                  placeholder: (_, _) => fallback,
                  errorWidget: (_, _, _) => fallback,
                ),
        ),
      );
    },
  );
}

class ProductThumb extends StatelessWidget {
  const ProductThumb(
    this.product, {
    super.key,
    this.size = 64,
    this.radius = 18,
  });
  final Product product;
  final double size, radius;

  @override
  Widget build(BuildContext context) => ClipRRect(
    borderRadius: BorderRadius.circular(radius),
    child: SizedBox(width: size, height: size, child: ProductVisual(product)),
  );
}

/// Tilted yellow "−22%" sticker.
class DiscountSticker extends StatelessWidget {
  const DiscountSticker(this.pct, {super.key, this.large = false});
  final int pct;
  final bool large;

  @override
  Widget build(BuildContext context) => Transform.rotate(
    angle: -math.pi / 36,
    child: Container(
      padding: EdgeInsets.symmetric(
        horizontal: large ? 16 : 8,
        vertical: large ? 8 : 3,
      ),
      decoration: BoxDecoration(
        color: Pal.yellow,
        borderRadius: BorderRadius.circular(large ? 18 : 12),
      ),
      child: Text(
        '−$pct%',
        style: TextStyle(
          color: Pal.ink,
          fontWeight: FontWeight.w900,
          fontSize: large ? 26 : 12,
        ),
      ),
    ),
  );
}

/// A corner ribbon for a discounted item, across the top-left corner of whatever it is placed over (give it a clipped parent).
class DiscountRibbon extends StatelessWidget {
  const DiscountRibbon(
    this.pct, {
    super.key,
    this.size = 58,
    this.fontSize = 10,
  });
  final int pct;

  /// Kept for the callers that size a corner sticker; the v4 discount is a small pill.
  final double size, fontSize;

  @override
  Widget build(BuildContext context) => Semantics(
    label: context.tr('{n}% off', {'n': '$pct'}),
    child: ExcludeSemantics(
      child: Container(
        margin: const EdgeInsets.all(6),
        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
        decoration: BoxDecoration(
          color: const Color(0xFF14A44D),
          borderRadius: BorderRadius.circular(6),
        ),
        child: Text(
          context.tr('{n}% off', {'n': '$pct'}),
          style: TextStyle(
            color: Colors.white,
            fontWeight: FontWeight.w800,
            fontSize: fontSize,
          ),
        ),
      ),
    ),
  );
}

class PriceText extends StatelessWidget {
  const PriceText(
    this.price, {
    super.key,
    this.mrp,
    this.size = 18,
    this.onCard = true,
  });
  final num price;
  final num? mrp;
  final double size;
  final bool onCard;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final color = onCard ? p.onCard : context.pal.onGround;
    return Row(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.baseline,
      textBaseline: TextBaseline.alphabetic,
      children: [
        Text(
          rupees(price),
          style: TextStyle(
            fontWeight: FontWeight.w900,
            fontSize: size,
            color: color,
          ),
        ),
        if (mrp != null && mrp! > price) ...[
          const SizedBox(width: 8),
          Text(
            rupees(mrp!),
            style: TextStyle(
              fontSize: size * .6,
              color: onCard ? p.mutedOnCard : context.pal.mutedOnGround,
              decoration: TextDecoration.lineThrough,
            ),
          ),
        ],
      ],
    );
  }
}

class ProductCard extends ConsumerWidget {
  const ProductCard(this.product, {super.key, this.rank});
  final Product product;

  /// Place in a ranked row (Popular near you): shown as a small #1, #2 badge on the tile.
  final int? rank;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final festive = ref.watch(festivalProductThemesProvider)[product.id];
    final p = context.pal;
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: () => context.push('/product/${product.id}'),
      // v4: the picture tile is the card (tint plus photo, no box around it); name, size and price sit on the page below.
      child: Surface(
        color: Colors.transparent,
        padding: EdgeInsets.zero,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            SizedBox(
              height: 96,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(14),
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    Opacity(
                      opacity: product.inStock ? 1 : .45,
                      child: ProductVisual(product),
                    ),
                    if (rank != null)
                      Positioned(
                        top: 6,
                        left: 6,
                        child: Container(
                          height: 18,
                          padding: const EdgeInsets.symmetric(horizontal: 6),
                          alignment: Alignment.center,
                          decoration: BoxDecoration(
                            color: const Color(0xD1141118),
                            borderRadius: BorderRadius.circular(6),
                          ),
                          child: Text(
                            '#$rank',
                            style: const TextStyle(
                              color: Pal.yellow,
                              fontSize: 10,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                        ),
                      ),
                    if (product.discountPct > 0)
                      Positioned(
                        top: 0,
                        left: rank == null ? 0 : null,
                        right: rank == null ? null : 0,
                        child: DiscountRibbon(product.discountPct),
                      ),
                    if (festive != null)
                      Positioned(
                        bottom: 4,
                        left: 4,
                        child: FestivalProductTag(festive),
                      ),
                    // v4: the add button sits on the picture tile's corner (30 drawn, 44 to tap); the stepper replaces it there.
                    if (product.inStock)
                      Positioned(
                        right: 0,
                        bottom: 0,
                        child: QuantityControl(product, size: 30),
                      ),
                  ],
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(2, 6, 2, 0),
              // v4: the name on one line, the size under it, so a long name is not eaten by the size.
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    productName(context, product),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: p.onCard,
                      fontSize: 12,
                      fontWeight: FontWeight.w800,
                      height: 1.25,
                    ),
                  ),
                  Text(
                    product.unit,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: p.mutedOnCard,
                      fontSize: 11,
                      height: 1.25,
                    ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.only(left: 2, top: 2),
              child: product.inStock
                  ? FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: Alignment.centerLeft,
                      child: PriceText(
                        product.price,
                        mrp: product.mrp,
                        size: 15,
                      ),
                    )
                  : Text(
                      context.tr('Out of stock'),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: Pal.pink,
                        fontSize: 12,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class SectionHeader extends StatelessWidget {
  const SectionHeader(
    this.title, {
    super.key,
    this.action,
    this.onAction,
    this.subtitle,
  });
  final String title;

  /// A small grey line under the title (for example "Most searched in your area today").
  final String? subtitle;
  final String? action;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisAlignment: MainAxisAlignment.spaceBetween,
    children: [
      Flexible(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: QC.display(18, color: context.pal.onGround)),
            if (subtitle != null)
              Text(
                subtitle!,
                style: TextStyle(
                  color: context.pal.mutedOnGround,
                  fontSize: 12,
                ),
              ),
          ],
        ),
      ),
      if (action != null)
        TextButton(
          onPressed: onAction,
          style: TextButton.styleFrom(
            minimumSize: const Size(QC.minTap, QC.minTap),
            padding: const EdgeInsets.symmetric(horizontal: 8),
            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
          ),
          child: Text(action!),
        ),
    ],
  );
}

class EmptyState extends StatelessWidget {
  const EmptyState({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.actionLabel,
    this.onAction,
  });
  final IconData icon;
  final String title, message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) => Center(
    child: SingleChildScrollView(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 96,
            height: 96,
            decoration: const BoxDecoration(
              color: Pal.yellow,
              shape: BoxShape.circle,
            ),
            child: Icon(icon, size: 44, color: Pal.ink),
          ),
          const SizedBox(height: 20),
          Text(
            title,
            textAlign: TextAlign.center,
            style: QC.display(20, color: context.pal.onGround),
          ),
          const SizedBox(height: 6),
          Text(
            message,
            textAlign: TextAlign.center,
            style: TextStyle(color: context.pal.mutedOnGround),
          ),
          if (actionLabel != null) ...[
            const SizedBox(height: 24),
            SizedBox(
              width: 240,
              child: PillButton(actionLabel!, onPressed: onAction, height: 56),
            ),
          ],
        ],
      ),
    ),
  );
}

/// Label/value row used for bill breakdowns (on a card).
class BillRow extends StatelessWidget {
  const BillRow(
    this.label,
    this.value, {
    super.key,
    this.bold = false,
    this.accent = false,
  });
  final String label, value;
  final bool bold, accent;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final style = TextStyle(
      fontSize: bold ? 18 : 14,
      fontWeight: bold ? FontWeight.w900 : FontWeight.w500,
      color: accent ? (p.dark ? Pal.green : const Color(0xFF0B6B33)) : p.onCard,
    );
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Flexible(child: Text(label, style: style)),
          const SizedBox(width: 12),
          Text(value, style: style),
        ],
      ),
    );
  }
}

/// Small rounded status chip.
class Chip2 extends StatelessWidget {
  const Chip2(
    this.label, {
    super.key,
    this.dot,
    this.background,
    this.foreground,
  });
  final String label;
  final Color? dot, background, foreground;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
      decoration: BoxDecoration(
        color: background ?? p.chip,
        borderRadius: BorderRadius.circular(QC.rPill),
        border: p.dark ? Border.all(color: p.border) : null,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (dot != null) ...[
            Container(
              width: 9,
              height: 9,
              decoration: BoxDecoration(color: dot, shape: BoxShape.circle),
            ),
            const SizedBox(width: 8),
          ],
          Text(
            label,
            style: TextStyle(
              color: foreground ?? p.onChip,
              fontWeight: FontWeight.w800,
              fontSize: 13,
            ),
          ),
        ],
      ),
    );
  }
}

/// The v4 order status pill: a small coloured pill with a dot. [tone] picks the colours: progress (blue), moving (yellow), done (green), stopped (red).
enum StatusTone { progress, moving, done, stopped }

class StatusPill extends StatelessWidget {
  const StatusPill(this.label, this.tone, {super.key, this.background});
  final String label;
  final StatusTone tone;

  /// A fixed background (white on a tinted card header) instead of the tone's own pastel.
  final Color? background;

  @override
  Widget build(BuildContext context) {
    final dark = context.pal.dark;
    final (bg, fg, dot) = switch (tone) {
      StatusTone.progress => (
        const Color(0xFFEAF0FF),
        const Color(0xFF1E3A8A),
        const Color(0xFF2455E6),
      ),
      StatusTone.moving => (
        const Color(0xFFFFF4D6),
        const Color(0xFF5C3B00),
        const Color(0xFFF5A700),
      ),
      StatusTone.done => (
        const Color(0xFFE3F7EA),
        const Color(0xFF0B6B33),
        const Color(0xFF14A44D),
      ),
      StatusTone.stopped => (
        const Color(0xFFFDECEA),
        const Color(0xFF9B2414),
        const Color(0xFFC42F1D),
      ),
    };
    // The pastel pills are light on purpose; on the dark theme they stay as they are so the colours keep their meaning.
    return Container(
      constraints: const BoxConstraints(minHeight: 26),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: background ?? (dark ? bg.withValues(alpha: .95) : bg),
        borderRadius: BorderRadius.circular(QC.rPill),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 7,
            height: 7,
            decoration: BoxDecoration(color: dot, shape: BoxShape.circle),
          ),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: fg,
                fontWeight: FontWeight.w800,
                fontSize: 12,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Grey-lavender placeholder block shown while data loads.
class SkeletonBox extends StatelessWidget {
  const SkeletonBox({
    super.key,
    this.width,
    this.height = 16,
    this.radius = 14,
  });
  final double? width;
  final double height, radius;

  @override
  Widget build(BuildContext context) => Container(
    width: width,
    height: height,
    decoration: BoxDecoration(
      color: Colors.white.withValues(alpha: .16),
      borderRadius: BorderRadius.circular(radius),
    ),
  );
}

/// Inline error with a retry button. Shows a translated, user-safe message, never raw exception text.
class ErrorState extends StatelessWidget {
  const ErrorState({
    super.key,
    required this.error,
    required this.onRetry,
    this.compact = false,
  });
  final Object error;
  final VoidCallback onRetry;

  /// Compact shows a small card for use inside a list; otherwise it fills the available space.
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final message = error is ApiException
        ? context.tr((error as ApiException).userMessageKey)
        : context.tr('Something went wrong. Please try again.');
    final retry = TextButton(
      onPressed: onRetry,
      child: Text(context.tr('Retry')),
    );
    if (compact) {
      return Surface(
        child: Row(
          children: [
            Icon(LucideIcons.cloudOff, color: context.pal.mutedOnCard),
            const SizedBox(width: 12),
            Expanded(
              child: Text(message, style: TextStyle(color: context.pal.onCard)),
            ),
            retry,
          ],
        ),
      );
    }
    return EmptyState(
      icon: LucideIcons.cloudOff,
      title: context.tr('Could not load'),
      message: message,
      actionLabel: context.tr('Retry'),
      onAction: onRetry,
    );
  }
}
