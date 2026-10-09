import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/l10n.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../data/location_services.dart';
import '../../data/models.dart';
import '../../data/providers.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_widgets.dart';
import 'address_controller.dart';
import 'delivery_actions.dart';

/// Adds or changes a saved address (signed in), or sets this device's delivery place (guest).
/// The place is found by typing an address and looking it up, by "use my current location", or by entering coordinates.
/// The map picker (task 4.4) will sit in the location card; everything around it already works without it.
class AddressEditorScreen extends ConsumerStatefulWidget {
  const AddressEditorScreen({super.key, this.args});
  final AddressEditorArgs? args;

  @override
  ConsumerState<AddressEditorScreen> createState() => _AddressEditorScreenState();
}

class _AddressEditorScreenState extends ConsumerState<AddressEditorScreen> {
  static const _labels = ['Home', 'Work', 'Other'];

  final _form = GlobalKey<FormState>();
  late final _line = TextEditingController(text: widget.args?.existing?.line ?? widget.args?.line ?? '');
  late final _flat = TextEditingController(text: widget.args?.existing?.flatOrBuilding ?? '');
  late final _landmark = TextEditingController(text: widget.args?.existing?.landmark ?? '');
  late final TextEditingController _name, _phone;
  final _otherLabel = TextEditingController();
  final _latText = TextEditingController();
  final _lngText = TextEditingController();

  late String _label = _initialLabel();
  late double? _lat = widget.args?.existing?.latitude ?? widget.args?.latitude;
  late double? _lng = widget.args?.existing?.longitude ?? widget.args?.longitude;
  ServiceabilityResult? _service;
  int _checkId = 0;
  bool _busy = false, _locating = false, _finding = false;
  String? _error, _locationError;

  bool get _editing => widget.args?.existing != null;
  bool get _hasPoint => _lat != null && _lng != null;

  String _initialLabel() {
    final label = widget.args?.existing?.label;
    if (label == null) return 'Home';
    if (_labels.contains(label)) return label;
    _otherLabel.text = label;
    return 'Other';
  }

  @override
  void initState() {
    super.initState();
    final user = ref.read(authProvider).user;
    _name = TextEditingController(text: widget.args?.existing?.receiverName ?? user?.fullName ?? '');
    _phone = TextEditingController(text: widget.args?.existing?.receiverPhone ?? user?.phoneNumber ?? '');
    if (_hasPoint) _checkService();
  }

  @override
  void dispose() {
    for (final c in [_line, _flat, _landmark, _name, _phone, _otherLabel, _latText, _lngText]) {
      c.dispose();
    }
    super.dispose();
  }

  void _setPoint(double lat, double lng) {
    setState(() {
      _lat = lat;
      _lng = lng;
      _locationError = null;
    });
    _checkService();
  }

  Future<void> _checkService() async {
    final id = ++_checkId;
    setState(() => _service = null);
    try {
      final answer = await ref.read(addressRepositoryProvider).serviceability(latitude: roundCoordinate(_lat!), longitude: roundCoordinate(_lng!));
      if (mounted && id == _checkId) setState(() => _service = answer);
    } catch (_) {
      // The banner is a courtesy: without it the address can still be saved and the server checks again at checkout.
    }
  }

  Future<void> _useMyLocation() async {
    if (_locating) return;
    setState(() => _locating = true);
    try {
      final found = await locateMe(context, ref);
      if (found == null || !mounted) return;
      _line.text = found.line;
      _setPoint(found.latitude, found.longitude);
    } finally {
      if (mounted) setState(() => _locating = false);
    }
  }

  Future<void> _findAddress() async {
    final text = _line.text.trim();
    if (text.isEmpty || _finding) {
      setState(() => _locationError = context.tr('Type the address first.'));
      return;
    }
    setState(() {
      _finding = true;
      _locationError = null;
    });
    try {
      final found = await ref.read(geocodingServiceProvider).forward(text);
      if (!mounted) return;
      if (found == null) {
        setState(() => _locationError = context.tr('We could not find this address. Check it, or use your current location.'));
      } else {
        _setPoint(found.latitude, found.longitude);
      }
    } on GeocodingUnavailable {
      if (mounted) setState(() => _locationError = context.tr('Address lookup is not available right now. Use your current location, or enter coordinates below.'));
    } finally {
      if (mounted) setState(() => _finding = false);
    }
  }

