import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_repository.dart';
import '../capture/capture_sheet.dart';
import '../planning/planning_repository.dart';

class TodayScreen extends ConsumerWidget {
  const TodayScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final me = ref.watch(meProvider).valueOrNull;
    final familyId = me?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());
    final today = ref.watch(todayProvider(familyId));

    return Scaffold(
      appBar: AppBar(title: Text(me?.primaryFamily?.name ?? l10n.tabToday)),
      body: today.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(todayProvider(familyId))),
        data: (d) => d.isEmpty
            ? HallyEmptyState(
                icon: Icons.photo_camera_outlined,
                title: l10n.todayEmptyTitle,
                body: l10n.todayEmptyBody,
                action: FilledButton.icon(
                  onPressed: () => showCaptureSheet(context, ref),
                  icon: const Icon(Icons.add_a_photo_outlined),
                  label: Text(l10n.tabCapture),
                ),
              )
            : RefreshIndicator(
                onRefresh: () => ref.refresh(todayProvider(familyId).future),
                child: ListView(
                  padding: const EdgeInsets.all(HallySpace.md),
                  children: [
                    if (d.waiting.isNotEmpty)
                      Card(
                        color: HallyColors.lavenderSoft,
                        child: ListTile(
                          leading: const Icon(Icons.auto_awesome, color: HallyColors.lavender),
                          title: Text(l10n.todayWaiting),
                          subtitle: Text(d.waiting.map((p) => p.payload.title).take(3).join(' · ')),
                          trailing: Text(l10n.todayReview),
                          onTap: () {
                            ref.read(lastCaptureProvider.notifier).state = null;
                            context.push('/capture/review');
                          },
                        ),
                      ),
                    _TaskSection(title: l10n.todayOverdue, tasks: d.overdue, familyId: familyId, color: HallyColors.apricot),
                    _TaskSection(title: l10n.todayDue, tasks: d.dueToday, familyId: familyId, color: HallyColors.mint),
                    if (d.events.isNotEmpty) ...[
                      _Header(l10n.todayEvents),
                      for (final e in d.events)
                        Card(
                          child: ListTile(
                            leading: const Icon(Icons.event, color: HallyColors.apricot),
                            title: Text(e.title),
                            subtitle: Text(e.allDay ? l10n.reviewAllDay : TimeOfDay.fromDateTime(e.startsAt).format(context)),
                          ),
                        ),
                    ],
                  ],
                ),
              ),
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(0, HallySpace.md, 0, HallySpace.sm),
        child: Text(text, style: Theme.of(context).textTheme.titleMedium),
      );
}

class _TaskSection extends ConsumerWidget {
  const _TaskSection({required this.title, required this.tasks, required this.familyId, required this.color});

  final String title;
  final List<TaskItem> tasks;
  final String familyId;
  final Color color;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (tasks.isEmpty) return const SizedBox.shrink();
    final l10n = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _Header(title),
        for (final t in tasks)
          Card(
            child: CheckboxListTile(
              value: t.state == 'Done',
              activeColor: color,
              title: Text(t.title),
              secondary: Tooltip(message: l10n.taskComplete, child: Icon(Icons.task_alt, color: color)),
              onChanged: (_) async {
                await ref.read(planningRepositoryProvider).completeTask(familyId, t.id);
                ref.invalidate(todayProvider(familyId));
              },
            ),
          ),
      ],
    );
  }
}
