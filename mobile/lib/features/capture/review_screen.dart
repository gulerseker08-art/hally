import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

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
final _compact = FilledButton.styleFrom(minimumSize: const Size(64, 40));

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
              icon: _done.isEmpty ? Icons.search_off : Icons.check_circle_outline,
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
                padding: const EdgeInsets.all(HallySpace.md),
                child: Row(
                  children: [
                    const Icon(Icons.verified_user_outlined, color: HallyColors.mint),
                    const SizedBox(width: HallySpace.sm),
                    Expanded(child: Text(l10n.reviewSubtitle)),
                  ],
                ),
              ),
              if (capture != null && capture.duplicatesSkipped > 0)
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: HallySpace.md),
                  child: Text(l10n.captureDuplicates(capture.duplicatesSkipped), style: Theme.of(context).textTheme.bodySmall),
                ),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.all(HallySpace.md),
                  children: [for (final p in open) _card(context, familyId, p, members)],
                ),
              ),
              SafeArea(
                child: Padding(
                  padding: const EdgeInsets.all(HallySpace.md),
                  child: FilledButton(
                    key: const Key('review.approveAll'),
                    onPressed: _busy.isNotEmpty
                        ? null
                        : () async {
                            for (final p in open.where(_ready)) {
                              await _approve(familyId, p);
                            }
                          },
                    child: Text(l10n.reviewApproveAll),
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
    return Card(
      key: Key('review.card.${p.id}'),
      margin: const EdgeInsets.only(bottom: HallySpace.md),
      child: Padding(
        padding: const EdgeInsets.all(HallySpace.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Chip(
                  label: Text(p.isEvent ? l10n.reviewEvent : l10n.reviewTask),
                  backgroundColor: p.isEvent ? HallyColors.apricotSoft : HallyColors.lavenderSoft,
                  visualDensity: VisualDensity.compact,
                ),
                const Spacer(),
                if (p.payload.confidence == 'low') const Icon(Icons.help_outline, color: HallyColors.apricot, size: 20),
              ],
            ),
            InkWell(
              onTap: () => _rename(p),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: HallySpace.sm),
                child: Text(e.title ?? p.payload.title, style: theme.textTheme.titleMedium),
              ),
            ),
            if (p.payload.notes != null) Text(p.payload.notes!, style: theme.textTheme.bodySmall),
            const SizedBox(height: HallySpace.sm),
            Wrap(
              spacing: HallySpace.sm,
              children: [
                ActionChip(
                  avatar: Icon(date == null ? Icons.warning_amber : Icons.event, size: 18),
                  label: Text(date == null
                      ? l10n.reviewNeedsDate
                      : '$date${p.payload.time != null ? ' ${p.payload.time}' : ' · ${l10n.reviewAllDay}'}'),
                  backgroundColor: date == null ? HallyColors.apricotSoft : null,
                  onPressed: () => _pickDate(p),
                ),
                if (p.payload.needsInfo.contains('child')) Chip(label: Text(l10n.reviewNeedsChild)),
              ],
            ),
            if (!p.isEvent && members.isNotEmpty)
              DropdownButton<String?>(
                isExpanded: true,
                value: e.assigneeUserId ?? p.payload.assigneeUserId,
                hint: Text(l10n.reviewAssignee),
                items: [
                  DropdownMenuItem<String?>(value: null, child: Text(l10n.reviewUnassigned)),
                  for (final m in members) DropdownMenuItem<String?>(value: m.userId, child: Text(m.displayName)),
                ],
                onChanged: (v) => setState(() => e.assigneeUserId = v),
              ),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                TextButton(onPressed: busy ? null : () => _reject(familyId, p), child: Text(l10n.reviewRemove)),
                const SizedBox(width: HallySpace.sm),
                FilledButton.tonal(
                  key: Key('review.approve.${p.id}'),
                  style: _compact,
                  onPressed: busy || !_ready(p) ? null : () => _approve(familyId, p),
                  child: Text(l10n.reviewApprove),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
