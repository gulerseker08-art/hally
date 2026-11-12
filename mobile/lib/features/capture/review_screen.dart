import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../family/family_repository.dart';
import '../planning/planning_repository.dart';
import 'capture_models.dart';
import 'capture_repository.dart';

final pendingProposalsProvider =
    FutureProvider.autoDispose.family<List<Proposal>, String>((ref, familyId) => ref.watch(captureRepositoryProvider).pending(familyId));

/// Extraction review / approval sheet. Every item is shown before anything is written.
class ReviewScreen extends ConsumerStatefulWidget {
  const ReviewScreen({super.key});

  @override
  ConsumerState<ReviewScreen> createState() => _ReviewScreenState();
}

class _Edit {
  String? title;
  String? date;
  String? assigneeUserId;
}

/// The theme gives buttons full width (Size.fromHeight); inside a Row or dialog actions that width is
/// unbounded, so inline buttons need a finite minimum size.
final _compact = FilledButton.styleFrom(minimumSize: const Size(64, 44), padding: const EdgeInsets.symmetric(horizontal: 20));

class _ReviewScreenState extends ConsumerState<ReviewScreen> {
  final _edits = <String, _Edit>{};
  final _busy = <String>{};
  final _done = <String>{};

  _Edit _edit(String id) => _edits.putIfAbsent(id, _Edit.new);

  bool _ready(Proposal p) => !p.isEvent || (_edit(p.id).date ?? p.payload.date) != null;

