import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:hally/core/api/api_client.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/billing/billing_repository.dart';
import 'package:hally/features/billing/paywall_screen.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

const _free = PlanLimits(maxAdults: 2, scansPerWeek: 3, scansPerMonth: 0, activeShareLinks: 3, remindersPerItem: 1);
const _plus = PlanLimits(maxAdults: 6, scansPerWeek: 0, scansPerMonth: 100, activeShareLinks: 0, remindersPerItem: 5);

Entitlement _entitlement({bool plus = false, bool trialAvailable = true, int scans = 3}) => Entitlement(
      plan: plus ? 'Plus' : 'Free',
      state: plus ? 'Trialing' : 'Free',
      source: plus ? 'Trial' : null,
      expiresAt: plus ? DateTime(2026, 11, 20) : null,
      willRenew: false,
      trialAvailable: trialAvailable,
      purchasedByMe: false,
      limits: plus ? _plus : _free,
      usage: Usage(adults: 2, scansThisWeek: scans, scansThisMonth: scans, activeShareLinks: 1),
    );

class _FakeBilling implements BillingRepository {
  _FakeBilling({this.trialAvailable = true});

  final bool trialAvailable;
  var trials = 0;
  final purchases = <String>[];

  @override
  Future<Catalog> catalog() async => const Catalog(
        products: [Product(id: 'hally_plus_monthly', period: 'monthly'), Product(id: 'hally_plus_yearly', period: 'yearly')],
        trialDays: 7,
        free: _free,
        plus: _plus,
        stores: [],
      );

  @override
  Future<Entitlement> entitlement(String familyId) async => _entitlement(plus: trials > 0, trialAvailable: trialAvailable && trials == 0);

  @override
  Future<Entitlement> startTrial(String familyId) async {
    trials++;
    return _entitlement(plus: true, trialAvailable: false);
  }

  @override
  Future<Entitlement> registerPurchase(String familyId, {required String store, required String productId, required String token}) async {
    purchases.add('$store:$productId:$token');
    return _entitlement(plus: true, trialAvailable: false);
  }
}

class _FakeStore implements StoreGateway {
  @override
  bool get available => true;

  @override
  Future<Map<String, String>> prices(List<String> productIds) async => {'hally_plus_monthly': '₺139,99', 'hally_plus_yearly': '₺1.399,99'};

  @override
  Future<StorePurchaseResult?> buy(String productId) async => StorePurchaseResult(store: 'GooglePlay', productId: productId, token: 'tok-1');

  @override
  Future<List<StorePurchaseResult>> restore() async => const [];
}

/// Home has a button that fails with a 402 like the API does, so the paywall opens through the real helper.
Widget _app(_FakeBilling billing, {StoreGateway? store, String start = '/'}) {
  final router = GoRouter(
    initialLocation: start,
    routes: [
      GoRoute(
        path: '/',
        builder: (context, _) => Scaffold(
          body: Center(
            child: FilledButton(
              key: const Key('home.scan'),
              onPressed: () => openPaywallIfLimited(context, ApiException(402, 'plan.scan_limit', 'limit', trigger: 'scan_limit')),
              child: const Text('scan'),
            ),
          ),
        ),
      ),
      GoRoute(path: '/paywall', builder: (_, s) => PaywallScreen(trigger: s.uri.queryParameters['trigger']!)),
      GoRoute(path: '/plan', builder: (_, __) => const PlanScreen()),
    ],
  );
  return ProviderScope(
    overrides: [
      meProvider.overrideWith((ref) async => _me),
      billingRepositoryProvider.overrideWithValue(billing),
      if (store != null) storeGatewayProvider.overrideWithValue(store),
    ],
    child: MaterialApp.router(
      theme: buildHallyTheme(Brightness.light),
      locale: const Locale('tr'),
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      routerConfig: router,
    ),
  );
}

void _phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

void main() {
  testWidgets('a 402 opens the paywall with the reason, and the trial unlocks Plus', (tester) async {
    _phone(tester);
    final billing = _FakeBilling();
    await tester.pumpWidget(_app(billing));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('home.scan')));
    await tester.pumpAndSettle();
    expect(find.text('Bu haftaki 3 ücretsiz taramayı kullandın'), findsOneWidget);
    expect(find.text('Haftada 3'), findsOneWidget, reason: 'free column of the comparison');
    expect(find.text('Ayda 100'), findsOneWidget);
    expect(find.text('Fiyat mağazada gösterilir', skipOffstage: false), findsNWidgets(2), reason: 'no store connected yet');

    await tester.tap(find.byKey(const Key('paywall.trial')));
    await tester.pumpAndSettle();
    expect(billing.trials, 1);
    expect(find.text('Family Plus denemen başladı'), findsOneWidget);
    expect(find.byKey(const Key('home.scan')), findsOneWidget, reason: 'the paywall closes after the trial starts');
  });

  testWidgets('after the trial, buying goes through the store and the server', (tester) async {
    _phone(tester);
    final billing = _FakeBilling(trialAvailable: false);
    await tester.pumpWidget(_app(billing, store: _FakeStore(), start: '/paywall?trigger=adult_limit'));
    await tester.pumpAndSettle();

    expect(find.text('Ücretsiz planda aile 2 yetişkinle sınırlı'), findsOneWidget);
    expect(find.byKey(const Key('paywall.trial')), findsNothing);
    expect(find.text('₺1.399,99', skipOffstage: false), findsOneWidget);

    final monthly = find.byKey(const Key('paywall.product.hally_plus_monthly'), skipOffstage: false);
    await tester.ensureVisible(monthly);
    await tester.pumpAndSettle();
    await tester.tap(monthly);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('paywall.buy')));
    await tester.pumpAndSettle();
    expect(billing.purchases, ['GooglePlay:hally_plus_monthly:tok-1']);
  });

  testWidgets('without a store, buying explains that purchases open with the beta', (tester) async {
    _phone(tester);
    await tester.pumpWidget(_app(_FakeBilling(trialAvailable: false), start: '/paywall?trigger=plan_screen'));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('paywall.buy')));
    await tester.pumpAndSettle();
    expect(find.textContaining('beta sürümünde açılacak'), findsOneWidget);
  });

  testWidgets('plan screen shows usage against the free limits', (tester) async {
    _phone(tester);
    await tester.pumpWidget(_app(_FakeBilling(), start: '/plan'));
    await tester.pumpAndSettle();

    expect(find.text('Ücretsiz plandasın'), findsOneWidget);
    expect(find.text('3 / 3'), findsOneWidget);
    expect(find.text('7 gün ücretsiz dene'), findsOneWidget);
  });
}
