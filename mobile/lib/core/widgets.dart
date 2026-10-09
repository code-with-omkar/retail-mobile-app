import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../data/api/api_exception.dart';
import '../data/models.dart';
import '../features/cart/cart_controller.dart';
import 'l10n.dart';
import 'theme.dart';

String productName(BuildContext context, Product p) => context.isMarathi ? p.nameMr : p.name;

/// Category name in the current language. Uses the API translation when present, otherwise the app's own
/// string table (for English names such as "Dairy"), otherwise English.
String categoryLabel(BuildContext context, Category c) => context.isMarathi ? (c.hasMarathi ? c.labelMr : context.tr(c.label)) : c.label;

String productDescription(BuildContext context, Product p) => context.isMarathi ? p.descriptionMr : p.description;

/// Rounded card on the gradient (lavender in light, deep indigo in dark).
/// A form field with its label above it. Used instead of a floating label, which gets cut by the rounded field border.
class LabeledField extends StatelessWidget {
  const LabeledField({super.key, required this.label, required this.child, this.onGround = true});
  final String label;
  final Widget child;

  /// Label colour for fields on the dark ground (white) or on a card (card text colour).
  final bool onGround;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Padding(
            padding: const EdgeInsets.only(left: 6, bottom: 6),
            child: ExcludeSemantics(child: Text(label, style: TextStyle(color: onGround ? Pal.mutedOnGround : context.pal.mutedOnCard, fontSize: 13, fontWeight: FontWeight.w800))),
          ),
          Semantics(label: label, child: child),
        ]),
      );
}

class Surface extends StatelessWidget {
  const Surface({super.key, required this.child, this.padding = const EdgeInsets.all(16), this.color, this.radius = QC.rCard});
  final Widget child;
  final EdgeInsets padding;
  final Color? color;
  final double radius;

  @override
  Widget build(BuildContext context) =>
      Container(padding: padding, decoration: BoxDecoration(color: color ?? context.pal.card, borderRadius: BorderRadius.circular(radius)), child: child);
}

/// Primary yellow pill. With [arrow] it shows the ink arrow circle on the right (Get OTP, Checkout).
class PillButton extends StatelessWidget {
  const PillButton(this.label, {super.key, required this.onPressed, this.arrow = false, this.height = 60});
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
              padding: EdgeInsets.only(left: arrow ? 28 : 16, right: arrow ? 8 : 16),
              child: Row(mainAxisAlignment: arrow ? MainAxisAlignment.spaceBetween : MainAxisAlignment.center, children: [
                Flexible(child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.ink, fontSize: 18, fontWeight: FontWeight.w800))),
                if (arrow) Container(width: height - 16, height: height - 16, decoration: const BoxDecoration(color: Pal.ink, shape: BoxShape.circle), child: const Icon(Icons.arrow_forward, color: Pal.yellow)),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}

/// Secondary pill (social sign-in, secondary actions).
class SoftPillButton extends StatelessWidget {
  const SoftPillButton(this.label, {super.key, required this.onPressed, this.leading, this.height = 56});
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
          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            if (leading != null) ...[leading!, const SizedBox(width: 10)],
            Flexible(child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800, fontSize: 15))),
          ]),
        ),
      ),
    );
  }
}

