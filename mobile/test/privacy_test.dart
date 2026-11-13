import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/auth/token_storage.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/family/family_repository.dart';
import 'package:hally/features/privacy/privacy_repository.dart';
import 'package:hally/features/privacy/privacy_screen.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

class _FakePrivacy implements PrivacyRepository {
  _FakePrivacy({this.noTraining = false});

  final bool noTraining;
  var deleted = 0;

  @override
  Future<PrivacyInfo> info() async => PrivacyInfo(aiProviderNoTraining: noTraining, originalsRetentionDays: 30);

  @override
  Future<Uint8List> export() async => Uint8List.fromList('{}'.codeUnits);

  @override
  Future<void> leaveFamily(String familyId) async {}

  @override
  Future<void> deleteAccount() async => deleted++;
}

class _MemoryTokens implements TokenStorage {
  StoredTokens? tokens = const StoredTokens(accessToken: 'a', refreshToken: 'r');

  @override
  Future<StoredTokens?> read() async => tokens;

  @override
  Future<void> write(StoredTokens value) async => tokens = value;

  @override
  Future<void> clear() async => tokens = null;
}

Widget _app(_FakePrivacy repo, _MemoryTokens tokens) => ProviderScope(
      overrides: [
        meProvider.overrideWith((ref) async => _me),
        privacyRepositoryProvider.overrideWithValue(repo),
        tokenStorageProvider.overrideWithValue(tokens),
        familyProvider.overrideWith((ref, id) async => FamilyDetails.fromJson({
              'id': id,
              'name': 'Yılmaz',
              'timeZoneId': 'Europe/Istanbul',
              'members': [
                {'userId': 'u1', 'displayName': 'Ayşe', 'role': 'Owner'},
                {'userId': 'u2', 'displayName': 'Mehmet', 'role': 'Adult'},
              ],
            })),
      ],
      child: MaterialApp(
        theme: buildHallyTheme(Brightness.light),
        locale: const Locale('tr'),
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        supportedLocales: AppLocalizations.supportedLocales,
        home: const PrivacyScreen(),
      ),
    );

void _phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

void main() {
  testWidgets('the no-training promise is shown only when the server confirms it', (tester) async {
    _phone(tester);
    await tester.pumpWidget(_app(_FakePrivacy(), _MemoryTokens()));
    await tester.pumpAndSettle();
    expect(find.textContaining('asla kullanılmaz'), findsNothing);
    expect(find.textContaining('model eğitmek için kullanmaz'), findsOneWidget);

    await tester.pumpWidget(_app(_FakePrivacy(noTraining: true), _MemoryTokens()));
    await tester.pumpAndSettle();
    expect(find.text('Ailenin verileri yapay zekâ modellerini eğitmek için asla kullanılmaz.'), findsOneWidget);
  });

  testWidgets('deleting the account needs two confirmations and the typed word, then signs out', (tester) async {
    _phone(tester);
    final repo = _FakePrivacy();
    final tokens = _MemoryTokens();
    await tester.pumpWidget(_app(repo, tokens));
    await tester.pumpAndSettle();

    final delete = find.byKey(const Key('privacy.delete'));
    await tester.scrollUntilVisible(delete, 300, scrollable: find.byType(Scrollable).first);
    await tester.pumpAndSettle();
    await tester.tap(delete);
    await tester.pumpAndSettle();
    expect(find.textContaining('App Store ya da Google Play'), findsOneWidget, reason: 'warns that store subscriptions are separate');
    await tester.tap(find.byKey(const Key('privacy.confirm')));
    await tester.pumpAndSettle();

    final finalButton = find.byKey(const Key('privacy.deleteFinal'));
    expect(tester.widget<FilledButton>(finalButton).onPressed, isNull);
    await tester.enterText(find.byKey(const Key('privacy.deleteWord')), 'sil');
    await tester.pump();
    expect(tester.widget<FilledButton>(finalButton).onPressed, isNotNull, reason: 'Turkish casing is forgiven');
    await tester.tap(finalButton);
    await tester.pumpAndSettle();

    expect(repo.deleted, 1);
    expect(tokens.tokens, isNull);
  });
}
