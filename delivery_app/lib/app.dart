import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';

import 'api.dart';

const _ink = Color(0xFF141118);
const _yellow = Color(0xFFFFD400);
const _green = Color(0xFF02F34C);

typedef OpenUri = Future<bool> Function(Uri uri);

Future<bool> _defaultOpen(Uri uri) => launchUrl(uri);

String rupees(double v) => '₹${v == v.roundToDouble() ? v.toStringAsFixed(0) : v.toStringAsFixed(2)}';

class PartnerApp extends StatelessWidget {
  const PartnerApp({super.key, required this.api, this.open = _defaultOpen, this.pollEvery = const Duration(seconds: 20)});
  final DeliveryApi api;
  final OpenUri open;
  final Duration pollEvery;

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'QuickCart Delivery',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: _yellow, primary: _ink, onPrimary: Colors.white, secondary: _yellow, brightness: Brightness.light),
          useMaterial3: true,
          filledButtonTheme: FilledButtonThemeData(style: FilledButton.styleFrom(backgroundColor: _ink, foregroundColor: Colors.white, minimumSize: const Size.fromHeight(52), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(26)))),
          inputDecorationTheme: InputDecorationTheme(border: OutlineInputBorder(borderRadius: BorderRadius.circular(16))),
        ),
        home: _Gate(api: api, open: open, pollEvery: pollEvery),
      );
}

class _Gate extends StatefulWidget {
  const _Gate({required this.api, required this.open, required this.pollEvery});
  final DeliveryApi api;
  final OpenUri open;
  final Duration pollEvery;
  @override
  State<_Gate> createState() => _GateState();
}

class _GateState extends State<_Gate> {
  bool _checking = true;
  bool _signedIn = false;

  @override
  void initState() {
    super.initState();
    widget.api.restore().then((ok) {
      if (mounted) setState(() { _signedIn = ok; _checking = false; });
    }).catchError((_) {
      if (mounted) setState(() => _checking = false);
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_checking) return const Scaffold(body: Center(child: CircularProgressIndicator()));
    return _signedIn
        ? DeliveriesScreen(api: widget.api, open: widget.open, pollEvery: widget.pollEvery, onSignedOut: () => setState(() => _signedIn = false))
        : LoginScreen(api: widget.api, onSignedIn: () => setState(() => _signedIn = true));
  }
}

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.api, required this.onSignedIn});
  final DeliveryApi api;
  final VoidCallback onSignedIn;
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _user = TextEditingController();
  final _pass = TextEditingController();
  String? _error;
  bool _busy = false;

  Future<void> _go() async {
    if (_busy) return;
    if (_user.text.trim().isEmpty || _pass.text.isEmpty) {
      setState(() => _error = 'Enter your username and password.');
      return;
    }

    setState(() { _busy = true; _error = null; });
    try {
      await widget.api.signIn(_user.text, _pass.text);
      if (mounted) widget.onSignedIn();
    } on ApiError catch (e) {
      if (mounted) setState(() { _busy = false; _error = e.message; });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        body: SafeArea(
          child: ListView(padding: const EdgeInsets.all(24), children: [
            const SizedBox(height: 48),
            const Icon(Icons.delivery_dining, size: 64),
            const SizedBox(height: 12),
            Text('QuickCart Delivery', style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w900)),
            const SizedBox(height: 32),
            TextField(controller: _user, key: const Key('username'), decoration: const InputDecoration(labelText: 'Username or email'), keyboardType: TextInputType.emailAddress, autocorrect: false),
            const SizedBox(height: 16),
            TextField(controller: _pass, key: const Key('password'), decoration: const InputDecoration(labelText: 'Password'), obscureText: true, onSubmitted: (_) => _go()),
            if (_error != null) Padding(padding: const EdgeInsets.only(top: 12), child: Text(_error!, key: const Key('error'), style: const TextStyle(color: Color(0xFFB00060), fontWeight: FontWeight.w700))),
            const SizedBox(height: 24),
            FilledButton(onPressed: _busy ? null : _go, child: Text(_busy ? 'Signing in…' : 'Sign in')),
          ]),
        ),
      );
}

class DeliveriesScreen extends StatefulWidget {
  const DeliveriesScreen({super.key, required this.api, required this.open, required this.pollEvery, required this.onSignedOut});
  final DeliveryApi api;
  final OpenUri open;
  final Duration pollEvery;
  final VoidCallback onSignedOut;
  @override
  State<DeliveriesScreen> createState() => _DeliveriesScreenState();
}

