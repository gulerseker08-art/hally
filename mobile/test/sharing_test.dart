import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/sharing/helper_share_sheet.dart';
import 'package:hally/features/sharing/sharing_repository.dart';
import 'package:hally/features/sharing/weekly_card_screen.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

// 1×1 transparent PNG.
final _png = base64Decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=');

class _FakeSharing implements SharingRepository {
  var created = 0;
  final revoked = <String>[];
  final cardCalls = <(int, bool)>[];
  var links = [ActiveLink(id: 'l1', kind: 'Day', date: '2026-11-13', expiresAt: DateTime(2026, 11, 15), openCount: 2)];

  @override
  Future<HelperLink> shareTask(String familyId, String taskId) async {
    created++;
    return HelperLink(url: 'https://hally.test/s/abc', shareText: 'Ayşe seninle paylaştı: https://hally.test/s/abc', whatsAppUrl: 'https://wa.me/?text=x', expiresAt: DateTime(2026, 11, 15, 22));
  }

  @override
  Future<HelperLink> shareDay(String familyId, DateTime day) => shareTask(familyId, '');

  @override
  Future<List<ActiveLink>> active(String familyId) async => List.of(links);

  @override
  Future<void> revoke(String familyId, String linkId) async {
    revoked.add(linkId);
    links = links.where((l) => l.id != linkId).toList();
  }

  @override
  Future<Uint8List> weeklyCard(String familyId, {required int offset, required bool includeChildNames}) async {
    cardCalls.add((offset, includeChildNames));
    return Uint8List.fromList(_png);
  }
}

Widget _app(Widget child, _FakeSharing repo) => ProviderScope(
      overrides: [meProvider.overrideWith((ref) async => _me), sharingRepositoryProvider.overrideWithValue(repo)],
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
        home: child,
      ),
    );

void main() {
  testWidgets('helper sheet creates the link once and copies the share text', (tester) async {
    final repo = _FakeSharing();
    String? copied;
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
      if (call.method == 'Clipboard.setData') copied = (call.arguments as Map)['text'] as String?;
      return null;
    });
    await tester.pumpWidget(_app(
      Consumer(
        builder: (context, ref, _) => Scaffold(
          body: TextButton(
            onPressed: () => showHelperShareSheet(context, ref, subject: 'Okuldan al', create: () => repo.shareTask('f1', 't1')),
            child: const Text('open'),
          ),
        ),
      ),
      repo,
    ));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    expect(repo.created, 0, reason: 'nothing is created until a channel is picked');
    expect(find.textContaining('Okuldan al'), findsOneWidget);

    await tester.tap(find.byKey(const Key('helper.copy')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('helper.copy')));
    await tester.pumpAndSettle();
    expect(repo.created, 1);
    expect(copied, contains('https://hally.test/s/abc'));
  });

  testWidgets('active links can be turned off', (tester) async {
    final repo = _FakeSharing();
    await tester.pumpWidget(_app(const ShareLinksScreen(), repo));
    await tester.pumpAndSettle();
    expect(find.text('2026-11-13 günü'), findsOneWidget);
    expect(find.text('2 kez açıldı'), findsOneWidget);

    await tester.tap(find.byKey(const Key('link.l1.revoke')));
    await tester.pumpAndSettle();
    expect(repo.revoked, ['l1']);
    expect(find.text('Aktif link yok'), findsOneWidget);
  });

  testWidgets('weekly card reloads for last week and with child names', (tester) async {
    final repo = _FakeSharing();
    await tester.pumpWidget(_app(const WeeklyCardScreen(), repo));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('weekly.image')), findsOneWidget);

    await tester.tap(find.text('Geçen hafta'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('weekly.children')));
    await tester.pumpAndSettle();
    expect(repo.cardCalls, [(0, false), (-1, false), (-1, true)]);
  });
}
