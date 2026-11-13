import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/analytics/analytics.dart';
import '../../core/api/api_client.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'billing_repository.dart';

/// Opens the paywall when [error] is a plan limit (HTTP 402). Returns true when it did,
/// so callers can skip their generic error message.
bool openPaywallIfLimited(BuildContext context, Object error) {
  if (error is! ApiException || !error.needsPlus) return false;
  context.push('/paywall?trigger=${error.trigger ?? 'limit'}');
  return true;
}

/// Family Plus offer. Shown at natural moments (4th weekly scan, 3rd adult, 2nd reminder on an item, 4th helper link)
/// and from the plan screen. Never blocks viewing existing data: closing it always works.
class PaywallScreen extends ConsumerStatefulWidget {
  const PaywallScreen({super.key, required this.trigger});

  final String trigger;

  @override
  ConsumerState<PaywallScreen> createState() => _PaywallScreenState();
}

class _PaywallScreenState extends ConsumerState<PaywallScreen> {
  String? _selected;
  bool _busy = false;
  Map<String, String> _prices = const {};

  @override
  void initState() {
    super.initState();
    ref.read(analyticsProvider).track(AnalyticsEvent.paywallViewed, {'trigger': widget.trigger});
    _loadPrices();
  }

  Future<void> _loadPrices() async {
    try {
      final catalog = await ref.read(catalogProvider.future);
      final prices = await ref.read(storeGatewayProvider).prices([for (final p in catalog.products) p.id]);
      if (mounted) setState(() => _prices = prices);
    } catch (_) {
      // Prices are optional decoration; the store sheet shows the real price.
    }
  }

  String? _familyId() => ref.read(meProvider).valueOrNull?.primaryFamily?.id;

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _startTrial() => _run(() async {
        final familyId = _familyId();
        if (familyId == null) return;
        final l10n = AppLocalizations.of(context);
        final messenger = ScaffoldMessenger.of(context);
        try {
          await ref.read(billingRepositoryProvider).startTrial(familyId);
          ref.invalidate(entitlementProvider);
          messenger.showSnackBar(SnackBar(content: Text(l10n.paywallTrialStarted)));
          if (mounted) context.pop(true);
        } catch (_) {
          messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
        }
      });

  Future<void> _buy(Product product) => _run(() async {
        final familyId = _familyId();
        if (familyId == null) return;
        final l10n = AppLocalizations.of(context);
        final messenger = ScaffoldMessenger.of(context);
        ref.read(analyticsProvider).track(AnalyticsEvent.purchaseStarted, {'product': product.id});
        try {
          final purchase = await ref.read(storeGatewayProvider).buy(product.id);
          if (purchase == null) return; // cancelled in the store sheet
          await ref.read(billingRepositoryProvider)
              .registerPurchase(familyId, store: purchase.store, productId: purchase.productId, token: purchase.token);
          ref.invalidate(entitlementProvider);
          messenger.showSnackBar(SnackBar(content: Text(l10n.paywallWelcomePlus)));
          if (mounted) context.pop(true);
        } on StoreUnavailableException {
          messenger.showSnackBar(SnackBar(content: Text(l10n.paywallStoreSoon)));
        } catch (_) {
          messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
        }
      });

  Future<void> _restore() => _run(() async {
        final familyId = _familyId();
        if (familyId == null) return;
        final l10n = AppLocalizations.of(context);
        final messenger = ScaffoldMessenger.of(context);
        try {
          final purchases = await ref.read(storeGatewayProvider).restore();
          Entitlement? last;
          for (final p in purchases) {
            last = await ref.read(billingRepositoryProvider).registerPurchase(familyId, store: p.store, productId: p.productId, token: p.token);
          }
          ref.invalidate(entitlementProvider);
          messenger.showSnackBar(SnackBar(content: Text(last?.isPlus == true ? l10n.paywallWelcomePlus : l10n.paywallNothingToRestore)));
          if (last?.isPlus == true && mounted) context.pop(true);
        } catch (_) {
          messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
        }
      });

  String _headline(AppLocalizations l10n, Catalog catalog) => switch (widget.trigger) {
        'scan_limit' => l10n.paywallScanLimit(catalog.free.scansPerWeek),
        'adult_limit' => l10n.paywallAdultLimit(catalog.free.maxAdults),
        'share_link_limit' => l10n.paywallShareLimit(catalog.free.activeShareLinks),
        'advanced_reminder' => l10n.paywallReminderLimit,
        _ => l10n.paywallTitle,
      };

