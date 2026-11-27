import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../../data/repositories/issue_repository.dart';

/// The report made about an order (or until when one can be made). A failure just means the section is not shown.
final orderIssueProvider = FutureProvider.autoDispose.family<OrderIssue, String>((ref, orderId) async {
  try {
    return await ref.watch(issueRepositoryProvider).mine(orderId);
  } catch (_) {
    return const OrderIssue();
  }
});

String issueKindName(BuildContext context, String kind) => switch (kind) {
      'Missing' => context.tr('Item missing'),
      'Damaged' => context.tr('Item damaged'),
      'WrongItem' => context.tr('Wrong item'),
      _ => context.tr('Poor quality'),
    };

/// On a delivered order: "Report a problem" while there is time, then what the store decided.
class OrderIssueSection extends ConsumerWidget {
  const OrderIssueSection(this.order, {super.key});
  final Order order;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (order.stage != OrderStage.delivered) return const SizedBox.shrink();
    final issue = ref.watch(orderIssueProvider(order.id)).value;
    if (issue == null || (issue.report == null && issue.reportUntil == null)) return const SizedBox.shrink();
    final p = context.pal;
    final report = issue.report;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Surface(
        child: report == null
            ? Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(context.tr('Something wrong with this order?'), style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 15)),
                const SizedBox(height: 4),
                Text(context.tr('You can report it within 48 hours of delivery.'), style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                const SizedBox(height: 10),
                PillButton(
                  context.tr('Report a problem'),
                  onPressed: () async {
                    await Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ReportProblemScreen(order)));
                    ref.invalidate(orderIssueProvider(order.id));
                  },
                ),
              ])
            : Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(
                  switch (report.status) {
                    'Approved' => context.tr('Your report was accepted'),
                    'Refused' => context.tr('Your report was not accepted'),
                    _ => context.tr('Report sent. The store will look at it.'),
                  },
                  style: TextStyle(color: p.onCard, fontWeight: FontWeight.w900, fontSize: 15),
                ),
                const SizedBox(height: 4),
                Text('${issueKindName(context, report.kind)} · ${rupees(report.claimedAmount)}', style: TextStyle(color: p.mutedOnCard, fontSize: 12)),
                if (report.status == 'Approved' && report.approvedAmount != null)
                  Text(
                    report.cash
                        ? (report.cashPaid ? context.tr('{amount} paid back to you', {'amount': rupees(report.approvedAmount!)}) : context.tr('The store will pay you back {amount}', {'amount': rupees(report.approvedAmount!)}))
                        : context.tr('{amount} is being refunded to your payment', {'amount': rupees(report.approvedAmount!)}),
                    style: const TextStyle(color: Pal.green, fontWeight: FontWeight.w800),
                  ),
                if (report.decisionNote != null) Text(report.decisionNote!, style: TextStyle(color: p.onCard, fontSize: 13)),
              ]),
      ),
    );
  }
}

/// Which items, what went wrong, and a note.
class ReportProblemScreen extends ConsumerStatefulWidget {
  const ReportProblemScreen(this.order, {super.key});
  final Order order;

  @override
  ConsumerState<ReportProblemScreen> createState() => _ReportProblemScreenState();
}

class _ReportProblemScreenState extends ConsumerState<ReportProblemScreen> {
  final _note = TextEditingController();
  final _chosen = <int, int>{}; // line index -> quantity
  String _kind = 'Missing';
  String? _error;
  bool _busy = false;

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (_busy) return;
    if (_chosen.isEmpty) {
      setState(() => _error = context.tr('Choose at least one item.'));
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(issueRepositoryProvider).report(widget.order.id, kind: _kind, note: _note.text.trim(), lines: [
        for (final entry in _chosen.entries) IssueLine(productId: widget.order.lines[entry.key].productId, variantId: widget.order.lines[entry.key].variantId, quantity: entry.value),
      ]);
      if (mounted) Navigator.of(context).pop();
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final lines = widget.order.lines;
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('Report a problem'))),
      body: ListView(padding: const EdgeInsets.all(QC.gutter), children: [
        Wrap(spacing: 8, children: [
          for (final kind in const ['Missing', 'Damaged', 'WrongItem', 'Quality'])
            ChoiceChip(label: Text(issueKindName(context, kind)), selected: _kind == kind, onSelected: (_) => setState(() => _kind = kind)),
        ]),
        const SizedBox(height: 12),
        for (var i = 0; i < lines.length; i++)
          CheckboxListTile(
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            value: _chosen.containsKey(i),
            onChanged: (on) => setState(() => on == true ? _chosen[i] = lines[i].quantity : _chosen.remove(i)),
            title: Text(lines[i].name),
            secondary: _chosen.containsKey(i) && lines[i].quantity > 1
                ? DropdownButton<int>(
                    value: _chosen[i],
                    items: [for (var q = 1; q <= lines[i].quantity; q++) DropdownMenuItem(value: q, child: Text('× $q'))],
                    onChanged: (q) => setState(() => _chosen[i] = q ?? 1),
                  )
                : null,
          ),
        TextField(controller: _note, maxLength: 500, maxLines: 3, decoration: InputDecoration(labelText: context.tr('Tell the store what happened (optional)'))),
        if (_error != null) Padding(padding: const EdgeInsets.only(bottom: 10), child: Text(_error!, style: const TextStyle(color: Pal.pink, fontWeight: FontWeight.w800))),
        PillButton(_busy ? context.tr('Sending…') : context.tr('Send report'), onPressed: _busy ? null : _send),
      ]),
    );
  }
}
