import 'package:flutter/material.dart';

import '../../l10n/gen/app_localizations.dart';
import 'tokens.dart';

/// Standard padded page body used by onboarding-style screens.
class HallyPage extends StatelessWidget {
  const HallyPage({super.key, required this.children, this.bottom});

  final List<Widget> children;
  final Widget? bottom;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(HallySpace.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(child: ListView(children: children)),
            if (bottom != null) bottom!,
          ],
        ),
      ),
    );
  }
}

/// Empty state that always teaches the next action.
class HallyEmptyState extends StatelessWidget {
  const HallyEmptyState({super.key, required this.icon, required this.title, required this.body, this.action});

  final IconData icon;
  final String title;
  final String body;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(HallySpace.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            CircleAvatar(
              radius: 36,
              backgroundColor: HallyColors.lavenderSoft,
              child: Icon(icon, size: 36, color: HallyColors.lavender),
            ),
            const SizedBox(height: HallySpace.md),
            Text(title, style: theme.textTheme.titleLarge, textAlign: TextAlign.center),
            const SizedBox(height: HallySpace.sm),
            Text(body, style: theme.textTheme.bodyMedium, textAlign: TextAlign.center),
            if (action != null) ...[const SizedBox(height: HallySpace.lg), action!],
          ],
        ),
      ),
    );
  }
}

class HallyErrorState extends StatelessWidget {
  const HallyErrorState({super.key, required this.onRetry});

  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return HallyEmptyState(
      icon: Icons.cloud_off_outlined,
      title: l10n.errorGeneric,
      body: '',
      action: OutlinedButton(onPressed: onRetry, child: Text(l10n.retry)),
    );
  }
}

class HallyLoading extends StatelessWidget {
  const HallyLoading({super.key});

  @override
  Widget build(BuildContext context) => const Center(child: CircularProgressIndicator());
}
