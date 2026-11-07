import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/analytics/analytics.dart';
import '../../core/config/app_config.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

class WelcomeScreen extends ConsumerWidget {
  const WelcomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    return Scaffold(
      body: HallyPage(
        bottom: Column(
          children: [
            FilledButton(
              key: const Key('welcome.start'),
              onPressed: () {
                ref.read(analyticsProvider).track(AnalyticsEvent.onboardingStarted);
                context.go('/trust');
              },
              child: Text(l10n.welcomeStart),
            ),
            const SizedBox(height: HallySpace.sm),
            TextButton(onPressed: () => context.go('/join'), child: Text(l10n.welcomeHaveInvite)),
          ],
        ),
        children: [
          const SizedBox(height: HallySpace.xxl),
          Text(AppConfig.appName, style: theme.textTheme.displaySmall?.copyWith(color: HallyColors.lavender)),
          const SizedBox(height: HallySpace.sm),
          Text(l10n.appTagline(AppConfig.appName), style: theme.textTheme.titleMedium),
          const SizedBox(height: HallySpace.xl),
          Text(l10n.welcomeTitle, style: theme.textTheme.headlineMedium),
          const SizedBox(height: HallySpace.md),
          Text(l10n.welcomeBody, style: theme.textTheme.bodyLarge),
        ],
      ),
    );
  }
}
