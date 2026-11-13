import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/api/api_client.dart';
import '../../core/design/tokens.dart';
import '../../l10n/gen/app_localizations.dart';
import 'notifications_repository.dart';

/// Bottom sheet to remind about a task or event: presets around its time, or a custom date and time.
Future<void> showReminderSheet(
  BuildContext context,
  WidgetRef ref, {
  required String familyId,
  required String target,
  required String targetId,
  required String title,
  DateTime? at,
  bool allDay = false,
}) async {
  final l10n = AppLocalizations.of(context);
  final messenger = ScaffoldMessenger.of(context);
  final router = GoRouter.maybeOf(context);
  final picked = await showModalBottomSheet<(DateTime, bool)>(
    context: context,
    showDragHandle: true,
    isScrollControlled: true,
    builder: (_) => _ReminderSheet(title: title, at: at, allDay: allDay),
  );
  if (picked == null) return;
  try {
    await ref.read(notificationsRepositoryProvider).createReminder(familyId, target: target, targetId: targetId, fireAt: picked.$1, forEveryone: picked.$2);
    messenger.showSnackBar(SnackBar(content: Text(l10n.reminderSet)));
  } on ApiException catch (e) {
    if (e.needsPlus && router != null) {
      // More than one reminder per item is a Family Plus feature.
      router.push('/paywall?trigger=${e.trigger ?? 'advanced_reminder'}');
    } else {
      messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
    }
  } catch (_) {
    messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
  }
}

class _ReminderSheet extends StatefulWidget {
  const _ReminderSheet({required this.title, required this.at, required this.allDay});

  final String title;
  final DateTime? at;
  final bool allDay;

  @override
  State<_ReminderSheet> createState() => _ReminderSheetState();
}

class _ReminderSheetState extends State<_ReminderSheet> {
  bool _everyone = false;

  String _label(AppLocalizations l10n, ReminderPreset p) => switch (p) {
        ReminderPreset.fifteenBefore => l10n.reminder15Before,
        ReminderPreset.hourBefore => l10n.reminderHourBefore,
        ReminderPreset.morningOf => l10n.reminderMorningOf,
        ReminderPreset.eveningBefore => l10n.reminderEveningBefore,
      };

  Future<void> _custom() async {
    final now = DateTime.now();
    final date = await showDatePicker(context: context, initialDate: widget.at ?? now, firstDate: now, lastDate: now.add(const Duration(days: 400)));
    if (date == null || !mounted) return;
    final time = await showTimePicker(context: context, initialTime: TimeOfDay.fromDateTime(now.add(const Duration(hours: 1))));
    if (time == null || !mounted) return;
    final at = DateTime(date.year, date.month, date.day, time.hour, time.minute);
    if (at.isAfter(DateTime.now())) Navigator.pop(context, (at, _everyone));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final locale = Localizations.localeOf(context).toLanguageTag();
    final presets = reminderPresets(widget.at, widget.allDay, DateTime.now());
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(HallySpace.md, 0, HallySpace.md, HallySpace.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(l10n.reminderTitle, style: theme.textTheme.titleLarge),
            Text(widget.title, style: theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
            SwitchListTile(
              key: const Key('reminder.everyone'),
              contentPadding: EdgeInsets.zero,
              value: _everyone,
              title: Text(l10n.reminderEveryone),
              onChanged: (v) => setState(() => _everyone = v),
            ),
            const SizedBox(height: HallySpace.sm),
            for (final (preset, at) in presets)
              ListTile(
                key: Key('reminder.${preset.name}'),
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.alarm_rounded),
                title: Text(_label(l10n, preset)),
                subtitle: Text(DateFormat.MMMEd(locale).add_Hm().format(at)),
                onTap: () => Navigator.pop(context, (at, _everyone)),
              ),
            ListTile(
              key: const Key('reminder.custom'),
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.edit_calendar_rounded),
              title: Text(l10n.reminderCustom),
              onTap: _custom,
            ),
          ],
        ),
      ),
    );
  }
}
