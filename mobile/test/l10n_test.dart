import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/strings_mr.dart';

void main() {
  test('every literal context.tr(...) string has a Marathi translation', () {
    final pattern = RegExp(r"tr\('((?:[^'\\]|\\.)*)'");
    final missing = <String>{};
    for (final f in Directory('lib').listSync(recursive: true).whereType<File>().where((f) => f.path.endsWith('.dart') && !f.path.endsWith('strings_mr.dart'))) {
      for (final m in pattern.allMatches(f.readAsStringSync())) {
        final key = m.group(1)!.replaceAll(r'\n', '\n');
        if (!marathi.containsKey(key)) missing.add(key);
      }
    }
    expect(missing, isEmpty, reason: 'Add these to lib/core/strings_mr.dart');
  });
}