/// − qty + pill: dark pill with a yellow plus, as in the reference.
class StepperPill extends StatelessWidget {
  const StepperPill({super.key, required this.qty, required this.onMinus, required this.onPlus, this.minusLabel = 'Remove one', this.plusLabel = 'Add one', this.size = 36, this.stretch = false});
  final int qty;
  final VoidCallback onMinus, onPlus;
  final String minusLabel, plusLabel;
  final double size;
  final bool stretch;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    Widget dot(IconData icon, Color bg, Color fg, String label, VoidCallback onTap) => Semantics(
          button: true,
          label: label,
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: onTap,
            child: Container(width: size, height: size, margin: const EdgeInsets.all(2), decoration: BoxDecoration(color: bg, shape: BoxShape.circle), child: Icon(icon, size: 18, color: fg)),
          ),
        );
    return Container(
      padding: const EdgeInsets.all(2),
      decoration: BoxDecoration(color: p.stepper, borderRadius: BorderRadius.circular(QC.rPill)),
      child: Row(mainAxisSize: stretch ? MainAxisSize.max : MainAxisSize.min, mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        dot(Icons.remove, p.minus, p.dark ? Pal.ink : Colors.white, minusLabel, onMinus),
        Padding(padding: EdgeInsets.symmetric(horizontal: size < 34 ? 8 : 10), child: Text('$qty', style: TextStyle(color: p.onStepper, fontWeight: FontWeight.w800, fontSize: 15))),
        dot(Icons.add, Pal.yellow, Pal.ink, plusLabel, onPlus),
      ]),
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
      return Flexible(child: Text(context.tr('Out of stock'), textAlign: TextAlign.end, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Pal.pink, fontSize: 12, fontWeight: FontWeight.w800)));
    }
    if (qty == 0) {
      return Semantics(
        button: true,
        label: '${context.tr('Add')} $name',
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => cart.add(key),
          child: SizedBox(width: QC.minTap, height: QC.minTap, child: Center(child: Container(width: 40, height: 40, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: const Icon(Icons.add, color: Pal.ink)))),
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
  Widget build(BuildContext context) => LayoutBuilder(builder: (context, c) {
        final side = c.biggest.shortestSide.isFinite ? c.biggest.shortestSide : 64.0;
        final fallback = Icon(product.icon, size: side * .5, color: Pal.ink.withValues(alpha: .7));
        final url = product.imageUrl;
        return ColoredBox(
          color: product.color,
          child: Center(child: url == null ? fallback : Image.network(url, fit: BoxFit.cover, width: double.infinity, height: double.infinity, loadingBuilder: (_, child, progress) => progress == null ? child : fallback, errorBuilder: (_, _, _) => fallback)),
        );
      });
}

class ProductThumb extends StatelessWidget {
  const ProductThumb(this.product, {super.key, this.size = 64, this.radius = 18});
  final Product product;
  final double size, radius;

  @override
  Widget build(BuildContext context) => ClipRRect(borderRadius: BorderRadius.circular(radius), child: SizedBox(width: size, height: size, child: ProductVisual(product)));
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
          padding: EdgeInsets.symmetric(horizontal: large ? 16 : 8, vertical: large ? 8 : 3),
          decoration: BoxDecoration(color: Pal.yellow, borderRadius: BorderRadius.circular(large ? 18 : 12)),
          child: Text('−$pct%', style: TextStyle(color: Pal.ink, fontWeight: FontWeight.w900, fontSize: large ? 26 : 12)),
        ),
      );
}

class PriceText extends StatelessWidget {
  const PriceText(this.price, {super.key, this.mrp, this.size = 18, this.onCard = true});
  final num price;
  final num? mrp;
  final double size;
  final bool onCard;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final color = onCard ? p.onCard : Pal.onGround;
    return Row(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.baseline, textBaseline: TextBaseline.alphabetic, children: [
      Text(rupees(price), style: TextStyle(fontWeight: FontWeight.w900, fontSize: size, color: color)),
      if (mrp != null && mrp! > price) ...[
        const SizedBox(width: 8),
        Text(rupees(mrp!), style: TextStyle(fontSize: size * .6, color: onCard ? p.mutedOnCard : Pal.mutedOnGround, decoration: TextDecoration.lineThrough)),
      ],
    ]);
  }
}

class ProductCard extends StatelessWidget {
  const ProductCard(this.product, {super.key});
  final Product product;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: () => context.push('/product/${product.id}'),
      child: Surface(
        padding: const EdgeInsets.all(8),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
          SizedBox(
            height: 80,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(22),
              child: Stack(fit: StackFit.expand, children: [
                Opacity(opacity: product.inStock ? 1 : .45, child: ProductVisual(product)),
                if (product.discountPct > 0) Positioned(top: 6, left: 6, child: DiscountSticker(product.discountPct)),
              ]),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(6, 8, 6, 0),
            child: Text.rich(
              TextSpan(children: [
                TextSpan(text: productName(context, product), style: const TextStyle(fontWeight: FontWeight.w800)),
                TextSpan(text: '  ${product.unit}', style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
              ]),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(color: p.onCard, fontSize: 14),
            ),
          ),
          Padding(
            padding: const EdgeInsets.only(left: 6, top: 2),
            child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              Flexible(child: FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: PriceText(product.price))),
              QuantityControl(product, size: 30),
            ]),
          ),
        ]),
      ),
    );
  }
}

