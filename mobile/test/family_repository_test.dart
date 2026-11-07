import 'package:flutter_test/flutter_test.dart';
import 'package:hally/features/family/family_repository.dart';

void main() {
  test('parses invite token from a link or a bare code', () {
    expect(parseInviteToken('https://hally.app/i/AbC_123-x'), 'AbC_123-x');
    expect(parseInviteToken('  AbC_123-x '), 'AbC_123-x');
  });

  test('maps family json', () {
    final f = Family.fromJson({
      'id': 'f1',
      'name': 'Yılmaz Ailesi',
      'timeZoneId': 'Europe/Istanbul',
      'members': [
        {'userId': 'u1', 'displayName': 'Ayşe', 'role': 'Owner'},
      ],
    });
    expect(f.members.single.role, 'Owner');
    expect(f.timeZoneId, 'Europe/Istanbul');
  });
}
