/// Hand-written DTOs mirroring `OrderResponse` in `QuickCommerce.Application/DTOs/OrderDtos.cs`.
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

/// The API sends the order status as a number (the enum's position) unless configured otherwise; accept both.
/// Pending, Accepted, Preparing, Ready, Completed, Rejected, Confirmed, OutForDelivery, Delivered, Cancelled.
OrderStage orderStageFrom(Object? status) {
  const names = ['Pending', 'Accepted', 'Preparing', 'Ready', 'Completed', 'Rejected', 'Confirmed', 'OutForDelivery', 'Delivered', 'Cancelled'];
  final name = status is num ? (status.toInt() >= 0 && status.toInt() < names.length ? names[status.toInt()] : null) : status as String?;
  return switch (name) {
    'Pending' => OrderStage.placed,
    'Accepted' || 'Preparing' || 'Confirmed' => OrderStage.packed,
    'Ready' || 'OutForDelivery' => OrderStage.onTheWay,
    'Completed' || 'Delivered' => OrderStage.delivered,
    'Rejected' => OrderStage.rejected,
    'Cancelled' => OrderStage.cancelled,
    _ => throw FormatException('Unknown order status "$status"'),
  };
}

OrderLine orderLineFromJson(Map<String, dynamic> j) => OrderLine(
      productId: _str(j, 'productId'),
      variantId: j['variantId'] as String?,
      name: _str(j, 'productNameSnapshot'),
      label: (j['variantLabel'] as String?) ?? '',
      unitPrice: _double(j, 'unitPrice'),
      quantity: (j['quantity'] as num).toInt(),
    );

Order orderFromJson(Map<String, dynamic> j) {
  final total = _double(j, 'totalAmount');
  return Order(
    id: _str(j, 'id'),
    number: _str(j, 'orderNumber'),
    storeId: _str(j, 'storeId'),
    lines: asList(j['items'], orderLineFromJson),
    // Orders placed before fees existed come back with a zero subtotal in some older answers: the total was the products.
    subtotal: (j['subtotalAmount'] as num?)?.toDouble() ?? total,
    deliveryFee: (j['deliveryFee'] as num?)?.toDouble() ?? 0,
    handlingFee: (j['handlingFee'] as num?)?.toDouble() ?? 0,
    total: total,
    placedAt: DateTime.parse(_str(j, 'createdAt')).toLocal(),
    stage: orderStageFrom(j['status']),
    address: _str(j, 'deliveryAddress'),
    payment: (j['paymentMethod'] as String?) ?? 'CashOnDelivery',
    receiverName: j['receiverName'] as String?,
    receiverPhone: j['receiverPhone'] as String?,
    storeName: j['storeName'] as String?,
    storePhone: (j['storePhone'] as String?)?.trim().isEmpty == true ? null : j['storePhone'] as String?,
    estimatedMinutes: (j['estimatedDeliveryMinutes'] as num?)?.toInt(),
  );
}

List<Order> ordersFromJson(Object? data) => asList(data, orderFromJson);


AppNotification notificationFromJson(Map<String, dynamic> j) => AppNotification(
      id: _str(j, 'id'),
      orderId: j['orderId'] as String?,
      type: _str(j, 'type'),
      title: _str(j, 'title'),
      message: _str(j, 'message'),
      isRead: j['isRead'] == true,
      createdAt: DateTime.parse(_str(j, 'createdAt')).toLocal(),
    );

List<AppNotification> notificationsFromJson(Object? data) => asList(data, notificationFromJson);