  void _close() {
    ref.read(analyticsProvider).track(AnalyticsEvent.paywallDismissed, {'trigger': widget.trigger});
    context.pop(false);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final catalog = ref.watch(catalogProvider);
    final entitlement = ref.watch(entitlementProvider).valueOrNull;
    final store = ref.watch(storeGatewayProvider);

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(key: const Key('paywall.close'), tooltip: l10n.paywallNotNow, icon: const Icon(Icons.close_rounded), onPressed: _close),
      ),
      body: catalog.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(catalogProvider)),
        data: (c) {
          final products = c.products;
          final selected = _selected ?? (products.where((p) => p.yearly).firstOrNull ?? products.firstOrNull)?.id;
          final trial = entitlement?.trialAvailable ?? false;
          return HallyPage(
            bottom: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (trial)
                  FilledButton(
                    key: const Key('paywall.trial'),
                    onPressed: _busy ? null : _startTrial,
                    child: Text(l10n.paywallStartTrial(c.trialDays)),
                  )
                else
                  FilledButton(
                    key: const Key('paywall.buy'),
                    onPressed: _busy || selected == null ? null : () => _buy(products.firstWhere((p) => p.id == selected)),
                    child: Text(l10n.paywallSubscribe),
                  ),
                const SizedBox(height: HallySpace.xs),
                Text(
                  trial ? l10n.paywallTrialNote : l10n.paywallRenewNote,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                TextButton(key: const Key('paywall.restore'), onPressed: _busy || !store.available ? null : _restore, child: Text(l10n.paywallRestore)),
              ],
            ),
            children: [
              HallyEntrance(child: _Hero(title: _headline(l10n, c), subtitle: l10n.paywallSubtitle)),
              const SizedBox(height: HallySpace.md),
              HallyEntrance(index: 1, child: _Comparison(free: c.free, plus: c.plus)),
              const SizedBox(height: HallySpace.md),
              for (final (i, p) in products.indexed)
                HallyEntrance(
                  index: i + 2,
                  child: _ProductTile(
                    product: p,
                    price: _prices[p.id] ?? _fallbackPrice(p),
                    selected: p.id == selected,
                    onTap: () => setState(() => _selected = p.id),
                  ),
                ),
            ],
          );
        },
      ),
    );
  }

  String? _fallbackPrice(Product p) {
    if (p.fallbackPrices.isEmpty) return null;
    final currency = NumberFormat.simpleCurrency(locale: Localizations.localeOf(context).toString()).currencyName;
    return p.fallbackPrices[currency] ?? p.fallbackPrices.values.first;
  }
}

class _Hero extends StatelessWidget {
  const _Hero({required this.title, required this.subtitle});

  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(HallySpace.lg),
      decoration: BoxDecoration(
        gradient: HallyGradients.hero,
        borderRadius: BorderRadius.circular(HallyRadius.lg),
        boxShadow: HallyShadows.glow(HallyColors.lavender),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.workspace_premium_rounded, color: Colors.white, size: 36),
          const SizedBox(height: HallySpace.sm),
          Text(title, key: const Key('paywall.headline'), style: Theme.of(context).textTheme.headlineSmall?.copyWith(color: Colors.white)),
          const SizedBox(height: HallySpace.xs),
          Text(subtitle, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w500)),
        ],
      ),
    );
  }
}

class _Comparison extends StatelessWidget {
  const _Comparison({required this.free, required this.plus});

  final PlanLimits free;
  final PlanLimits plus;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    String n(int v) => v <= 0 ? l10n.planUnlimited : '$v';
    String scans(PlanLimits l) => l.scansPerWeek > 0 ? l10n.planPerWeek(l.scansPerWeek) : l.scansPerMonth > 0 ? l10n.planPerMonth(l.scansPerMonth) : l10n.planUnlimited;
    final rows = [
      (l10n.planRowAdults, n(free.maxAdults), n(plus.maxAdults)),
      (l10n.planRowScans, scans(free), scans(plus)),
      (l10n.planRowLinks, n(free.activeShareLinks), n(plus.activeShareLinks)),
      (l10n.planRowReminders, n(free.remindersPerItem), n(plus.remindersPerItem)),
    ];
    final style = Theme.of(context).textTheme;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(HallySpace.md),
        child: Table(
          columnWidths: const {0: FlexColumnWidth(2.2), 1: FlexColumnWidth(1), 2: FlexColumnWidth(1.2)},
          defaultVerticalAlignment: TableCellVerticalAlignment.middle,
          children: [
            TableRow(children: [
              const SizedBox(),
              Text(l10n.planFree, style: style.labelLarge, textAlign: TextAlign.center),
              Text(l10n.planPlus, style: style.labelLarge?.copyWith(color: HallyColors.lavender), textAlign: TextAlign.center),
            ]),
            for (final (label, f, p) in rows)
              TableRow(children: [
                Padding(padding: const EdgeInsets.symmetric(vertical: 6), child: Text(label, style: style.bodyMedium)),
                Text(f, style: style.bodyMedium, textAlign: TextAlign.center),
                Text(p, style: style.bodyMedium?.copyWith(fontWeight: FontWeight.w700), textAlign: TextAlign.center),
              ]),
          ],
        ),
      ),
    );
  }
}

