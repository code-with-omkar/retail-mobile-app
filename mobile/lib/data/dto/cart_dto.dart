/// Hand-written DTOs mirroring `QuickCommerce.Application/DTOs/CartDtos.cs` and `CheckoutDtos.cs`.
library;

import '../models.dart';
import 'catalog_dto.dart';

String _str(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is String) return v;
  throw FormatException('Field "$key" expected a string but was ${v.runtimeType}');
}

double _double(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is num) return v.toDouble();
  throw FormatException('Field "$key" expected a number but was ${v.runtimeType}');
}

int _int(Map<String, dynamic> j, String key) {
  final v = j[key];
  if (v is num) return v.toInt();
  throw FormatException('Field "$key" expected a number but was ${v.runtimeType}');
}

double? _doubleOrNull(Object? v) => v is num ? v.toDouble() : null;

ServerCartLine serverCartLineFromJson(Map<String, dynamic> j) => ServerCartLine(
      productId: _str(j, 'productId'),
      variantId: j['variantId'] as String?,
      name: _str(j, 'productNameSnapshot'),
      label: (j['variantLabel'] as String?) ?? '',
      quantity: _int(j, 'quantity'),
      unitPrice: _double(j, 'unitPriceSnapshot'),
      currentUnitPrice: _doubleOrNull(j['currentUnitPrice']),
      available: (j['available'] as num?)?.toInt(),
      unavailable: j['unavailable'] == true,
    );

/// `GET/POST/PUT/DELETE api/carts/...`: the cart with the server's fees.
ServerCart serverCartFromJson(Map<String, dynamic> j) => ServerCart(
      storeId: _str(j, 'storeId'),
      lines: asList(j['items'], serverCartLineFromJson),
      pricing: CartPricing(
        subtotal: _double(j, 'subtotal'),
        deliveryFee: _double(j, 'deliveryFee'),
        handlingFee: _double(j, 'handlingFee'),
        total: _double(j, 'total'),
        freeDeliveryThreshold: _double(j, 'freeDeliveryThreshold'),
        amountToFreeDelivery: _double(j, 'amountToFreeDelivery'),
      ),
    );

CartNote cartNoteFromJson(Map<String, dynamic> j) => CartNote(
      productId: _str(j, 'productId'),
      variantId: j['variantId'] as String?,
      name: (j['name'] as String?) ?? '',
      label: (j['label'] as String?) ?? '',
      kind: _str(j, 'kind'),
      quantity: _int(j, 'quantity'),
    );

/// `GET api/catalog/pricing`.
PricingSettings pricingFromJson(Map<String, dynamic> j) =>
    PricingSettings(deliveryFee: _double(j, 'deliveryFee'), handlingFee: _double(j, 'handlingFee'), freeDeliveryThreshold: _double(j, 'freeDeliveryThreshold'));

/// One of the `details` of a failed checkout. Unknown extra fields are ignored.
CheckoutIssue checkoutIssueFromJson(Map<String, dynamic> j) => CheckoutIssue(
      productId: _str(j, 'productId'),
      variantId: j['variantId'] as String?,
      name: (j['name'] as String?) ?? '',
      label: (j['label'] as String?) ?? '',
      quantity: _int(j, 'quantity'),
      available: (j['available'] as num?)?.toInt(),
      oldPrice: _doubleOrNull(j['oldPrice']),
      newPrice: _doubleOrNull(j['newPrice']),
    );

/// The issues in a 409's `details`; anything unreadable is skipped rather than hiding the real problem.
List<CheckoutIssue> checkoutIssuesFrom(List<Object?> details) {
  final issues = <CheckoutIssue>[];
  for (final d in details) {
    if (d is Map<String, dynamic>) {
      try {
        issues.add(checkoutIssueFromJson(d));
      } on FormatException {
        // skip
      }
    }
  }
  return issues;
}
