import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:share_plus/share_plus.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'sharing_repository.dart';

typedef _CardArgs = (String familyId, int offset, bool includeChildNames);

final _cardProvider = FutureProvider.autoDispose.family<Uint8List, _CardArgs>(
    (ref, a) => ref.watch(sharingRepositoryProvider).weeklyCard(a.$1, offset: a.$2, includeChildNames: a.$3));

/// The weekly summary card (rendered by the server) with a share button.
class WeeklyCardScreen extends ConsumerStatefulWidget {
  const WeeklyCardScreen({super.key});

  @override
  ConsumerState<WeeklyCardScreen> createState() => _WeeklyCardScreenState();
}

class _WeeklyCardScreenState extends ConsumerState<WeeklyCardScreen> {
  int _offset = 0;
  bool _children = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());
    final args = (familyId, _offset, _children);
    final card = ref.watch(_cardProvider(args));

    return Scaffold(
      appBar: AppBar(title: Text(l10n.weeklyCardTitle)),
      body: HallyPage(
        bottom: FilledButton.icon(
          key: const Key('weekly.share'),
          onPressed: card.valueOrNull == null
              ? null
              : () async {
                  ref.read(analyticsProvider).track(AnalyticsEvent.summaryCardShared, {'offset': _offset, 'children': _children});
                  await Share.shareXFiles([XFile.fromData(card.value!, mimeType: 'image/png', name: 'hally-week.png')]);
                },
          icon: const Icon(Icons.ios_share_rounded),
          label: Text(l10n.weeklyCardShare),
        ),
        children: [
          SegmentedButton<int>(
            segments: [
              ButtonSegment(value: 0, label: Text(l10n.weeklyThisWeek)),
              ButtonSegment(value: -1, label: Text(l10n.weeklyLastWeek)),
            ],
            selected: {_offset},
            onSelectionChanged: (s) => setState(() => _offset = s.first),
          ),
          SwitchListTile(
            key: const Key('weekly.children'),
            contentPadding: EdgeInsets.zero,
            value: _children,
            title: Text(l10n.weeklyShowChildren),
            subtitle: Text(l10n.weeklyShowChildrenBody),
            onChanged: (v) => setState(() => _children = v),
          ),
          const SizedBox(height: HallySpace.sm),
          AspectRatio(
            aspectRatio: 4 / 5,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(HallyRadius.lg),
              child: card.when(
                loading: () => const HallyLoading(),
                error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(_cardProvider(args))),
                data: (bytes) => Image.memory(bytes, key: const Key('weekly.image'), fit: BoxFit.cover, gaplessPlayback: true),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Active helper links with a revoke button.
class ShareLinksScreen extends ConsumerWidget {
  const ShareLinksScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());
    final links = ref.watch(activeLinksProvider(familyId));
    return Scaffold(
      appBar: AppBar(title: Text(l10n.helperLinksTitle)),
      body: links.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(activeLinksProvider(familyId))),
        data: (items) => items.isEmpty
            ? HallyEmptyState(icon: Icons.link_off_rounded, title: l10n.helperLinksEmpty, body: l10n.helperLinksEmptyBody)
            : ListView(
                padding: const EdgeInsets.all(HallySpace.md),
                children: [
                  for (final l in items)
                    Card(
                      child: ListTile(
                        leading: HallyIconBadge(icon: l.kind == 'Day' ? Icons.today_rounded : Icons.task_alt_rounded, color: HallyColors.mint),
                        title: Text(l.kind == 'Day' ? l10n.helperLinkDay(l.date ?? '') : l10n.helperLinkTask),
                        subtitle: Text(l10n.helperLinkOpened(l.openCount)),
                        trailing: TextButton(
                          key: Key('link.${l.id}.revoke'),
                          onPressed: () async {
                            await ref.read(sharingRepositoryProvider).revoke(familyId, l.id);
                            ref.invalidate(activeLinksProvider(familyId));
                          },
                          child: Text(l10n.helperLinkRevoke),
                        ),
                      ),
                    ),
                ],
              ),
      ),
    );
  }
}