  Future<void> _approve(String familyId, Proposal p) async {
    final e = _edit(p.id);
    setState(() => _busy.add(p.id));
    try {
      await ref.read(captureRepositoryProvider).approve(
            familyId,
            p.id,
            ProposalEdits(title: e.title, date: e.date, assigneeUserId: e.assigneeUserId),
          );
      ref.read(analyticsProvider).track(AnalyticsEvent.proposalApproved, {'count': 1, 'edited': e.title != null || e.date != null});
      setState(() => _done.add(p.id));
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).errorGeneric)));
    } finally {
      if (mounted) setState(() => _busy.remove(p.id));
    }
  }

  Future<void> _reject(String familyId, Proposal p) async {
    setState(() => _busy.add(p.id));
    try {
      await ref.read(captureRepositoryProvider).reject(familyId, p.id);
      ref.read(analyticsProvider).track(AnalyticsEvent.proposalRejected);
      setState(() => _done.add(p.id));
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).errorGeneric)));
    } finally {
      if (mounted) setState(() => _busy.remove(p.id));
    }
  }

  Future<void> _pickDate(Proposal p) async {
    final current = DateTime.tryParse(_edit(p.id).date ?? p.payload.date ?? '');
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: current ?? now,
      firstDate: now.subtract(const Duration(days: 1)),
      lastDate: now.add(const Duration(days: 365)),
    );
    if (picked != null) {
      setState(() => _edit(p.id).date =
          '${picked.year.toString().padLeft(4, '0')}-${picked.month.toString().padLeft(2, '0')}-${picked.day.toString().padLeft(2, '0')}');
    }
  }

  Future<void> _rename(Proposal p) async {
    final controller = TextEditingController(text: _edit(p.id).title ?? p.payload.title);
    final value = await showDialog<String>(
      context: context,
      builder: (c) => AlertDialog(
        content: TextField(controller: controller, autofocus: true, maxLength: 200),
        actions: [
          TextButton(onPressed: () => Navigator.pop(c), child: Text(MaterialLocalizations.of(c).cancelButtonLabel)),
          FilledButton(
            style: _compact,
            onPressed: () => Navigator.pop(c, controller.text.trim()),
            child: Text(MaterialLocalizations.of(c).okButtonLabel),
          ),
        ],
      ),
    );
    controller.dispose();
    if (value != null && value.isNotEmpty) setState(() => _edit(p.id).title = value);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());

    final capture = ref.watch(lastCaptureProvider);
    final proposals = ref.watch(pendingProposalsProvider(familyId));
    final members = ref.watch(familyProvider(familyId)).valueOrNull?.members ?? const <FamilyMember>[];

    return Scaffold(
      appBar: AppBar(title: Text(l10n.reviewTitle)),
      body: proposals.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(pendingProposalsProvider(familyId))),
        data: (all) {
          final open = all.where((p) => !_done.contains(p.id)).toList();
          if (capture != null && capture.failed) {
            return HallyEmptyState(
              icon: Icons.cloud_off_outlined,
              color: HallyColors.apricot,
              title: l10n.captureFailed,
              body: '',
              action: OutlinedButton(
                onPressed: () async {
                  final retried = await ref.read(captureRepositoryProvider).retry(familyId, capture.id);
                  ref.read(lastCaptureProvider.notifier).state = retried;
                  ref.invalidate(pendingProposalsProvider(familyId));
                },
                child: Text(l10n.retry),
              ),
            );
          }

          if (open.isEmpty) {
            return HallyEmptyState(
              icon: _done.isEmpty ? Icons.search_off_rounded : Icons.celebration_rounded,
              color: _done.isEmpty ? HallyColors.apricot : HallyColors.mint,
              title: _done.isEmpty ? l10n.captureNothingFound : l10n.reviewDone,
              body: capture != null && capture.duplicatesSkipped > 0 ? l10n.captureDuplicates(capture.duplicatesSkipped) : '',
              action: FilledButton(
                onPressed: () {
                  ref.invalidate(todayProvider(familyId));
                  context.go('/today');
                },
                child: Text(l10n.tabToday),
              ),
            );
          }

          return Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.sm, HallySpace.md, 0),
                child: Container(
                  padding: const EdgeInsets.all(HallySpace.md - 2),
                  decoration: BoxDecoration(
                    color: HallyColors.softOf(HallyColors.mint, Theme.of(context).brightness),
                    borderRadius: BorderRadius.circular(HallyRadius.md),
                  ),
                  child: Row(
                    children: [
                      const HallyIconBadge(icon: Icons.verified_user_rounded, color: HallyColors.mint, gradient: HallyGradients.mint, size: 36),
                      const SizedBox(width: HallySpace.sm + 4),
                      Expanded(child: Text(l10n.reviewSubtitle, style: Theme.of(context).textTheme.titleSmall)),
                      HallyPill(label: '${open.length}', color: HallyColors.mintDeep, solid: true),
                    ],
                  ),
                ),
              ),
              if (capture != null && capture.duplicatesSkipped > 0)
                Padding(
                  padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.sm, HallySpace.md, 0),
                  child: Row(
                    children: [
                      Icon(Icons.copy_all_rounded, size: 16, color: Theme.of(context).colorScheme.onSurfaceVariant),
                      const SizedBox(width: 6),
                      Expanded(child: Text(l10n.captureDuplicates(capture.duplicatesSkipped), style: Theme.of(context).textTheme.bodySmall)),
                    ],
                  ),
                ),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.all(HallySpace.md),
                  children: [
                    for (final (i, p) in open.indexed)
                      HallyEntrance(key: ValueKey(p.id), index: i, child: _card(context, familyId, p, members)),
                  ],
                ),
              ),
              SafeArea(
                child: Padding(
                  padding: const EdgeInsets.all(HallySpace.md),
                  child: FilledButton.icon(
                    key: const Key('review.approveAll'),
                    icon: const Icon(Icons.done_all_rounded),
                    onPressed: _busy.isNotEmpty
                        ? null
                        : () async {
                            for (final p in open.where(_ready)) {
                              await _approve(familyId, p);
                            }
                          },
                    label: Text(l10n.reviewApproveAll),
                  ),
                ),
              ),
            ],
          );
        },
      ),
    );
  }

  Widget _card(BuildContext context, String familyId, Proposal p, List<FamilyMember> members) {
    final l10n = AppLocalizations.of(context);
    final e = _edit(p.id);
    final date = e.date ?? p.payload.date;
    final busy = _busy.contains(p.id);
    final theme = Theme.of(context);
    final accent = p.isEvent ? HallyColors.apricot : HallyColors.lavender;
    final assignee = e.assigneeUserId ?? p.payload.assigneeUserId;
    return Card(
      key: Key('review.card.${p.id}'),
      margin: const EdgeInsets.only(bottom: HallySpace.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Container(height: 5, decoration: BoxDecoration(gradient: LinearGradient(colors: [accent, Color.lerp(accent, Colors.white, 0.45)!]))),
          Padding(
            padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.md - 2, HallySpace.md, HallySpace.sm),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    HallyPill(
                      label: p.isEvent ? l10n.reviewEvent : l10n.reviewTask,
                      icon: p.isEvent ? Icons.event_rounded : Icons.check_circle_outline_rounded,
                      color: accent,
                    ),
                    const Spacer(),
                    if (p.payload.confidence == 'low')
                      HallyPill(label: l10n.reviewLowConfidence, icon: Icons.help_outline_rounded, color: HallyColors.apricot),
                  ],
                ),
                const SizedBox(height: HallySpace.sm),
                InkWell(
                  borderRadius: BorderRadius.circular(HallyRadius.sm),
                  onTap: () => _rename(p),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: HallySpace.xs),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(child: Text(e.title ?? p.payload.title, style: theme.textTheme.titleLarge?.copyWith(fontSize: 19))),
                        Tooltip(
                          message: l10n.reviewEditTitle,
                          child: Padding(
                            padding: const EdgeInsets.only(left: HallySpace.sm, top: 2),
                            child: Icon(Icons.edit_rounded, size: 18, color: theme.colorScheme.onSurfaceVariant),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                if (p.payload.notes != null) ...[
                  const SizedBox(height: 2),
                  Text(p.payload.notes!, style: theme.textTheme.bodyMedium),
                ],
                const SizedBox(height: HallySpace.sm + 2),
                Wrap(
                  spacing: HallySpace.sm,
                  runSpacing: HallySpace.sm,
                  children: [
                    ActionChip(
                      avatar: Icon(
                        date == null ? Icons.warning_amber_rounded : Icons.calendar_today_rounded,
                        size: 16,
                        color: date == null ? HallyColors.apricot : accent,
                      ),
                      label: Text(date == null
                          ? l10n.reviewNeedsDate
                          : '${_prettyDate(context, date)}${p.payload.time != null ? ' · ${p.payload.time}' : ' · ${l10n.reviewAllDay}'}'),
                      backgroundColor: date == null ? HallyColors.softOf(HallyColors.apricot, theme.brightness) : null,
                      side: date == null ? const BorderSide(color: HallyColors.apricot) : null,
                      onPressed: () => _pickDate(p),
                    ),
                    if (p.payload.location != null)
                      Chip(
                        avatar: Icon(Icons.place_rounded, size: 16, color: theme.colorScheme.onSurfaceVariant),
                        label: Text(p.payload.location!),
                      ),
                    if (p.payload.needsInfo.contains('child'))
                      Chip(
                        avatar: const Icon(Icons.child_care_rounded, size: 16, color: HallyColors.apricot),
                        label: Text(l10n.reviewNeedsChild),
                      ),
                  ],
                ),
                if (!p.isEvent && members.isNotEmpty) ...[
                  const SizedBox(height: HallySpace.md),
                  Text(l10n.reviewAssignee, style: theme.textTheme.labelLarge?.copyWith(fontSize: 13, color: theme.colorScheme.onSurfaceVariant)),
                  const SizedBox(height: HallySpace.sm),
                  SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: Row(
                      children: [
                        for (final m in members)
                          Padding(
                            padding: const EdgeInsets.only(right: HallySpace.sm),
                            child: ChoiceChip(
                              avatar: HallyAvatar(name: m.displayName, size: 22),
                              label: Text(m.displayName),
                              selected: assignee == m.userId,
                              showCheckmark: false,
                              onSelected: (_) => setState(() => e.assigneeUserId = m.userId),
                            ),
                          ),
                        ChoiceChip(
                          label: Text(l10n.reviewUnassigned),
                          selected: assignee == null,
                          showCheckmark: false,
                          onSelected: (_) => setState(() => e.assigneeUserId = null),
                        ),
                      ],
                    ),
                  ),
                ],
                const SizedBox(height: HallySpace.sm),
                Row(
                  children: [
                    TextButton.icon(
                      onPressed: busy ? null : () => _reject(familyId, p),
                      icon: const Icon(Icons.close_rounded, size: 18),
                      label: Text(l10n.reviewRemove),
                      style: TextButton.styleFrom(foregroundColor: theme.colorScheme.onSurfaceVariant),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: Key('review.approve.${p.id}'),
                      style: _compact,
                      onPressed: busy || !_ready(p) ? null : () => _approve(familyId, p),
                      child: busy
                          ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                          : Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [const Icon(Icons.check_rounded, size: 18), const SizedBox(width: 6), Text(l10n.reviewApprove)],
                            ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// "2026-11-13" → "13 Kas Cum" in the user's language.
  String _prettyDate(BuildContext context, String iso) {
    final d = DateTime.tryParse(iso);
    if (d == null) return iso;
    return DateFormat('d MMM EEE', Localizations.localeOf(context).toLanguageTag()).format(d);
  }
}
