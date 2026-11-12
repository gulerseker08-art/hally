import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'notifications_repository.dart';

/// Inbox of reminders and evening summaries, plus the evening summary setting.
class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key});

  @override
  ConsumerState<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  bool _markedRead = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final locale = Localizations.localeOf(context).toLanguageTag();
    final inbox = ref.watch(inboxProvider);

    ref.listen(inboxProvider, (_, next) {
      final unread = next.valueOrNull?.unread ?? 0;
      if (!_markedRead && unread > 0) {
        _markedRead = true;
        ref.read(notificationsRepositoryProvider).readAll().ignore();
      }
    });

    return Scaffold(
      appBar: AppBar(title: Text(l10n.notificationsTitle)),
      body: inbox.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(inboxProvider)),
        data: (data) => RefreshIndicator(
          onRefresh: () => ref.refresh(inboxProvider.future),
          child: ListView(
            padding: const EdgeInsets.all(HallySpace.md),
            children: [
              const _EveningPrepCard(),
              const SizedBox(height: HallySpace.md),
              if (data.items.isEmpty)
                HallyEmptyState(icon: Icons.notifications_none_rounded, title: l10n.notificationsEmpty, body: l10n.notificationsEmptyBody)
              else
                for (final n in data.items)
                  Card(
                    child: ListTile(
                      leading: HallyIconBadge(
                        icon: n.kind == 'EveningPrep' ? Icons.nights_stay_rounded : Icons.alarm_rounded,
                        color: n.kind == 'EveningPrep' ? HallyColors.lavender : HallyColors.apricot,
                      ),
                      title: Text(n.title, style: theme.textTheme.titleSmall?.copyWith(fontWeight: n.read ? FontWeight.w500 : FontWeight.w700)),
                      subtitle: Text('${n.body}\n${DateFormat.MMMd(locale).add_Hm().format(n.createdAt)}'),
                      isThreeLine: true,
                      onTap: switch (n.targetKind) {
                        'Task' => () => context.go('/tasks'),
                        'Event' => () => context.go('/calendar'),
                        _ => () => context.go('/today'),
                      },
                    ),
                  ),
            ],
          ),
        ),
      ),
    );
  }
}

class _EveningPrepCard extends ConsumerWidget {
  const _EveningPrepCard();

  Future<void> _save(WidgetRef ref, NotificationSettings s) async {
    await ref.read(notificationsRepositoryProvider).saveSettings(s);
    ref.invalidate(notificationSettingsProvider);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final settings = ref.watch(notificationSettingsProvider).valueOrNull;
    if (settings == null) return const SizedBox.shrink();
    return Card(
      child: Column(
        children: [
          SwitchListTile(
            key: const Key('prep.enabled'),
            secondary: const HallyIconBadge(icon: Icons.nights_stay_rounded, color: HallyColors.lavender),
            title: Text(l10n.eveningPrepTitle),
            subtitle: Text(l10n.eveningPrepBody),
            value: settings.eveningPrepEnabled,
            onChanged: (v) => _save(ref, NotificationSettings(eveningPrepEnabled: v, eveningPrepHour: settings.eveningPrepHour)),
          ),
          if (settings.eveningPrepEnabled)
            ListTile(
              title: Text(l10n.eveningPrepHour),
              trailing: DropdownButton<int>(
                key: const Key('prep.hour'),
                value: settings.eveningPrepHour,
                underline: const SizedBox.shrink(),
                items: [for (var h = 16; h <= 22; h++) DropdownMenuItem(value: h, child: Text('${h.toString().padLeft(2, '0')}:00'))],
                onChanged: (h) => h == null ? null : _save(ref, NotificationSettings(eveningPrepEnabled: true, eveningPrepHour: h)),
              ),
            ),
        ],
      ),
    );
  }
}

/// Bell with an unread badge, for the Today header.
class NotificationBell extends ConsumerWidget {
  const NotificationBell({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(inboxProvider).valueOrNull?.unread ?? 0;
    return IconButton(
      key: const Key('today.notifications'),
      tooltip: AppLocalizations.of(context).notificationsTitle,
      onPressed: () async {
        await context.push('/notifications');
        ref.invalidate(inboxProvider);
      },
      icon: Badge(
        isLabelVisible: unread > 0,
        label: Text('$unread'),
        backgroundColor: HallyColors.rose,
        child: const Icon(Icons.notifications_none_rounded, color: Colors.white),
      ),
    );
  }
}
