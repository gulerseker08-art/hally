import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';

class InboxItem {
  const InboxItem({required this.id, required this.kind, required this.title, required this.body, required this.createdAt, required this.read, this.targetKind, this.targetId});

  factory InboxItem.fromJson(Map<String, dynamic> j) => InboxItem(
        id: j['id'] as String,
        kind: j['kind'] as String,
        title: j['title'] as String,
        body: j['body'] as String,
        targetKind: j['targetKind'] as String?,
        targetId: j['targetId'] as String?,
        createdAt: DateTime.parse(j['createdAt'] as String).toLocal(),
        read: j['read'] as bool? ?? false,
      );

  final String id;
  final String kind; // Reminder | EveningPrep
  final String title;
  final String body;
  final String? targetKind;
  final String? targetId;
  final DateTime createdAt;
  final bool read;
}

class Inbox {
  const Inbox({required this.unread, required this.items});

  final int unread;
  final List<InboxItem> items;
}

class NotificationSettings {
  const NotificationSettings({required this.eveningPrepEnabled, required this.eveningPrepHour});

  final bool eveningPrepEnabled;
  final int eveningPrepHour;
}

abstract interface class NotificationsRepository {
  Future<Inbox> inbox();
  Future<void> readAll();
  Future<NotificationSettings> settings();
  Future<NotificationSettings> saveSettings(NotificationSettings settings);

  /// [target] is `Task` or `Event`. [forEveryone] reminds every adult instead of only the caller.
  Future<void> createReminder(String familyId, {required String target, required String targetId, required DateTime fireAt, required bool forEveryone});
}

class ApiNotificationsRepository implements NotificationsRepository {
  ApiNotificationsRepository(this._api);

  final ApiClient _api;

  Future<T> _guard<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  @override
  Future<Inbox> inbox() => _guard(() async {
        final d = (await _api.dio.get<Map<String, dynamic>>('/api/v1/me/notifications')).data!;
        return Inbox(unread: d['unread'] as int, items: [for (final i in d['items'] as List) InboxItem.fromJson(i as Map<String, dynamic>)]);
      });

  @override
  Future<void> readAll() => _guard(() => _api.dio.post<dynamic>('/api/v1/me/notifications/read-all'));

  @override
  Future<NotificationSettings> settings() => _guard(() async {
        final d = (await _api.dio.get<Map<String, dynamic>>('/api/v1/me/notification-settings')).data!;
        return NotificationSettings(eveningPrepEnabled: d['eveningPrepEnabled'] as bool, eveningPrepHour: d['eveningPrepHour'] as int);
      });

  @override
  Future<NotificationSettings> saveSettings(NotificationSettings s) => _guard(() async {
        final d = (await _api.dio.put<Map<String, dynamic>>('/api/v1/me/notification-settings',
                data: {'eveningPrepEnabled': s.eveningPrepEnabled, 'eveningPrepHour': s.eveningPrepHour}))
            .data!;
        return NotificationSettings(eveningPrepEnabled: d['eveningPrepEnabled'] as bool, eveningPrepHour: d['eveningPrepHour'] as int);
      });

  @override
  Future<void> createReminder(String familyId, {required String target, required String targetId, required DateTime fireAt, required bool forEveryone}) =>
      _guard(() => _api.dio.post<dynamic>('/api/v1/families/$familyId/reminders', data: {
            'target': target,
            'targetId': targetId,
            'fireAt': fireAt.toUtc().toIso8601String(),
            'forEveryone': forEveryone,
          }));
}

final notificationsRepositoryProvider = Provider<NotificationsRepository>((ref) => ApiNotificationsRepository(ref.watch(apiClientProvider)));

final inboxProvider = FutureProvider.autoDispose<Inbox>((ref) => ref.watch(notificationsRepositoryProvider).inbox());

final notificationSettingsProvider = FutureProvider.autoDispose<NotificationSettings>((ref) => ref.watch(notificationsRepositoryProvider).settings());

/// Ready-made reminder times for an item due at [at], in device-local time. Past options are dropped.
List<(ReminderPreset, DateTime)> reminderPresets(DateTime? at, bool allDay, DateTime now) {
  final out = <(ReminderPreset, DateTime)>[];
  if (at != null) {
    final day = DateTime(at.year, at.month, at.day);
    if (!allDay) {
      out.add((ReminderPreset.fifteenBefore, at.subtract(const Duration(minutes: 15))));
      out.add((ReminderPreset.hourBefore, at.subtract(const Duration(hours: 1))));
    }
    out.add((ReminderPreset.morningOf, day.add(const Duration(hours: 8))));
    out.add((ReminderPreset.eveningBefore, day.subtract(const Duration(hours: 4)))); // 20:00 the day before
  }
  return out.where((p) => p.$2.isAfter(now) && (at == null || allDay || !p.$2.isAfter(at))).toList();
}

enum ReminderPreset { fifteenBefore, hourBefore, morningOf, eveningBefore }
