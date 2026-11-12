import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/analytics/analytics.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// "Approval-first AI" promise, shown before sign-in.
class TrustScreen extends ConsumerWidget {
  const TrustScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final points = [
      (Icons.visibility_rounded, HallyColors.lavender, l10n.trustPoint1),
      (Icons.lock_rounded, HallyColors.mint, l10n.trustPoint2),
      (Icons.delete_sweep_rounded, HallyColors.apricot, l10n.trustPoint3),
    ];
    return Scaffold(
      appBar: AppBar(),
      body: HallyPage(
        bottom: FilledButton(
          key: const Key('trust.continue'),
          onPressed: () {
            ref.read(analyticsProvider).track(AnalyticsEvent.trustAccepted);
            context.go('/demo');
          },
          child: Text(l10n.trustContinue),
        ),
        children: [
          const SizedBox(height: HallySpace.md),
          Center(
            child: Container(
              width: 112,
              height: 112,
              decoration: BoxDecoration(shape: BoxShape.circle, color: HallyColors.softOf(HallyColors.lavender, Theme.of(context).brightness)),
              alignment: Alignment.center,
              child: const HallyIconBadge(
                icon: Icons.shield_rounded,
                color: HallyColors.lavender,
                gradient: HallyGradients.hero,
                size: 72,
                glow: true,
              ),
            ),
          ),
          const SizedBox(height: HallySpace.lg),
          Text(l10n.trustTitle, style: Theme.of(context).textTheme.headlineMedium, textAlign: TextAlign.center),
          const SizedBox(height: HallySpace.sm),
          Text(l10n.trustSubtitle, style: Theme.of(context).textTheme.bodyLarge?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant), textAlign: TextAlign.center),
          const SizedBox(height: HallySpace.lg),
          for (final (i, (icon, color, text)) in points.indexed)
            HallyEntrance(
              index: i + 1,
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(HallySpace.md),
                  child: Row(
                    children: [
                      HallyIconBadge(icon: icon, color: color, size: 44),
                      const SizedBox(width: HallySpace.md),
                      Expanded(child: Text(text, style: Theme.of(context).textTheme.bodyLarge?.copyWith(fontWeight: FontWeight.w500))),
                    ],
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
