import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/analytics/analytics.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'demo_repository.dart';
import 'demo_samples.dart';

enum _Phase { ready, scanning, done, failed }

/// Pre-signup "aha" moment: scan a sample school announcement and see the tasks Hally finds.
class DemoScanScreen extends ConsumerStatefulWidget {
  const DemoScanScreen({super.key});

  @override
  ConsumerState<DemoScanScreen> createState() => _DemoScanScreenState();
}

class _DemoScanScreenState extends ConsumerState<DemoScanScreen> {
  _Phase _phase = _Phase.ready;
  List<DemoItem> _items = const [];

  Future<void> _scan() async {
    final lang = Localizations.localeOf(context).languageCode;
    setState(() => _phase = _Phase.scanning);
    try {
      final items = await ref.read(demoRepositoryProvider).scanSample(DemoSamples.idFor(lang), lang);
      ref.read(analyticsProvider).track(AnalyticsEvent.demoScanned, {'count': items.length});
      ref.read(pendingDemoTextProvider.notifier).state = DemoSamples.textFor(lang);
      if (mounted) setState(() => (_items, _phase) = (items, _Phase.done));
    } catch (_) {
      if (mounted) setState(() => _phase = _Phase.failed);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final lang = Localizations.localeOf(context).languageCode;
    return Scaffold(
      appBar: AppBar(
        actions: [
          TextButton(key: const Key('demo.skip'), onPressed: () => context.go('/sign-in'), child: Text(l10n.demoSkip)),
        ],
      ),
      body: HallyPage(
        bottom: switch (_phase) {
          _Phase.done => FilledButton(
              key: const Key('demo.save'),
              onPressed: () => context.go('/sign-in'),
              child: Text(l10n.demoSave),
            ),
          _Phase.scanning => const FilledButton(onPressed: null, child: SizedBox.square(dimension: 22, child: CircularProgressIndicator(strokeWidth: 2))),
          _ => FilledButton.icon(
              key: const Key('demo.scan'),
              onPressed: _scan,
              icon: const Icon(Icons.document_scanner_outlined),
              label: Text(_phase == _Phase.failed ? l10n.retry : l10n.demoScan),
            ),
        },
        children: [
          Text(_phase == _Phase.done ? l10n.demoFoundTitle(_items.length) : l10n.demoTitle, style: theme.textTheme.headlineSmall),
          const SizedBox(height: HallySpace.sm),
          Text(_phase == _Phase.done ? l10n.demoFoundBody : l10n.demoBody, style: theme.textTheme.bodyMedium),
          const SizedBox(height: HallySpace.lg),
          if (_phase != _Phase.done) _SampleCard(text: DemoSamples.textFor(lang), label: l10n.demoSampleLabel, scanning: _phase == _Phase.scanning),
          if (_phase == _Phase.failed) ...[
            const SizedBox(height: HallySpace.md),
            Text(l10n.captureFailed, style: theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.error)),
          ],
          if (_phase == _Phase.done)
            for (final (i, item) in _items.indexed)
              HallyEntrance(
                index: i,
                child: Card(
                  margin: const EdgeInsets.only(bottom: HallySpace.sm + 4),
                  child: ListTile(
                    leading: HallyIconBadge(
                      icon: item.isEvent ? Icons.event_rounded : Icons.check_circle_outline_rounded,
                      color: item.isEvent ? HallyColors.apricot : HallyColors.lavender,
                    ),
                    title: Text(item.payload.title, style: theme.textTheme.titleSmall),
                    subtitle: Text(
                      [
                        if (item.payload.date != null) item.payload.date!,
                        if (item.payload.time != null) item.payload.time!,
                        if (item.payload.date == null) l10n.reviewNeedsDate,
                      ].join(' · '),
                    ),
                  ),
                ),
              ),
        ],
      ),
    );
  }
}

class _SampleCard extends StatelessWidget {
  const _SampleCard({required this.text, required this.label, required this.scanning});

  final String text;
  final String label;
  final bool scanning;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AnimatedOpacity(
      duration: HallyMotion.medium,
      opacity: scanning ? 0.6 : 1,
      child: Container(
        padding: const EdgeInsets.all(HallySpace.md),
        decoration: BoxDecoration(
          color: theme.colorScheme.surface,
          borderRadius: BorderRadius.circular(HallyRadius.md),
          border: Border.all(color: theme.colorScheme.outlineVariant),
          boxShadow: HallyShadows.soft(theme.brightness),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            HallyPill(label: label, color: HallyColors.sky, icon: Icons.school_outlined),
            const SizedBox(height: HallySpace.sm + 4),
            Text(text, style: theme.textTheme.bodyMedium?.copyWith(height: 1.45)),
          ],
        ),
      ),
    );
  }
}