  void _useCoordinates() {
    final lat = double.tryParse(_latText.text.trim().replaceAll(',', '.'));
    final lng = double.tryParse(_lngText.text.trim().replaceAll(',', '.'));
    if (lat == null || lng == null || lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0)) {
      setState(() => _locationError = context.tr('Enter valid coordinates, for example 19.0760 and 72.8777.'));
      return;
    }
    _setPoint(lat, lng);
  }

  String get _chosenLabel => _label == 'Other' && _otherLabel.text.trim().isNotEmpty ? _otherLabel.text.trim() : _label;

  Future<void> _save() async {
    if (_busy) return;
    final signedIn = ref.read(authProvider).isSignedIn;
    final valid = _form.currentState!.validate();
    if (!_hasPoint) setState(() => _locationError = context.tr('Set the location first: find the address or use your current location.'));
    if (!valid || !_hasPoint) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      if (!signedIn) {
        final place = DeliveryPlace(label: 'Delivery location', line: _line.text.trim(), latitude: _lat!, longitude: _lng!);
        if (await chooseDevicePlace(context, ref, place) && mounted) goBack(context, '/addresses');
        return;
      }
      final draft = AddressDraft(
        label: _chosenLabel,
        line: _line.text,
        flatOrBuilding: _flat.text,
        landmark: _landmark.text,
        latitude: _lat!,
        longitude: _lng!,
        receiverName: _name.text,
        receiverPhone: _phone.text,
      );
      final notifier = ref.read(savedAddressesProvider.notifier);
      if (_editing) {
        final existing = widget.args!.existing!;
        final moved = existing.latitude != _lat || existing.longitude != _lng;
        // Moving the address the cart is being delivered to can make the cart's store stop serving it: ask before saving.
        if (moved && ref.read(deliveryPlaceProvider)?.addressId == existing.id && !await cartAllowsPlace(context, ref, _lat!, _lng!)) return;
        await notifier.edit(existing.id, draft);
      } else {
        final saved = await notifier.add(draft);
        if (mounted) await chooseSavedAddress(context, ref, saved);
      }
      if (mounted) goBack(context, '/addresses');
    } catch (e) {
      if (mounted) setState(() => _error = addressErrorText(context, e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final signedIn = ref.watch(authProvider).isSignedIn;
    final p = context.pal;
    return AppScaffold(
      appBar: appTopBar(context, _editing ? context.tr('Edit address') : (signedIn ? context.tr('Add address') : context.tr('Delivery location')), backFallback: '/addresses'),
      body: Form(
        key: _form,
        child: ListView(padding: const EdgeInsets.fromLTRB(QC.gutter, 4, QC.gutter, 24), keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag, children: [
          AuthField(
            context.tr('Address'),
            Icons.place_outlined,
            controller: _line,
            enabled: !_busy,
            maxLines: 3,
            keyboard: TextInputType.streetAddress,
            capitalization: TextCapitalization.words,
            onChanged: (_) => setState(() {}),
            validator: (v) => requiredField(context, v),
          ),
          Row(children: [
            Expanded(child: SoftPillButton(context.tr('Find this address'), height: 52, onPressed: _busy || _finding ? () {} : _findAddress)),
            const SizedBox(width: 10),
            Expanded(child: SoftPillButton(context.tr('Use my current location'), height: 52, onPressed: _busy || _locating ? () {} : _useMyLocation)),
          ]),
          const SizedBox(height: 12),
          _LocationStatus(lat: _lat, lng: _lng, service: _service, error: _locationError, working: _finding || _locating),
          Theme(
            data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
            child: ExpansionTile(
              tilePadding: EdgeInsets.zero,
              iconColor: Colors.white,
              collapsedIconColor: Colors.white,
              title: Text(context.tr('Enter coordinates yourself'), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 14, fontWeight: FontWeight.w700)),
              children: [
                Row(children: [
                  Expanded(child: AuthField(context.tr('Latitude'), Icons.swap_vert, controller: _latText, keyboard: const TextInputType.numberWithOptions(decimal: true, signed: true))),
                  const SizedBox(width: 10),
                  Expanded(child: AuthField(context.tr('Longitude'), Icons.swap_horiz, controller: _lngText, keyboard: const TextInputType.numberWithOptions(decimal: true, signed: true))),
                ]),
                SoftPillButton(context.tr('Use these coordinates'), height: 48, onPressed: _useCoordinates),
                const SizedBox(height: 8),
              ],
            ),
          ),
          if (signedIn) ...[
            const SizedBox(height: 8),
            AuthField(context.tr('Flat / house no. / building'), Icons.apartment_outlined, controller: _flat, enabled: !_busy, textInputAction: TextInputAction.next),
            AuthField(context.tr('Landmark (optional)'), Icons.flag_outlined, controller: _landmark, enabled: !_busy, textInputAction: TextInputAction.next),
            AuthField(context.tr('Receiver name'), Icons.person_outline, controller: _name, enabled: !_busy, capitalization: TextCapitalization.words, autofillHints: const [AutofillHints.name], textInputAction: TextInputAction.next, validator: (v) => requiredField(context, v)),
            AuthField(
              context.tr('Receiver phone'),
              Icons.phone_outlined,
              controller: _phone,
              enabled: !_busy,
              keyboard: TextInputType.phone,
              autofillHints: const [AutofillHints.telephoneNumber],
              formatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9+ ]'))],
              validator: (v) => _phoneOk(v) ? null : context.tr('Enter a phone number with 10 to 15 digits.'),
            ),
            Padding(
              padding: const EdgeInsets.only(left: 6, bottom: 6),
              child: Text(context.tr('Save as'), style: const TextStyle(color: Pal.mutedOnGround, fontSize: 13, fontWeight: FontWeight.w800)),
            ),
            Wrap(spacing: 8, runSpacing: 8, children: [
              for (final l in _labels)
                Semantics(
                  button: true,
                  selected: _label == l,
                  label: context.tr(l),
                  child: GestureDetector(
                    onTap: _busy ? null : () => setState(() => _label = l),
                    child: Chip2(context.tr(l), background: _label == l ? Pal.yellow : (p.dark ? p.border : Colors.white), foreground: _label == l ? Pal.ink : p.onCard),
                  ),
                ),
            ]),
            if (_label == 'Other') ...[
              const SizedBox(height: 12),
              AuthField(context.tr('Name this address'), Icons.label_outline, controller: _otherLabel, enabled: !_busy, validator: (v) => requiredField(context, v)),
            ],
            const SizedBox(height: 16),
          ] else
            const SizedBox(height: 12),
          if (_error != null) ErrorBanner(_error!),
          PillButton(
            context.tr(_busy ? 'Please wait…' : (signedIn ? 'Save address' : 'Use this location')),
            arrow: !_busy,
            onPressed: _busy ? null : _save,
          ),
        ]),
      ),
    );
  }

  static bool _phoneOk(String? v) {
    final digits = (v ?? '').replaceAll(RegExp(r'\D'), '');
    return digits.length >= 10 && digits.length <= 15;
  }
}

