import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/capture/capture_models.dart';
import 'package:hally/features/capture/capture_repository.dart';
import 'package:hally/features/capture/review_screen.dart';
import 'package:hally/features/family/family_repository.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

class _FakeCaptureRepository implements CaptureRepository {
  final approved = <String>[];
  final rejected = <String>[];

  @override
  Future<void> approve(String familyId, String proposalId, ProposalEdits edits) async => approved.add(proposalId);

  @override
  Future<void> reject(String familyId, String proposalId) async => rejected.add(proposalId);

  @override
  Future<List<Proposal>> pending(String familyId) async => const [];

  @override
  Future<CaptureResult> captureImage(String familyId, List<int> bytes, String fileName, String contentType, {required String source}) =>
      throw UnimplementedError();

  @override
  Future<CaptureResult> captureText(String familyId, String text, {String source = 'Text'}) => throw UnimplementedError();

  @override
  Future<CaptureResult> retry(String familyId, String captureId) => throw UnimplementedError();
}

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

final _proposals = [
  Proposal.fromJson({
    'id': 'p1',
    'kind': 'CreateTask',
    'status': 'Pending',
    'payload': {'title': 'Kırmızı tişört giydir', 'date': '2026-11-13', 'needsInfo': <String>[], 'confidence': 'high'},
  }),
  Proposal.fromJson({
    'id': 'p2',
    'kind': 'CreateEvent',
    'status': 'Pending',
    'payload': {'title': 'Okul gezisi', 'date': null, 'needsInfo': ['date'], 'confidence': 'medium'},
  }),
];

Widget _app(_FakeCaptureRepository repo) => ProviderScope(
      overrides: [
        meProvider.overrideWith((ref) async => _me),
        captureRepositoryProvider.overrideWithValue(repo),
        pendingProposalsProvider('f1').overrideWith((ref) async => _proposals),
        familyProvider('f1').overrideWith((ref) async => const FamilyDetails(
              id: 'f1',
              name: 'Yılmaz',
              timeZoneId: 'Europe/Istanbul',
              members: [FamilyMember(userId: 'u1', displayName: 'Ayşe', role: 'Owner')],
            )),
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
        home: const ReviewScreen(),
      ),
    );

void main() {
  testWidgets('shows proposals with the approval promise and flags missing dates', (tester) async {
    await tester.pumpWidget(_app(_FakeCaptureRepository()));
    await tester.pumpAndSettle();

    expect(find.text('Senin onayın olmadan hiçbir şey eklenmez.'), findsOneWidget);
    expect(find.text('Kırmızı tişört giydir'), findsOneWidget);
    expect(find.text('Okul gezisi'), findsOneWidget);
    expect(find.text('Tarih gerekli'), findsOneWidget);

    final eventApprove = tester.widget<FilledButton>(find.byKey(const Key('review.approve.p2')));
    expect(eventApprove.onPressed, isNull, reason: 'an event without a date cannot be approved');
  });

  testWidgets('approve all only sends items that have the required info', (tester) async {
    final repo = _FakeCaptureRepository();
    await tester.pumpWidget(_app(repo));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('review.approveAll')));
    await tester.pumpAndSettle();

    expect(repo.approved, ['p1']);
    expect(find.text('Okul gezisi'), findsOneWidget);
  });

  testWidgets('remove rejects the proposal', (tester) async {
    final repo = _FakeCaptureRepository();
    await tester.pumpWidget(_app(repo));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Çıkar').first);
    await tester.pumpAndSettle();
    expect(repo.rejected, ['p1']);
  });
}
