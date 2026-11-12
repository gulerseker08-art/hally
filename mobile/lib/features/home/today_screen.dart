import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_repository.dart';
import '../capture/capture_sheet.dart';
import '../notifications/notifications_screen.dart';
import '../planning/planning_repository.dart';

class TodayScreen extends ConsumerWidget {
  const TodayScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final me = ref.watch(meProvider).valueOrNull;
    final familyId = me?.primaryFamily?.id;
    if (me == null || familyId == null) return const Scaffold(body: HallyLoading());
    final today = ref.watch(todayProvider(familyId));

    return Scaffold(
      body: RefreshIndicator(
        edgeOffset: 120,
        onRefresh: () => ref.refresh(todayProvider(familyId).future),
        child: CustomScrollView(
          physics: const AlwaysScrollableScrollPhysics(parent: BouncingScrollPhysics()),
          slivers: [
            SliverToBoxAdapter(child: TodayHero(name: me.displayName, data: today.valueOrNull)),
            ...today.when(
              loading: () => [const SliverFillRemaining(hasScrollBody: false, child: HallyLoading())],
              error: (_, __) => [
                SliverFillRemaining(
                  hasScrollBody: false,
                  child: HallyErrorState(onRetry: () => ref.invalidate(todayProvider(familyId))),
                ),
              ],
              data: (d) => [
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.md, HallySpace.md, 120),
                  sliver: SliverList.list(children: _body(context, ref, d, familyId)),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  List<Widget> _body(BuildContext context, WidgetRef ref, TodayData d, String familyId) {
    final l10n = AppLocalizations.of(context);
    var i = 0;
    return [
      HallyEntrance(index: i++, child: const _QuickCapture()),
      if (d.waiting.isNotEmpty)
        HallyEntrance(
          index: i++,
          child: _WaitingCard(
            titles: [for (final p in d.waiting) p.payload.title],
            onTap: () {
              ref.read(lastCaptureProvider.notifier).state = null;
              context.push('/capture/review');
            },
          ),
        ),
      if (d.isEmpty)
        HallyEntrance(
          index: i++,
          child: Padding(
            padding: const EdgeInsets.only(top: HallySpace.lg),
            child: HallyEmptyState(
              icon: Icons.wb_sunny_rounded,
              color: HallyColors.mint,
              title: l10n.todayEmptyTitle,
              body: l10n.todayEmptyBody,
            ),
          ),
        ),
      if (d.overdue.isNotEmpty) ...[
        HallySectionHeader(title: l10n.todayOverdue, color: HallyColors.apricot, count: d.overdue.length),
        for (final t in d.overdue) HallyEntrance(index: i++, child: TaskTile(task: t, familyId: familyId, color: HallyColors.apricot)),
      ],
      if (d.dueToday.isNotEmpty) ...[
        HallySectionHeader(title: l10n.todayDue, color: HallyColors.mint, count: d.dueToday.length),
        for (final t in d.dueToday) HallyEntrance(index: i++, child: TaskTile(task: t, familyId: familyId, color: HallyColors.mint)),
      ],
      if (d.events.isNotEmpty) ...[
        HallySectionHeader(title: l10n.todayEvents, color: HallyColors.lavender, count: d.events.length),
        for (final (n, e) in d.events.indexed)
          HallyEntrance(index: i++, child: _EventTile(event: e, last: n == d.events.length - 1)),
      ],
    ];
  }
}

/// Gradient header with greeting, date and a summary of the day.
class TodayHero extends StatelessWidget {
  const TodayHero({super.key, required this.name, required this.data});

  final String name;
  final TodayData? data;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final now = DateTime.now();
    final first = name.trim().split(' ').first;
    final greeting = now.hour < 12
        ? l10n.todayGreetingMorning(first)
        : now.hour < 18
            ? l10n.todayGreetingAfternoon(first)
            : l10n.todayGreetingEvening(first);
    final locale = Localizations.localeOf(context).toLanguageTag();
    final date = DateFormat('d MMMM EEEE', locale).format(now);
    final d = data;

    return Container(
      decoration: const BoxDecoration(
        gradient: HallyGradients.hero,
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(HallyRadius.xl + 4)),
      ),
      clipBehavior: Clip.antiAlias,
      child: Stack(
        children: [
          const Positioned(top: -60, right: -40, child: HallyGlow(color: Color(0xFFF0A36A), size: 220)),
          const Positioned(bottom: -80, left: -50, child: HallyGlow(color: Color(0xFF6FD3B4), size: 200)),
          SafeArea(
            bottom: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(HallySpace.lg, HallySpace.md, HallySpace.lg, HallySpace.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          date.toUpperCase(),
                          style: theme.textTheme.labelMedium?.copyWith(color: Colors.white70, letterSpacing: 1.2, fontWeight: FontWeight.w700),
                        ),
                      ),
                      IconButton(
                        key: const Key('today.tasks'),
                        tooltip: l10n.todayAllTasks,
                        onPressed: () => context.push('/tasks'),
                        icon: const Icon(Icons.checklist_rounded, color: Colors.white),
                      ),
                      const NotificationBell(),
                      const SizedBox(width: HallySpace.xs),
                      HallyAvatar(name: name, size: 40, ring: true),
                    ],
                  ),
                  const SizedBox(height: HallySpace.sm),
                  Text(greeting, style: theme.textTheme.headlineMedium?.copyWith(color: Colors.white, height: 1.1)),
                  const SizedBox(height: HallySpace.md),
                  AnimatedOpacity(
                    opacity: d == null ? 0 : 1,
                    duration: HallyMotion.medium,
                    child: Wrap(
                      spacing: HallySpace.sm,
                      runSpacing: HallySpace.sm,
                      children: [
                        _Stat(icon: Icons.check_circle_outline_rounded, label: l10n.todayStatTasks((d?.overdue.length ?? 0) + (d?.dueToday.length ?? 0))),
                        _Stat(icon: Icons.event_rounded, label: l10n.todayStatEvents(d?.events.length ?? 0)),
                        if ((d?.waiting.length ?? 0) > 0) _Stat(icon: Icons.auto_awesome, label: l10n.todayStatWaiting(d!.waiting.length), hot: true),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.icon, required this.label, this.hot = false});

  final IconData icon;
  final String label;
  final bool hot;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
        decoration: BoxDecoration(
          color: hot ? Colors.white : Colors.white.withValues(alpha: 0.16),
          borderRadius: BorderRadius.circular(HallyRadius.pill),
          border: Border.all(color: Colors.white.withValues(alpha: 0.25)),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 16, color: hot ? HallyColors.rose : Colors.white),
            const SizedBox(width: 6),
            Text(
              label,
              style: TextStyle(color: hot ? HallyColors.ink : Colors.white, fontWeight: FontWeight.w700, fontSize: 13),
            ),
          ],
        ),
      );
}

/// Three one-tap capture shortcuts so the core action is never more than a tap away.
class _QuickCapture extends ConsumerWidget {
  const _QuickCapture();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    Widget tile(IconData icon, Gradient g, Color c, String label, VoidCallback onTap) => Expanded(
          child: HallyPressable(
            onTap: onTap,
            child: Container(
              padding: const EdgeInsets.symmetric(vertical: HallySpace.md - 2),
              decoration: BoxDecoration(
                color: theme.colorScheme.surface,
                borderRadius: BorderRadius.circular(HallyRadius.lg),
                border: Border.all(color: theme.colorScheme.outlineVariant),
                boxShadow: HallyShadows.soft(theme.brightness),
              ),
              child: Column(
                children: [
                  HallyIconBadge(icon: icon, color: c, gradient: g, size: 42),
                  const SizedBox(height: HallySpace.sm),
                  Text(label, style: theme.textTheme.labelLarge?.copyWith(fontSize: 13), textAlign: TextAlign.center, maxLines: 1),
                ],
              ),
            ),
          ),
        );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(HallySpace.xs, HallySpace.xs, 0, HallySpace.sm + 2),
          child: Text(l10n.todayQuickCapture, style: theme.textTheme.titleMedium?.copyWith(fontSize: 17)),
        ),
        Row(
          children: [
            tile(Icons.photo_camera_rounded, HallyGradients.lavender, HallyColors.lavender, l10n.captureCamera,
                () => startPhotoCapture(context, ref, camera: true)),
            const SizedBox(width: HallySpace.sm + 2),
            tile(Icons.photo_library_rounded, HallyGradients.mint, HallyColors.mint, l10n.captureGallery,
                () => startPhotoCapture(context, ref, camera: false)),
            const SizedBox(width: HallySpace.sm + 2),
            tile(Icons.chat_rounded, HallyGradients.apricot, HallyColors.apricot, l10n.captureText, () => context.push('/capture/text')),
          ],
        ),
      ],
    );
  }
}

