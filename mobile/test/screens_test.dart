import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/family/family_repository.dart';
import 'package:hally/features/family/family_setup_screen.dart';
import 'package:hally/features/onboarding/trust_screen.dart';
import 'package:hally/features/onboarding/welcome_screen.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

Widget _wrap(Widget child, {Locale locale = const Locale('tr'), List<Override> overrides = const []}) => ProviderScope(
      overrides: overrides,
      child: MaterialApp(
        theme: buildHallyTheme(Brightness.light),
        locale: locale,
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

class _RecordingFamilyRepository implements FamilyRepository {
  final created = <String>[];

  @override
  Future<Family> create(String name, String timeZoneId) async {
    created.add('$name|$timeZoneId');
    return Family(id: 'f1', name: name, timeZoneId: timeZoneId, members: const []);
  }

  @override
  Future<String> accept(String token) => throw UnimplementedError();
  @override
  Future<Family> get(String familyId) => throw UnimplementedError();
  @override
  Future<Invitation> invite(String familyId) => throw UnimplementedError();
  @override
  Future<InvitationPreview> preview(String token) => throw UnimplementedError();
}

void main() {
  testWidgets('welcome is Turkish by default', (tester) async {
    await tester.pumpWidget(_wrap(const WelcomeScreen()));
    expect(find.text('Başlayalım'), findsOneWidget);
    expect(find.text('Çek, söyle, Hally halletsin.'), findsOneWidget);
  });

  testWidgets('welcome is localized to English', (tester) async {
    await tester.pumpWidget(_wrap(const WelcomeScreen(), locale: const Locale('en')));
    expect(find.text("Let's start"), findsOneWidget);
    expect(find.text('Snap it. Say it. Hally handles it.'), findsOneWidget);
  });

  testWidgets('trust screen lists the approval-first promise', (tester) async {
    await tester.pumpWidget(_wrap(const TrustScreen()));
    expect(find.text('Senin onayın olmadan hiçbir şey yapmam'), findsOneWidget);
    expect(find.byKey(const Key('trust.continue')), findsOneWidget);
  });

  testWidgets('family setup requires a name and defaults to Istanbul', (tester) async {
    final repo = _RecordingFamilyRepository();
    await tester.pumpWidget(_wrap(const FamilySetupScreen(), overrides: [familyRepositoryProvider.overrideWithValue(repo)]));

    await tester.tap(find.byKey(const Key('familySetup.create')));
    await tester.pump();
    expect(find.text('Aile adı gerekli'), findsOneWidget);
    expect(repo.created, isEmpty);
    expect(find.text('Saat dilimi: Europe/Istanbul'), findsOneWidget);
  });
}
