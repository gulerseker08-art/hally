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
      (Icons.verified_user_outlined, HallyColors.lavender, l10n.trustPoint1),
      (Icons.lock_outline, HallyColors.mint, l10n.trustPoint2),
      (Icons.delete_outline, HallyColors.apricot, l10n.trustPoint3),
    ];
    return Scaffold(
      appBar: AppBar(),
      body: HallyPage(
        bottom: FilledButton(
          key: const Key('trust.continue'),
          onPressed: () {
            ref.read(analyticsProvider).track(AnalyticsEvent.trustAccepted);
            context.go('/sign-in');
          },
          child: Text(l10n.trustContinue),
        ),
        children: [
          Text(l10n.trustTitle, style: Theme.of(context).textTheme.headlineMedium),
          const SizedBox(height: HallySpace.lg),
          for (final (icon, color, text) in points)
            Padding(
              padding: const EdgeInsets.only(bottom: HallySpace.md),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(icon, color: color),
                  const SizedBox(width: HallySpace.md),
                  Expanded(child: Text(text, style: Theme.of(context).textTheme.bodyLarge)),
                ],
              ),
            ),
        ],
      ),
    );
  }
}