class _DeliveriesScreenState extends State<DeliveriesScreen> {
  List<Delivery>? _items;
  String? _error;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _load();
    _timer = Timer.periodic(widget.pollEvery, (_) => _load());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final list = await widget.api.mine();
      if (mounted) setState(() { _items = list; _error = null; });
    } on ApiError catch (e) {
      if (!mounted) return;
      if (e.status == 401) {
        widget.onSignedOut();
        return;
      }
      setState(() => _error = e.message);
    }
  }

  Future<void> _signOut() async {
    await widget.api.signOut();
    if (mounted) widget.onSignedOut();
  }

  @override
  Widget build(BuildContext context) {
    final items = _items;
    final todo = items?.where((d) => !d.isDone).toList() ?? const <Delivery>[];
    final done = items?.where((d) => d.isDone).toList() ?? const <Delivery>[];
    return Scaffold(
      appBar: AppBar(title: const Text('My deliveries'), actions: [IconButton(tooltip: 'Sign out', onPressed: _signOut, icon: const Icon(Icons.logout))]),
      body: RefreshIndicator(
        onRefresh: _load,
        child: items == null && _error == null
            ? ListView(children: const [SizedBox(height: 200), Center(child: CircularProgressIndicator())])
            : ListView(padding: const EdgeInsets.all(16), children: [
                if (_error != null) Padding(padding: const EdgeInsets.only(bottom: 12), child: Text(_items == null ? _error! : 'Could not refresh. Showing the last update.', key: const Key('list-error'), style: const TextStyle(color: Color(0xFFB00060), fontWeight: FontWeight.w700))),
                if (items != null && items.isEmpty) const Padding(padding: EdgeInsets.only(top: 80), child: Center(child: Text('Nothing to deliver right now.'))),
                if (todo.isNotEmpty) const _Heading('To do'),
                for (final d in todo) _Card(d, () => _openDetail(d)),
                if (done.isNotEmpty) const _Heading('Delivered in the last day'),
                for (final d in done) _Card(d, () => _openDetail(d)),
              ]),
      ),
    );
  }

  Future<void> _openDetail(Delivery d) async {
    await Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => DetailScreen(delivery: d, api: widget.api, open: widget.open)));
    await _load();
  }
}

class _Heading extends StatelessWidget {
  const _Heading(this.text);
  final String text;
  @override
  Widget build(BuildContext context) => Padding(padding: const EdgeInsets.fromLTRB(4, 8, 4, 8), child: Text(text, style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 16)));
}

class _Card extends StatelessWidget {
  const _Card(this.d, this.onTap);
  final Delivery d;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) => Card(
        margin: const EdgeInsets.only(bottom: 12),
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(12),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Expanded(child: Text('#${d.number}', style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 16))),
                _Chip(d.isReady ? 'Pick up' : d.isOut ? 'On the way' : 'Delivered', d.isDone ? _green : _yellow),
              ]),
              const SizedBox(height: 8),
              Text(d.receiverName ?? 'Customer', style: const TextStyle(fontWeight: FontWeight.w700)),
              Text(d.address, maxLines: 2, overflow: TextOverflow.ellipsis),
              const SizedBox(height: 8),
              Text(d.cashToCollect > 0 ? 'Collect ${rupees(d.cashToCollect)} cash' : 'Paid online', style: const TextStyle(fontWeight: FontWeight.w800)),
            ]),
          ),
        ),
      );
}

class _Chip extends StatelessWidget {
  const _Chip(this.label, this.color);
  final String label;
  final Color color;
  @override
  Widget build(BuildContext context) => Container(padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4), decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(999)), child: Text(label, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 12, color: _ink)));
}

class DetailScreen extends StatefulWidget {
  const DetailScreen({super.key, required this.delivery, required this.api, required this.open});
  final Delivery delivery;
  final DeliveryApi api;
  final OpenUri open;
  @override
  State<DetailScreen> createState() => _DetailScreenState();
}

class _DetailScreenState extends State<DetailScreen> {
  late Delivery d = widget.delivery;
  final _otp = TextEditingController();
  late final _cash = TextEditingController(text: d.cashToCollect > 0 ? d.cashToCollect.toStringAsFixed(d.cashToCollect == d.cashToCollect.roundToDouble() ? 0 : 2) : '0');
  String? _error;
  bool _busy = false;

