import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/text/locale_case.dart';

void main() {
  test('Turkish upper case keeps dotted and dotless i apart', () {
    expect(upperCaseFor('14 ekim pazartesi', const Locale('tr')), '14 EKİM PAZARTESİ');
    expect(upperCaseFor('salı ılık', const Locale('tr')), 'SALI ILIK');
  });

  test('other locales use the default upper case', () {
    expect(upperCaseFor('monday, april', const Locale('en')), 'MONDAY, APRIL');
  });
}
