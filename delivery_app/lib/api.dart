import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// What went wrong, in words the partner can act on. [reason] is the server's short code (`WrongCode`, `Locked`) when it sent one.
class ApiError implements Exception {
  ApiError(this.message, {this.reason, this.status});
  final String message;
  final String? reason;
  final int? status;

  bool get isWrongCode => reason == 'WrongCode';
  bool get isLocked => reason == 'Locked';
  @override
  String toString() => message;
}

class Delivery {
  Delivery({
    required this.orderId,
    required this.number,
    required this.status,
    required this.storeName,
    required this.storePhone,
    required this.address,
    required this.latitude,
    required this.longitude,
    required this.receiverName,
    required this.receiverPhone,
    required this.payment,
    required this.total,
    required this.cashToCollect,
    required this.items,
    required this.deliveredAt,
  });

  final String orderId, number, storeName, address, payment;
  final String? storePhone, receiverName, receiverPhone;

  /// Ready (to pick up at the store), OutForDelivery or Delivered.
  final String status;
  final double latitude, longitude, total, cashToCollect;
  final List<String> items;
  final DateTime? deliveredAt;

  bool get isReady => status == 'Ready';
  bool get isOut => status == 'OutForDelivery';
  bool get isDone => status == 'Delivered';

  static const _names = ['Pending', 'Accepted', 'Preparing', 'Ready', 'Completed', 'Rejected', 'Confirmed', 'OutForDelivery', 'Delivered', 'Cancelled', 'AwaitingPayment'];

  factory Delivery.fromJson(Map<String, dynamic> j) {
    final raw = j['status'];
    return Delivery(
      orderId: j['orderId'] as String,
      number: j['orderNumber'] as String,
      status: raw is num ? _names[raw.toInt()] : '$raw',
      storeName: (j['storeName'] as String?) ?? '',
      storePhone: j['storePhone'] as String?,
      address: (j['deliveryAddress'] as String?) ?? '',
      latitude: (j['latitude'] as num?)?.toDouble() ?? 0,
      longitude: (j['longitude'] as num?)?.toDouble() ?? 0,
      receiverName: j['receiverName'] as String?,
      receiverPhone: j['receiverPhone'] as String?,
      payment: (j['paymentMethod'] as String?) ?? 'CashOnDelivery',
      total: (j['totalAmount'] as num?)?.toDouble() ?? 0,
      cashToCollect: (j['cashToCollect'] as num?)?.toDouble() ?? 0,
      items: [for (final i in (j['items'] as List? ?? const [])) '${(i as Map)['productNameSnapshot']}${(i['variantLabel'] as String?)?.isNotEmpty == true ? ' ${i['variantLabel']}' : ''} × ${i['quantity']}'],
      deliveredAt: j['deliveredAt'] == null ? null : DateTime.parse(j['deliveredAt'] as String).toLocal(),
    );
  }
}

abstract class DeliveryApi {
  Future<bool> restore();
  Future<String> signIn(String username, String password);
  Future<void> signOut();
  Future<List<Delivery>> mine();
  Future<Delivery> pickUp(String orderId);

  /// Tells the server where the partner is while an order is out for delivery (the customer sees distance and ETA only).
  Future<void> shareLocation(String orderId, double latitude, double longitude);
  Future<Delivery> deliver(String orderId, {required String otp, required double cash});
}

/// Keeps the refresh token between launches.
abstract class TokenStore {
  Future<String?> read();
  Future<void> write(String? token);
}

class SecureTokenStore implements TokenStore {
  static const _key = 'refresh_token';
  final _storage = const FlutterSecureStorage();
  @override
  Future<String?> read() => _storage.read(key: _key);
  @override
  Future<void> write(String? token) => token == null ? _storage.delete(key: _key) : _storage.write(key: _key, value: token);
}

class HttpDeliveryApi implements DeliveryApi {
  HttpDeliveryApi({required String baseUrl, required this.tokens, Dio? dio}) : _dio = dio ?? Dio(BaseOptions(baseUrl: baseUrl, connectTimeout: const Duration(seconds: 10), receiveTimeout: const Duration(seconds: 20)));

  final Dio _dio;
  final TokenStore tokens;
  String? _access;

  Never _fail(DioException e) {
    final body = e.response?.data;
    final message = body is Map && body['message'] is String ? body['message'] as String : (e.response?.statusCode == 403 ? 'This account is not a delivery partner.' : 'Could not reach the server. Check your connection.');
    throw ApiError(message, reason: body is Map ? body['reason'] as String? : null, status: e.response?.statusCode);
  }

  Future<String> _adopt(Response<dynamic> r) async {
    final data = (r.data as Map)['data'] as Map;
    final roles = ((data['user'] as Map)['roles'] as List?)?.cast<String>() ?? const [];
    if (!roles.contains('DeliveryPartner')) {
      throw ApiError('This account is not a delivery partner.');
    }

    _access = data['accessToken'] as String;
    await tokens.write(data['refreshToken'] as String);
    return (data['user'] as Map)['displayName'] as String;
  }

  Future<bool> _refresh() async {
    final token = await tokens.read();
    if (token == null) return false;
    try {
      await _adopt(await _dio.post('/api/auth/refresh', data: {'refreshToken': token}));
      return true;
    } catch (_) {
      await tokens.write(null);
      _access = null;
      return false;
    }
  }

  @override
  Future<bool> restore() => _refresh();

  @override
  Future<String> signIn(String username, String password) async {
    try {
      return await _adopt(await _dio.post('/api/auth/login', data: {'username': username.trim(), 'password': password}));
    } on DioException catch (e) {
      if (e.response?.statusCode == 401) throw ApiError('Wrong username or password.');
      _fail(e);
    }
  }

  @override
  Future<void> signOut() async {
    final token = await tokens.read();
    try {
      if (token != null && _access != null) {
        await _dio.post('/api/auth/logout', data: {'refreshToken': token}, options: Options(headers: {'Authorization': 'Bearer $_access'}));
      }
    } catch (_) {
      // Signing out always works on this phone.
    }

    _access = null;
    await tokens.write(null);
  }

  Future<Response<dynamic>> _send(String method, String path, {Object? body}) async {
    for (var attempt = 0; ; attempt++) {
      try {
        return await _dio.request(path, data: body, options: Options(method: method, headers: {'Authorization': 'Bearer $_access'}));
      } on DioException catch (e) {
        if (e.response?.statusCode == 401 && attempt == 0 && await _refresh()) continue;
        _fail(e);
      }
    }
  }

  @override
  Future<List<Delivery>> mine() async {
    final r = await _send('GET', '/api/delivery/mine');
    return [for (final j in (r.data as Map)['data'] as List) Delivery.fromJson(j as Map<String, dynamic>)];
  }

  @override
  Future<void> shareLocation(String orderId, double latitude, double longitude) async {
    await _send('PUT', '/api/delivery/$orderId/location', body: {'latitude': latitude, 'longitude': longitude});
  }

  @override
  Future<Delivery> pickUp(String orderId) async => Delivery.fromJson(((await _send('POST', '/api/delivery/$orderId/pickup')).data as Map)['data'] as Map<String, dynamic>);

  @override
  Future<Delivery> deliver(String orderId, {required String otp, required double cash}) async =>
      Delivery.fromJson(((await _send('POST', '/api/delivery/$orderId/deliver', body: {'otp': otp, 'cashCollected': cash})).data as Map)['data'] as Map<String, dynamic>);
}