/// Whether the location is set, what was found, and whether stores deliver there.
class _LocationStatus extends StatelessWidget {
  const _LocationStatus({required this.lat, required this.lng, required this.service, required this.error, required this.working});
  final double? lat, lng;
  final ServiceabilityResult? service;
  final String? error;
  final bool working;

  @override
  Widget build(BuildContext context) {
    final p = context.pal;
    if (working) {
      return Padding(padding: const EdgeInsets.only(bottom: 8), child: Row(children: [const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2.5, color: Pal.yellow)), const SizedBox(width: 10), Text(context.tr('Looking…'), style: const TextStyle(color: Pal.mutedOnGround))]));
    }
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      if (error != null) ErrorBanner(error!),
      if (lat != null && lng != null)
        Surface(
          color: service == null ? null : (service!.serviceable ? Pal.green : Pal.pink),
          padding: const EdgeInsets.all(14),
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Icon(service == null ? Icons.location_on_outlined : (service!.serviceable ? Icons.check_circle_outline : Icons.location_off_outlined), color: service == null ? p.onCard : (service!.serviceable ? Pal.ink : Colors.white)),
            const SizedBox(width: 10),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(
                  service == null ? context.tr('Location set') : (service!.serviceable ? context.tr('We deliver here') : context.tr('We do not deliver here yet')),
                  style: TextStyle(color: service == null ? p.onCard : (service!.serviceable ? Pal.ink : Colors.white), fontWeight: FontWeight.w900),
                ),
                Text('${lat!.toStringAsFixed(4)}, ${lng!.toStringAsFixed(4)}', style: TextStyle(color: service == null ? p.mutedOnCard : (service!.serviceable ? Pal.ink : Colors.white), fontSize: 12)),
                if (service != null && !service!.serviceable) Padding(padding: const EdgeInsets.only(top: 4), child: Text(context.tr('You can still save it, but orders to this address are not possible until a store delivers here.'), style: const TextStyle(color: Colors.white, fontSize: 12))),
              ]),
            ),
          ]),
        ),
      const SizedBox(height: 8),
    ]);
  }
}
