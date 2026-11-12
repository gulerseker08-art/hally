import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../notifications/reminder_sheet.dart';
import '../sharing/helper_share_sheet.dart';
import '../sharing/sharing_repository.dart';
import 'planning_repository.dart';

/// Week strip + the selected day's events. Events come from approved proposals or manual entry.
class CalendarScreen extends ConsumerStatefulWidget {
  const CalendarScreen({super.key});

  @override
  ConsumerState<CalendarScreen> createState() => _CalendarScreenState();
}

class _CalendarScreenState extends ConsumerState<CalendarScreen> {
  DateTime _selected = DateUtils.dateOnly(DateTime.now());

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final locale = Localizations.localeOf(context).toLanguageTag();
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());

    final monday = mondayOf(_selected);
    final key = WeekKey(familyId, monday);
    final week = ref.watch(weekEventsProvider(key));
    final days = [for (var i = 0; i < 7; i++) monday.add(Duration(days: i))];

    return Scaffold(
      appBar: AppBar(
        title: Text(DateFormat.yMMMM(locale).format(_selected)),
        actions: [
          IconButton(icon: const Icon(Icons.chevron_left), onPressed: () => setState(() => _selected = _selected.subtract(const Duration(days: 7)))),
          TextButton(onPressed: () => setState(() => _selected = DateUtils.dateOnly(DateTime.now())), child: Text(l10n.calendarToday)),
          IconButton(icon: const Icon(Icons.chevron_right), onPressed: () => setState(() => _selected = _selected.add(const Duration(days: 7)))),
          IconButton(
            key: const Key('calendar.shareDay'),
            tooltip: l10n.calendarShareDay,
            icon: const Icon(Icons.volunteer_activism_outlined),
            onPressed: () {
              final day = _selected;
              showHelperShareSheet(context, ref,
                  subject: DateFormat.MMMMEEEEd(locale).format(day), create: () => ref.read(sharingRepositoryProvider).shareDay(familyId, day));
            },
          ),
        ],
      ),
      floatingActionButton: Padding(
        padding: const EdgeInsets.only(bottom: 88),
        child: FloatingActionButton.extended(
          key: const Key('calendar.add'),
          onPressed: () => _addEvent(familyId, key),
          icon: const Icon(Icons.add),
          label: Text(l10n.calendarAdd),
        ),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: HallySpace.sm),
            child: Row(
              children: [
                for (final d in days)
                  Expanded(
                    child: _DayChip(
                      day: d,
                      selected: DateUtils.isSameDay(d, _selected),
                      hasEvents: week.valueOrNull?.any((e) => DateUtils.isSameDay(e.startsAt, d)) ?? false,
                      locale: locale,
                      onTap: () => setState(() => _selected = d),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(height: HallySpace.sm),
          Expanded(
            child: week.when(
              loading: () => const HallyLoading(),
              error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(weekEventsProvider(key))),
              data: (events) {
                final today = events.where((e) => DateUtils.isSameDay(e.startsAt, _selected)).toList();
                if (today.isEmpty) {
                  return HallyEmptyState(icon: Icons.event_available_outlined, title: l10n.calendarEmpty, body: l10n.calendarEmptyBody);
                }
                return ListView(
                  padding: const EdgeInsets.fromLTRB(HallySpace.md, 0, HallySpace.md, 120),
                  children: [
                    for (final e in today)
                      Card(
                        child: ListTile(
                          leading: const HallyIconBadge(icon: Icons.event_rounded, color: HallyColors.apricot),
                          title: Text(e.title, style: theme.textTheme.titleSmall),
                          subtitle: Text(e.allDay ? l10n.reviewAllDay : TimeOfDay.fromDateTime(e.startsAt).format(context)),
                          trailing: IconButton(
                            key: Key('event.${e.id}.remind'),
                            icon: const Icon(Icons.notification_add_outlined),
                            tooltip: l10n.reminderTitle,
                            onPressed: () => showReminderSheet(context, ref,
                                familyId: familyId, target: 'Event', targetId: e.id, title: e.title, at: e.startsAt, allDay: e.allDay),
                          ),
                        ),
                      ),
                  ],
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _addEvent(String familyId, WeekKey key) async {
    final l10n = AppLocalizations.of(context);
    final messenger = ScaffoldMessenger.of(context);
    final result = await showDialog<(String, DateTime, TimeOfDay?)>(
      context: context,
      builder: (_) => _EventDialog(initialDate: _selected),
    );
    if (result == null) return;
    final (title, date, time) = result;
    final startsAt = DateTime(date.year, date.month, date.day, time?.hour ?? 0, time?.minute ?? 0);
    try {
      final saved = await ref.read(planningRepositoryProvider).createEvent(familyId, title: title, startsAt: startsAt, allDay: time == null);
      ref.invalidate(weekEventsProvider(WeekKey(familyId, mondayOf(startsAt))));
      if (saved.conflicts.isNotEmpty) {
        messenger.showSnackBar(SnackBar(content: Text(l10n.eventConflict(saved.conflicts.first.title))));
      }
      if (mounted) setState(() => _selected = DateUtils.dateOnly(startsAt));
    } catch (_) {
      messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
    }
  }
}

class _DayChip extends StatelessWidget {
  const _DayChip({required this.day, required this.selected, required this.hasEvents, required this.locale, required this.onTap});

  final DateTime day;
  final bool selected;
  final bool hasEvents;
  final String locale;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final fg = selected ? Colors.white : theme.colorScheme.onSurface;
    return InkWell(
      borderRadius: BorderRadius.circular(HallyRadius.md),
      onTap: onTap,
      child: AnimatedContainer(
        duration: HallyMotion.fast,
        margin: const EdgeInsets.all(2),
        padding: const EdgeInsets.symmetric(vertical: HallySpace.sm),
        decoration: BoxDecoration(
          gradient: selected ? HallyGradients.lavender : null,
          borderRadius: BorderRadius.circular(HallyRadius.md),
        ),
        child: Column(
          children: [
            Text(DateFormat.E(locale).format(day), style: theme.textTheme.labelSmall?.copyWith(color: fg)),
            const SizedBox(height: 2),
            Text('${day.day}', style: theme.textTheme.titleMedium?.copyWith(color: fg)),
            const SizedBox(height: 2),
            Container(
              width: 5,
              height: 5,
              decoration: BoxDecoration(shape: BoxShape.circle, color: hasEvents ? (selected ? Colors.white : HallyColors.apricot) : Colors.transparent),
            ),
          ],
        ),
      ),
    );
  }
}

class _EventDialog extends StatefulWidget {
  const _EventDialog({required this.initialDate});

  final DateTime initialDate;

  @override
  State<_EventDialog> createState() => _EventDialogState();
}

class _EventDialogState extends State<_EventDialog> {
  final _title = TextEditingController();
  late DateTime _date = widget.initialDate;
  TimeOfDay? _time;

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
      title: Text(l10n.calendarAdd),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(key: const Key('event.title'), controller: _title, autofocus: true, maxLength: 200, decoration: InputDecoration(labelText: l10n.eventTitle)),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.event),
            title: Text(DateFormat.yMMMEd(locale).format(_date)),
            onTap: () async {
              final picked = await showDatePicker(
                context: context,
                initialDate: _date,
                firstDate: DateTime.now().subtract(const Duration(days: 365)),
                lastDate: DateTime.now().add(const Duration(days: 730)),
              );
              if (picked != null) setState(() => _date = picked);
            },
          ),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.schedule),
            title: Text(_time?.format(context) ?? l10n.reviewAllDay),
            onTap: () async {
              final picked = await showTimePicker(context: context, initialTime: _time ?? const TimeOfDay(hour: 9, minute: 0));
              if (picked != null) setState(() => _time = picked);
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
            Navigator.pop(context, (title, _date, _time));
          },
          child: Text(l10n.eventSave),
        ),
      ],
    );
  }
}
