import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../notifications/reminder_sheet.dart';
import 'planning_repository.dart';

/// All family tasks: open (by due date) and done, with quick add.
class TasksScreen extends ConsumerStatefulWidget {
  const TasksScreen({super.key});

  @override
  ConsumerState<TasksScreen> createState() => _TasksScreenState();
}

class _TasksScreenState extends ConsumerState<TasksScreen> {
  String _state = 'Open';

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toLanguageTag();
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());
    final args = (familyId, _state);
    final tasks = ref.watch(tasksProvider(args));

    return Scaffold(
      appBar: AppBar(title: Text(l10n.tabTasks)),
      floatingActionButton: Padding(
        padding: const EdgeInsets.only(bottom: 88),
        child: FloatingActionButton.extended(
          key: const Key('tasks.add'),
          onPressed: () => _addTask(familyId),
          icon: const Icon(Icons.add),
          label: Text(l10n.taskAdd),
        ),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: HallySpace.md),
            child: SegmentedButton<String>(
              segments: [
                ButtonSegment(value: 'Open', label: Text(l10n.tasksOpen)),
                ButtonSegment(value: 'Done', label: Text(l10n.tasksDone)),
              ],
              selected: {_state},
              onSelectionChanged: (s) => setState(() => _state = s.first),
            ),
          ),
          Expanded(
            child: tasks.when(
              loading: () => const HallyLoading(),
              error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(tasksProvider(args))),
              data: (items) => items.isEmpty
                  ? HallyEmptyState(icon: Icons.task_alt_rounded, title: l10n.tasksEmpty, body: l10n.tasksEmptyBody)
                  : RefreshIndicator(
                      onRefresh: () => ref.refresh(tasksProvider(args).future),
                      child: ListView(
                        padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.sm, HallySpace.md, 120),
                        children: [
                          for (final t in items)
                            Card(
                              child: CheckboxListTile(
                                key: Key('task.${t.id}'),
                                value: t.state == 'Done',
                                controlAffinity: ListTileControlAffinity.leading,
                                secondary: t.state == 'Done'
                                    ? null
                                    : IconButton(
                                        key: Key('task.${t.id}.remind'),
                                        icon: const Icon(Icons.notification_add_outlined),
                                        tooltip: l10n.reminderTitle,
                                        onPressed: () => showReminderSheet(context, ref,
                                            familyId: familyId, target: 'Task', targetId: t.id, title: t.title, at: t.dueAt, allDay: t.allDay),
                                      ),
                                activeColor: HallyColors.mint,
                                title: Text(t.title),
                                subtitle: Text(t.dueAt == null
                                    ? l10n.taskNoDate
                                    : t.allDay
                                        ? DateFormat.MMMEd(locale).format(t.dueAt!)
                                        : DateFormat.MMMEd(locale).add_Hm().format(t.dueAt!)),
                                onChanged: (_) async {
                                  final repo = ref.read(planningRepositoryProvider);
                                  if (t.state == 'Done') {
                                    await repo.reopenTask(familyId, t.id);
                                  } else {
                                    await repo.completeTask(familyId, t.id);
                                  }
                                  ref.invalidate(tasksProvider((familyId, 'Open')));
                                  ref.invalidate(tasksProvider((familyId, 'Done')));
                                  ref.invalidate(todayProvider(familyId));
                                },
                              ),
                            ),
                        ],
                      ),
                    ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _addTask(String familyId) async {
    final l10n = AppLocalizations.of(context);
    final messenger = ScaffoldMessenger.of(context);
    final result = await showDialog<(String, DateTime?)>(context: context, builder: (_) => const _TaskDialog());
    if (result == null) return;
    final (title, due) = result;
    try {
      await ref.read(planningRepositoryProvider).createTask(familyId, title: title, dueDate: due);
      ref.invalidate(tasksProvider((familyId, 'Open')));
      ref.invalidate(todayProvider(familyId));
    } catch (_) {
      messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
    }
  }
}

/// Owns its controller so it is disposed only after the dialog's exit animation.
class _TaskDialog extends StatefulWidget {
  const _TaskDialog();

  @override
  State<_TaskDialog> createState() => _TaskDialogState();
}

class _TaskDialogState extends State<_TaskDialog> {
  final _title = TextEditingController();
  DateTime? _due;

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toLanguageTag();
    return AlertDialog(
      title: Text(l10n.taskAdd),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(key: const Key('task.title'), controller: _title, autofocus: true, maxLength: 200),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.event),
            title: Text(_due == null ? l10n.taskNoDate : DateFormat.yMMMEd(locale).format(_due!)),
            onTap: () async {
              final picked = await showDatePicker(
                context: context,
                initialDate: DateTime.now(),
                firstDate: DateTime.now().subtract(const Duration(days: 1)),
                lastDate: DateTime.now().add(const Duration(days: 730)),
              );
              if (picked != null) setState(() => _due = picked);
            },
          ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: Text(MaterialLocalizations.of(context).cancelButtonLabel)),
        FilledButton(
          style: FilledButton.styleFrom(minimumSize: const Size(64, 40)),
          onPressed: () {
            final title = _title.text.trim();
            if (title.isEmpty) return;
            Navigator.pop(context, (title, _due));
          },
          child: Text(l10n.eventSave),
        ),
      ],
    );
  }
}