class _WaitingCard extends StatelessWidget {
  const _WaitingCard({required this.titles, required this.onTap});

  final List<String> titles;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: HallySpace.md),
      child: HallyPressable(
        onTap: onTap,
        child: Container(
          decoration: BoxDecoration(
            gradient: HallyGradients.magic,
            borderRadius: BorderRadius.circular(HallyRadius.lg),
            boxShadow: HallyShadows.glow(HallyColors.rose),
          ),
          clipBehavior: Clip.antiAlias,
          child: Stack(
            children: [
              const Positioned(right: -30, top: -30, child: HallyGlow(color: Colors.white, size: 140)),
              Padding(
                padding: const EdgeInsets.all(HallySpace.md + 2),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Container(
                          padding: const EdgeInsets.all(8),
                          decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.22), borderRadius: BorderRadius.circular(12)),
                          child: const Icon(Icons.auto_awesome, color: Colors.white, size: 20),
                        ),
                        const SizedBox(width: HallySpace.sm + 4),
                        Expanded(
                          child: Text(l10n.todayWaiting, style: theme.textTheme.titleLarge?.copyWith(color: Colors.white)),
                        ),
                        HallyPill(label: '${titles.length}', color: Colors.white.withValues(alpha: 0.25), solid: true),
                      ],
                    ),
                    const SizedBox(height: HallySpace.sm),
                    Text(l10n.todayWaitingBody(titles.length), style: const TextStyle(color: Colors.white, fontSize: 14, height: 1.35)),
                    const SizedBox(height: HallySpace.md),
                    for (final t in titles.take(3))
                      Padding(
                        padding: const EdgeInsets.only(bottom: 6),
                        child: Row(
                          children: [
                            const Icon(Icons.circle, size: 6, color: Colors.white70),
                            const SizedBox(width: HallySpace.sm),
                            Expanded(
                              child: Text(
                                t,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600),
                              ),
                            ),
                          ],
                        ),
                      ),
                    const SizedBox(height: HallySpace.sm),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(HallyRadius.pill)),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            l10n.todayReview,
                            style: const TextStyle(color: HallyColors.lavenderDeep, fontWeight: FontWeight.w800),
                          ),
                          const SizedBox(width: 6),
                          const Icon(Icons.arrow_forward_rounded, size: 18, color: HallyColors.lavenderDeep),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A task row: tap the circle or swipe right to complete. The check animates before the row disappears.
