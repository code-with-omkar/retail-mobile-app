import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/api/api_exception.dart';
import '../../data/providers.dart';

/// Debug-only screen (route and entry point exist only when `kDebugMode`) that proves the app can
/// reach the configured API: health endpoints and the public categories endpoint.
/// Developer text is intentionally not translated.
class ApiDiagnosticsScreen extends ConsumerStatefulWidget {
  const ApiDiagnosticsScreen({super.key});
  @override
  ConsumerState<ApiDiagnosticsScreen> createState() => _ApiDiagnosticsScreenState();
}

class _ApiDiagnosticsScreenState extends ConsumerState<ApiDiagnosticsScreen> {
  final _lines = <(bool, String)>[];
  bool _running = false;

  Future<void> _run() async {
    setState(() {
      _running = true;
      _lines.clear();
    });
    void add(bool ok, String text) => setState(() => _lines.add((ok, text)));
    try {
      final health = await ref.read(healthRepositoryProvider).check();
      add(health.liveOk, 'GET /health/live');
      add(health.readyOk, 'GET /health/ready (${health.readyBody.isEmpty ? 'no body' : health.readyBody})');
    } on ApiException catch (e) {
      add(false, 'Health check failed: $e');
    }
    try {
      final cats = await ref.read(catalogRepositoryProvider).categories();
      add(true, 'GET /api/categories -> ${cats.length} categories: ${cats.take(5).map((c) => c.label).join(', ')}');
    } on ApiException catch (e) {
      add(false, 'Categories failed: $e');
    }
    if (mounted) setState(() => _running = false);
  }

  @override
  Widget build(BuildContext context) {
    final config = ref.watch(appConfigProvider);
    final p = context.pal;
    return AppScaffold(
      appBar: appTopBar(context, 'API diagnostics'),
      body: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), children: [
        Surface(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('Environment: ${config.env.name}', style: TextStyle(color: p.onCard, fontWeight: FontWeight.w800)),
            const SizedBox(height: 4),
            Text(config.apiBaseUrl, style: TextStyle(color: p.mutedOnCard)),
          ]),
        ),
        const SizedBox(height: 12),
        PillButton(_running ? 'Running…' : 'Run checks', onPressed: _running ? null : _run, height: 56),
        const SizedBox(height: 12),
        for (final (ok, text) in _lines)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Surface(
              padding: const EdgeInsets.all(14),
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Icon(ok ? Icons.check_circle : Icons.error, color: ok ? const Color(0xFF008A2B) : Pal.pink),
                const SizedBox(width: 10),
                Expanded(child: Text(text, style: TextStyle(color: p.onCard))),
              ]),
            ),
          ),
      ]),
    );
  }
}