  Future<void> _run(Future<Delivery> Function() action) async {
    if (_busy) return;
    setState(() { _busy = true; _error = null; });
    try {
      final next = await action();
      if (mounted) setState(() { d = next; _busy = false; });
    } on ApiError catch (e) {
      if (mounted) setState(() { _busy = false; _error = e.message; });
    }
  }

  Future<void> _call(String? phone) async {
    if (phone == null) return;
    if (!await widget.open(Uri(scheme: 'tel', path: phone)) && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not open the phone app.')));
    }
  }

  Future<void> _navigate() async {
    final uri = Uri.parse('geo:${d.latitude},${d.longitude}?q=${d.latitude},${d.longitude}(${Uri.encodeComponent(d.address)})');
    if (!await widget.open(uri) && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No maps app found.')));
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text('#${d.number}')),
        body: ListView(padding: const EdgeInsets.all(16), children: [
          Row(children: [_Chip(d.isReady ? 'Pick up at the store' : d.isOut ? 'On the way' : 'Delivered', d.isDone ? _green : _yellow)]),
          const SizedBox(height: 16),
          _Block('Pick up from', [Text(d.storeName, style: const TextStyle(fontWeight: FontWeight.w800)), if (d.storePhone != null) TextButton.icon(onPressed: () => _call(d.storePhone), icon: const Icon(Icons.call_outlined), label: const Text('Call store'))]),
          _Block('Deliver to', [
            Text(d.receiverName ?? 'Customer', style: const TextStyle(fontWeight: FontWeight.w800)),
            Text(d.address),
            Wrap(spacing: 8, children: [
              if (d.receiverPhone != null) TextButton.icon(onPressed: () => _call(d.receiverPhone), icon: const Icon(Icons.call_outlined), label: const Text('Call customer')),
              TextButton.icon(onPressed: _navigate, icon: const Icon(Icons.navigation_outlined), label: const Text('Navigate')),
            ]),
          ]),
          _Block('Items', [for (final i in d.items) Text(i)]),
          _Block('Payment', [Text(d.cashToCollect > 0 ? 'Collect ${rupees(d.cashToCollect)} in cash' : 'Paid online — collect nothing', key: const Key('cash-line'), style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 16))]),
          const SizedBox(height: 8),
          if (d.isReady) FilledButton(key: const Key('pickup'), onPressed: _busy ? null : () => _run(() => widget.api.pickUp(d.orderId)), child: Text(_busy ? 'Please wait…' : 'I picked it up')),
          if (d.isOut) ...[
            TextField(
              key: const Key('otp'),
              controller: _otp,
              decoration: const InputDecoration(labelText: 'Code from the customer', helperText: 'The 4 digits shown in their app'),
              keyboardType: TextInputType.number,
              maxLength: 4,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly],
            ),
            if (d.cashToCollect > 0)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: TextField(key: const Key('cash'), controller: _cash, decoration: const InputDecoration(labelText: 'Cash collected (₹)'), keyboardType: const TextInputType.numberWithOptions(decimal: true)),
              ),
            FilledButton(
              key: const Key('deliver'),
              onPressed: _busy ? null : () => _run(() => widget.api.deliver(d.orderId, otp: _otp.text, cash: d.cashToCollect > 0 ? double.tryParse(_cash.text) ?? -1 : 0)),
              child: Text(_busy ? 'Please wait…' : 'Delivered'),
            ),
          ],
          if (d.isDone) const Padding(padding: EdgeInsets.only(top: 8), child: Text('This delivery is complete.', style: TextStyle(fontWeight: FontWeight.w800))),
          if (_error != null) Padding(padding: const EdgeInsets.only(top: 12), child: Text(_error!, key: const Key('error'), style: const TextStyle(color: Color(0xFFB00060), fontWeight: FontWeight.w700))),
        ]),
      );
}

class _Block extends StatelessWidget {
  const _Block(this.title, this.children);
  final String title;
  final List<Widget> children;
  @override
  Widget build(BuildContext context) => Card(
        margin: const EdgeInsets.only(bottom: 12),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Text(title, style: const TextStyle(fontSize: 12, color: Colors.black54)), const SizedBox(height: 4), ...children]),
        ),
      );
}
