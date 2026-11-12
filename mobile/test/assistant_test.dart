import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/assistant/assistant_repository.dart';
import 'package:hally/features/assistant/assistant_screen.dart';
import 'package:hally/features/capture/capture_models.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

class _FakeAssistant implements AssistantRepository {
  final sent = <(String, String?)>[];

  @override
  Future<AssistantReply> send(String familyId, String text, {String? conversationId}) async {
    sent.add((text, conversationId));
    final proposals = text.contains('ekle')
        ? [
            Proposal.fromJson({
              'id': 'p1',
              'kind': 'CreateEvent',
              'status': 'Pending',
              'payload': {'title': 'Yüzme dersi', 'date': '2026-11-14', 'time': '17:00', 'needsInfo': <String>[], 'confidence': 'medium'},
            }),
          ]
        : <Proposal>[];
    return AssistantReply(
      conversationId: 'c1',
      reply: ChatEntry(id: 'm${sent.length}', fromUser: false, text: proposals.isEmpty ? 'Yarın: Okul gezisi (09:00)' : 'Ekledim, onayını bekliyor.', proposals: proposals),
    );
  }
}

Widget _app(_FakeAssistant repo) => ProviderScope(
      overrides: [meProvider.overrideWith((ref) async => _me), assistantRepositoryProvider.overrideWithValue(repo)],
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
        home: const AssistantScreen(),
      ),
    );

void main() {
  testWidgets('starter question gets an answer and the conversation continues', (tester) async {
    final repo = _FakeAssistant();
    await tester.pumpWidget(_app(repo));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('assistant.starter.0')));
    await tester.pumpAndSettle();
    expect(find.text('Yarın ne lazım?'), findsOneWidget);
    expect(find.text('Yarın: Okul gezisi (09:00)'), findsOneWidget);

    await tester.enterText(find.byKey(const Key('assistant.input')), 'Yarın 17:00\'de yüzme dersi ekle');
    await tester.tap(find.byKey(const Key('assistant.send')));
    await tester.pumpAndSettle();
    expect(repo.sent.last.$2, 'c1', reason: 'follow-up messages stay in the same conversation');
    expect(find.text('Yüzme dersi · 2026-11-14 · 17:00'), findsOneWidget);
    expect(find.byKey(const Key('assistant.review.m2')), findsOneWidget);
  });
}
