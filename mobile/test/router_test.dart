import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/router.dart';

const _noFamily = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: []);
const _withFamily = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

String? _go(String location, {bool loading = false, Me? me, String? pending}) =>
    resolveRedirect(location: location, loading: loading, me: me, pendingInviteToken: pending);

void main() {
  test('shows splash while the session loads', () {
    expect(_go('/today', loading: true), '/');
    expect(_go('/', loading: true), isNull);
  });

  test('signed-out users stay in onboarding', () {
    expect(_go('/'), '/welcome');
    expect(_go('/today'), '/welcome');
    expect(_go('/trust'), isNull);
    expect(_go('/sign-in'), isNull);
  });

  test('invite deep link opens without signing in', () {
    expect(_go('/i/abc'), isNull);
    expect(_go('/i/abc', loading: true), isNull);
  });

  test('signed-in user without a family is sent to family setup', () {
    expect(_go('/sign-in', me: _noFamily), '/family/new');
    expect(_go('/family/new', me: _noFamily), isNull);
  });

  test('pending invite wins over family setup after sign-in', () {
    expect(_go('/sign-in', me: _noFamily, pending: 'tok'), '/i/tok');
    expect(_go('/i/tok', me: _noFamily, pending: 'tok'), isNull);
  });

  test('family members land on Today', () {
    expect(_go('/', me: _withFamily), '/today');
    expect(_go('/welcome', me: _withFamily), '/today');
    expect(_go('/family', me: _withFamily), isNull);
    expect(_go('/family/f1/invite', me: _withFamily), isNull);
  });
}