class SectionHeader extends StatelessWidget {
  const SectionHeader(this.title, {super.key, this.action, this.onAction});
  final String title;
  final String? action;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) => Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        Flexible(child: Text(title, style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w900, color: Pal.onGround))),
        if (action != null) TextButton(onPressed: onAction, style: TextButton.styleFrom(minimumSize: const Size(48, 36), padding: const EdgeInsets.symmetric(horizontal: 8), tapTargetSize: MaterialTapTargetSize.shrinkWrap), child: Text(action!)),
      ]);
}

class EmptyState extends StatelessWidget {
  const EmptyState({super.key, required this.icon, required this.title, required this.message, this.actionLabel, this.onAction});
  final IconData icon;
  final String title, message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) => Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(32),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Container(width: 96, height: 96, decoration: const BoxDecoration(color: Pal.yellow, shape: BoxShape.circle), child: Icon(icon, size: 44, color: Pal.ink)),
            const SizedBox(height: 20),
            Text(title, textAlign: TextAlign.center, style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w900, color: Pal.onGround)),
            const SizedBox(height: 6),
            Text(message, textAlign: TextAlign.center, style: const TextStyle(color: Pal.mutedOnGround)),
            if (actionLabel != null) ...[const SizedBox(height: 24), SizedBox(width: 240, child: PillButton(actionLabel!, onPressed: onAction, height: 56))],
          ]),
        ),
      );
}

/// Label/value row used for bill breakdowns (on a card).
class BillRow extends StatelessWidget {
  const BillRow(this.label, this.value, {super.key, this.bold = false, this.accent = false});
  final String label, value;
  final bool bold, accent;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    final style = TextStyle(fontSize: bold ? 18 : 14, fontWeight: bold ? FontWeight.w900 : FontWeight.w500, color: accent ? (p.dark ? Pal.green : const Color(0xFF008A2B)) : p.onCard);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [Flexible(child: Text(label, style: style)), const SizedBox(width: 12), Text(value, style: style)]),
    );
  }
}

/// Small rounded status chip.
class Chip2 extends StatelessWidget {
  const Chip2(this.label, {super.key, this.dot, this.background, this.foreground});
  final String label;
  final Color? dot, background, foreground;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
      decoration: BoxDecoration(color: background ?? p.chip, borderRadius: BorderRadius.circular(QC.rPill), border: p.dark ? Border.all(color: p.border) : null),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        if (dot != null) ...[Container(width: 9, height: 9, decoration: BoxDecoration(color: dot, shape: BoxShape.circle)), const SizedBox(width: 8)],
        Text(label, style: TextStyle(color: foreground ?? p.onChip, fontWeight: FontWeight.w800, fontSize: 13)),
      ]),
    );
  }
}

/// Grey-lavender placeholder block shown while data loads.
class SkeletonBox extends StatelessWidget {
  const SkeletonBox({super.key, this.width, this.height = 16, this.radius = 14});
  final double? width;
  final double height, radius;

  @override
  Widget build(BuildContext context) => Container(width: width, height: height, decoration: BoxDecoration(color: Colors.white.withValues(alpha: .16), borderRadius: BorderRadius.circular(radius)));
}

/// Inline error with a retry button. Shows a translated, user-safe message, never raw exception text.
class ErrorState extends StatelessWidget {
  const ErrorState({super.key, required this.error, required this.onRetry, this.compact = false});
  final Object error;
  final VoidCallback onRetry;

  /// Compact shows a small card for use inside a list; otherwise it fills the available space.
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final message = error is ApiException ? context.tr((error as ApiException).userMessageKey) : context.tr('Something went wrong. Please try again.');
    final retry = TextButton(onPressed: onRetry, child: Text(context.tr('Retry')));
    if (compact) {
      return Surface(
        child: Row(children: [
          Icon(Icons.cloud_off_outlined, color: context.pal.mutedOnCard),
          const SizedBox(width: 12),
          Expanded(child: Text(message, style: TextStyle(color: context.pal.onCard))),
          retry,
        ]),
      );
    }
    return EmptyState(icon: Icons.cloud_off_outlined, title: context.tr('Could not load'), message: message, actionLabel: context.tr('Retry'), onAction: onRetry);
  }
}
