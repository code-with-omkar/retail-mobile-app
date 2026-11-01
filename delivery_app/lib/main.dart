import 'package:flutter/material.dart';

import 'api.dart';
import 'app.dart';

/// Where the API is. Set with `--dart-define=API_BASE_URL=http://localhost:5067` (use `adb reverse tcp:5067 tcp:5067` on a USB phone).
const _baseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://localhost:5067');

void main() {
  runApp(PartnerApp(api: HttpDeliveryApi(baseUrl: _baseUrl, tokens: SecureTokenStore())));
}