class TaskTile extends ConsumerStatefulWidget {
  const TaskTile({super.key, required this.task, required this.familyId, required this.color});

  final TaskItem task;
  final String familyId;
  final Color color;

  @override
  ConsumerState<TaskTile> createState() => _TaskTileState();
}

class _TaskTileState extends ConsumerState<TaskTile> {
  bool _checked = false;

  Future<void> _complete() async {
    if (_checked) return;
    HapticFeedback.lightImpact();
    setState(() => _checked = true);
    final messenger = ScaffoldMessenger.of(context);
    final l10n = AppLocalizations.of(context);
    try {
      await ref.read(planningRepositoryProvider).completeTask(widget.familyId, widget.task.id);
      messenger.showSnackBar(SnackBar(content: Text('🎉  ${l10n.taskDoneToast}'), duration: const Duration(seconds: 2)));
      await Future<void>.delayed(HallyMotion.medium);
      ref.invalidate(todayProvider(widget.familyId));
    } catch (_) {
      if (mounted) setState(() => _checked = false);
      messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final t = widget.task;
    final done = _checked || t.state == 'Done';
    final time = t.dueAt != null && !t.allDay ? TimeOfDay.fromDateTime(t.dueAt!).format(context) : null;

    return Dismissible(
      key: ValueKey('task.${t.id}'),
      direction: DismissDirection.startToEnd,
      confirmDismiss: (_) async {
        await _complete();
        return false;
      },
      background: Container(
        margin: const EdgeInsets.only(bottom: HallySpace.sm + 2),
        padding: const EdgeInsets.symmetric(horizontal: HallySpace.lg),
        alignment: Alignment.centerLeft,
        decoration: BoxDecoration(gradient: HallyGradients.mint, borderRadius: BorderRadius.circular(HallyRadius.lg)),
        child: Row(
          children: [
            const Icon(Icons.check_rounded, color: Colors.white),
            const SizedBox(width: HallySpace.sm),
            Text(l10n.taskSwipeDone, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700)),
          ],
        ),
      ),
      child: AnimatedOpacity(
        opacity: done ? 0.55 : 1,
        duration: HallyMotion.medium,
        child: Card(
          child: InkWell(
            onTap: _complete,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: HallySpace.md, vertical: HallySpace.md - 2),
              child: Row(
                children: [
                  Tooltip(
                    message: l10n.taskComplete,
                    child: AnimatedContainer(
                      duration: HallyMotion.medium,
                      curve: Curves.easeOutBack,
                      width: 28,
                      height: 28,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: done ? widget.color : Colors.transparent,
                        border: Border.all(color: widget.color, width: 2.2),
                      ),
                      child: AnimatedScale(
                        scale: done ? 1 : 0,
                        duration: HallyMotion.medium,
                        curve: Curves.easeOutBack,
                        child: const Icon(Icons.check_rounded, size: 18, color: Colors.white),
                      ),
                    ),
                  ),
                  const SizedBox(width: HallySpace.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        AnimatedDefaultTextStyle(
                          duration: HallyMotion.medium,
                          style: theme.textTheme.titleMedium!.copyWith(
                            decoration: done ? TextDecoration.lineThrough : null,
                            decorationColor: theme.colorScheme.onSurfaceVariant,
                          ),
                          child: Text(t.title),
                        ),
                        if (time != null) ...[
                          const SizedBox(height: 2),
                          Row(
                            children: [
                              Icon(Icons.schedule_rounded, size: 14, color: theme.colorScheme.onSurfaceVariant),
                              const SizedBox(width: 4),
                              Text(time, style: theme.textTheme.bodySmall),
                            ],
                          ),
                        ],
                      ],
                    ),
                  ),
                  Container(width: 4, height: 28, decoration: BoxDecoration(color: widget.color.withValues(alpha: 0.5), borderRadius: BorderRadius.circular(2))),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Timeline-style event row: time on the left, a coloured rail, then the title.
class _EventTile extends StatelessWidget {
  const _EventTile({required this.event, required this.last});

  final EventItem event;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final time = event.allDay ? l10n.reviewAllDay : TimeOfDay.fromDateTime(event.startsAt).format(context);
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: 58,
            child: Padding(
              padding: const EdgeInsets.only(top: HallySpace.md),
              child: Text(time, style: theme.textTheme.labelLarge?.copyWith(fontSize: 13, color: HallyColors.lavender)),
            ),
          ),
          Column(
            children: [
              const SizedBox(height: HallySpace.md + 2),
              Container(
                width: 12,
                height: 12,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: HallyColors.lavender,
                  border: Border.all(color: HallyColors.softOf(HallyColors.lavender, theme.brightness), width: 3),
                ),
              ),
              Expanded(
                child: Container(width: 2, color: last ? Colors.transparent : HallyColors.softOf(HallyColors.lavender, theme.brightness)),
              ),
            ],
          ),
          const SizedBox(width: HallySpace.sm + 4),
          Expanded(
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(HallySpace.md),
                child: Row(
                  children: [
                    Expanded(child: Text(event.title, style: theme.textTheme.titleMedium)),
                    const HallyIconBadge(icon: Icons.event_rounded, color: HallyColors.lavender, size: 34),
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
