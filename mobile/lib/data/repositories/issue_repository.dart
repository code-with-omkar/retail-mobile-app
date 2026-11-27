import '../api/api_client.dart';
import '../dto/catalog_dto.dart';

/// One item reported as a problem.
class IssueLine {
  const IssueLine({required this.productId, this.variantId, required this.quantity});
  final String productId;
  final String? variantId;
  final int quantity;
}

/// The customer's report and the store's answer.
class IssueReport {
  const IssueReport({required this.kind, required this.status, required this.claimedAmount, this.approvedAmount, this.decisionNote, this.cash = false, this.cashPaid = false});
  final String kind, status;
  final double claimedAmount;
  final double? approvedAmount;
  final String? decisionNote;

  /// The store pays back by hand (cash on delivery orders).
  final bool cash, cashPaid;

  factory IssueReport.fromJson(Map<String, dynamic> j) => IssueReport(
        kind: (j['kind'] as String?) ?? '',
        status: (j['status'] as String?) ?? 'Open',
        claimedAmount: (j['claimedAmount'] as num?)?.toDouble() ?? 0,
        approvedAmount: (j['approvedAmount'] as num?)?.toDouble(),
        decisionNote: j['decisionNote'] as String?,
        cash: j['refundMethod'] == 'Cash',
        cashPaid: j['cashPaidAt'] != null,
      );
}

/// What an order's screen needs: the report already made, or until when one can be made.
class OrderIssue {
  const OrderIssue({this.report, this.reportUntil});
  final IssueReport? report;
  final DateTime? reportUntil;

  factory OrderIssue.fromJson(Map<String, dynamic> j) => OrderIssue(
        report: j['issue'] is Map ? IssueReport.fromJson(asObject(j['issue'])) : null,
        reportUntil: j['reportUntil'] is String ? DateTime.parse(j['reportUntil'] as String).toLocal() : null,
      );
}

/// Problems with a delivered order (`api/customer/orders/{id}/issue`).
abstract interface class IssueRepository {
  Future<OrderIssue> mine(String orderId);
  Future<void> report(String orderId, {required String kind, required List<IssueLine> lines, String note});
}

class ApiIssueRepository implements IssueRepository {
  ApiIssueRepository(this._api);
  final ApiClient _api;

  @override
  Future<OrderIssue> mine(String orderId) => _api.get('/api/customer/orders/$orderId/issue', parse: (d) => OrderIssue.fromJson(asObject(d)));

  @override
  Future<void> report(String orderId, {required String kind, required List<IssueLine> lines, String note = ''}) => _api.post(
        '/api/customer/orders/$orderId/issue',
        body: {
          'kind': kind,
          'note': note.isEmpty ? null : note,
          'items': [for (final l in lines) {'productId': l.productId, 'variantId': l.variantId, 'quantity': l.quantity}],
        },
        parse: (_) {},
      );
}

/// Offline demo data has no reports.
class LocalIssueRepository implements IssueRepository {
  @override
  Future<OrderIssue> mine(String orderId) async => const OrderIssue();

  @override
  Future<void> report(String orderId, {required String kind, required List<IssueLine> lines, String note = ''}) async {}
}