class _ProductTile extends StatelessWidget {
  const _ProductTile({required this.product, required this.price, required this.selected, required this.onTap});

  final Product product;
  final String? price;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: HallySpace.sm),
      child: HallyPressable(
        onTap: onTap,
        child: AnimatedContainer(
          key: Key('paywall.product.${product.id}'),
          duration: HallyMotion.fast,
          padding: const EdgeInsets.all(HallySpace.md),
          decoration: BoxDecoration(
            color: theme.colorScheme.surface,
            borderRadius: BorderRadius.circular(HallyRadius.md),
            border: Border.all(color: selected ? HallyColors.lavender : theme.dividerColor, width: selected ? 2 : 1),
          ),
          child: Row(
            children: [
              Icon(selected ? Icons.radio_button_checked_rounded : Icons.radio_button_off_rounded, color: HallyColors.lavender),
              const SizedBox(width: HallySpace.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(product.yearly ? l10n.paywallYearly : l10n.paywallMonthly, style: theme.textTheme.titleMedium),
                    Text(price ?? l10n.paywallPriceInStore, style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
              if (product.yearly) HallyPill(label: l10n.paywallYearlyBadge, color: HallyColors.mint),
            ],
          ),
        ),
      ),
    );
  }
}

/// Current plan, usage this week and a way to upgrade. Reached from the Family tab.
class PlanScreen extends ConsumerWidget {
  const PlanScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final entitlement = ref.watch(entitlementProvider);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.planTitle)),
      body: entitlement.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(entitlementProvider)),
        data: (e) {
          if (e == null) return const HallyLoading();
          final dateFormat = DateFormat.yMMMd(Localizations.localeOf(context).toString());
          final status = switch (e.state) {
            'Trialing' => l10n.planStatusTrial(dateFormat.format(e.expiresAt!)),
            'Active' when e.willRenew => l10n.planStatusRenews(dateFormat.format(e.expiresAt!)),
            'Active' => l10n.planStatusEnds(dateFormat.format(e.expiresAt!)),
            'GracePeriod' => l10n.planStatusGrace,
            _ => l10n.planStatusFree,
          };
          final l = e.limits;
          final u = e.usage;
          String of(int used, int limit) => limit <= 0 ? '$used' : '$used / $limit';
          return RefreshIndicator(
            onRefresh: () => ref.refresh(entitlementProvider.future),
            child: ListView(
              padding: const EdgeInsets.all(HallySpace.md),
              children: [
                Card(
                  child: ListTile(
                    key: const Key('plan.status'),
                    leading: HallyIconBadge(
                      icon: e.isPlus ? Icons.workspace_premium_rounded : Icons.favorite_rounded,
                      color: e.isPlus ? HallyColors.lavender : HallyColors.mint,
                    ),
                    title: Text(e.isPlus ? l10n.planPlus : l10n.planFree, style: Theme.of(context).textTheme.titleMedium),
                    subtitle: Text(status),
                  ),
                ),
                HallySectionHeader(title: l10n.planUsage),
                Card(
                  child: Column(
                    children: [
                      ListTile(
                        title: Text(l10n.planRowScans),
                        trailing: Text(l.scansPerWeek > 0 ? of(u.scansThisWeek, l.scansPerWeek) : of(u.scansThisMonth, l.scansPerMonth),
                            key: const Key('plan.scans')),
                        subtitle: Text(l.scansPerWeek > 0 ? l10n.planThisWeek : l10n.planThisMonth),
                      ),
                      ListTile(title: Text(l10n.planRowAdults), trailing: Text(of(u.adults, l.maxAdults))),
                      ListTile(title: Text(l10n.planRowLinks), trailing: Text(of(u.activeShareLinks, l.activeShareLinks))),
                    ],
                  ),
                ),
                const SizedBox(height: HallySpace.md),
                if (!e.isPlus)
                  FilledButton(
                    key: const Key('plan.upgrade'),
                    onPressed: () => context.push('/paywall?trigger=plan_screen'),
                    child: Text(e.trialAvailable ? l10n.paywallStartTrial(ref.watch(catalogProvider).valueOrNull?.trialDays ?? 7) : l10n.paywallSubscribe),
                  )
                else if (e.source == 'AppStore' || e.source == 'GooglePlay')
                  Text(e.source == 'AppStore' ? l10n.planManageApple : l10n.planManageGoogle,
                      style: Theme.of(context).textTheme.bodySmall, textAlign: TextAlign.center),
              ],
            ),
          );
        },
      ),
    );
  }
}
