import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../capture/capture_models.dart';

class TaskItem {
  const TaskItem({required this.id, required this.title, this.dueAt, required this.allDay, this.assigneeUserId, required this.state});

  factory TaskItem.fromJson(Map<String, dynamic> j) => TaskItem(
        id: j['id'] as String,
        title: j['title'] as String,
        dueAt: j['dueAt'] == null ? null : DateTime.parse(j['dueAt'] as String).toLocal(),
        allDay: j['allDay'] as bool? ?? false,
        assigneeUserId: j['assigneeUserId'] as String?,
        state: j['state'] as String,
      );

  final String id;
  final String title;
  final DateTime? dueAt;
  final bool allDay;
  final String? assigneeUserId;
  final String state;
}

class EventItem {
  const EventItem({required this.id, required this.title, required this.startsAt, required this.allDay});

  factory EventItem.fromJson(Map<String, dynamic> j) => EventItem(
        id: j['id'] as String,
        title: j['title'] as String,
        startsAt: DateTime.parse(j['startsAt'] as String).toLocal(),
        allDay: j['allDay'] as bool? ?? false,
      );

  final String id;
  final String title;
  final DateTime startsAt;
  final bool allDay;
}

class TodayData {
  const TodayData({required this.overdue, required this.dueToday, required this.events, required this.waiting});

  factory TodayData.fromJson(Map<String, dynamic> j) => TodayData(
        overdue: [for (final t in j['overdue'] as List) TaskItem.fromJson(t as Map<String, dynamic>)],
        dueToday: [for (final t in j['dueToday'] as List) TaskItem.fromJson(t as Map<String, dynamic>)],
        events: [for (final e in j['events'] as List) EventItem.fromJson(e as Map<String, dynamic>)],
        waiting: [for (final p in j['waitingForYou'] as List) Proposal.fromJson(p as Map<String, dynamic>)],
      );

  final List<TaskItem> overdue;
  final List<TaskItem> dueToday;
  final List<EventItem> events;
  final List<Proposal> waiting;

  bool get isEmpty => overdue.isEmpty && dueToday.isEmpty && events.isEmpty && waiting.isEmpty;
}

class SavedEvent {
  const SavedEvent({required this.event, required this.conflicts});

  final EventItem event;
  final List<EventItem> conflicts;
}

abstract interface class PlanningRepository {
  Future<TodayData> today(String familyId);
  Future<void> completeTask(String familyId, String taskId);
  Future<void> reopenTask(String familyId, String taskId);
  Future<List<TaskItem>> tasks(String familyId, {required String state});
  Future<TaskItem> createTask(String familyId, {required String title, DateTime? dueDate});
  Future<List<EventItem>> events(String familyId, DateTime from, DateTime to);
  Future<SavedEvent> createEvent(String familyId, {required String title, required DateTime startsAt, required bool allDay});
}

class ApiPlanningRepository implements PlanningRepository {
  ApiPlanningRepository(this._api);

  final ApiClient _api;

  Future<T> _guard<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  @override
  Future<TodayData> today(String familyId) async {
    try {
      final res = await _api.dio.get<Map<String, dynamic>>('/api/v1/families/$familyId/today');
      return TodayData.fromJson(res.data!);
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  @override
  Future<void> completeTask(String familyId, String taskId) =>
      _guard(() => _api.dio.post<dynamic>('/api/v1/families/$familyId/tasks/$taskId/complete'));

  @override
  Future<void> reopenTask(String familyId, String taskId) =>
      _guard(() => _api.dio.post<dynamic>('/api/v1/families/$familyId/tasks/$taskId/reopen'));

  @override
  Future<List<TaskItem>> tasks(String familyId, {required String state}) => _guard(() async {
        final res = await _api.dio.get<List<dynamic>>('/api/v1/families/$familyId/tasks', queryParameters: {'state': state});
        return [for (final t in res.data!) TaskItem.fromJson(t as Map<String, dynamic>)];
      });

  @override
  Future<TaskItem> createTask(String familyId, {required String title, DateTime? dueDate}) => _guard(() async {
        final res = await _api.dio.post<Map<String, dynamic>>('/api/v1/families/$familyId/tasks', data: {
          'title': title,
          'dueAt': dueDate == null ? null : DateTime(dueDate.year, dueDate.month, dueDate.day).toUtc().toIso8601String(),
          'allDay': dueDate != null,
        });
        return TaskItem.fromJson(res.data!);
      });

  @override
  Future<List<EventItem>> events(String familyId, DateTime from, DateTime to) => _guard(() async {
        final res = await _api.dio.get<List<dynamic>>('/api/v1/families/$familyId/events', queryParameters: {
          'from': from.toUtc().toIso8601String(),
          'to': to.toUtc().toIso8601String(),
        });
        return [for (final e in res.data!) EventItem.fromJson(e as Map<String, dynamic>)];
      });

  @override
  Future<SavedEvent> createEvent(String familyId, {required String title, required DateTime startsAt, required bool allDay}) =>
      _guard(() async {
        final res = await _api.dio.post<Map<String, dynamic>>('/api/v1/families/$familyId/events', data: {
          'title': title,
          'startsAt': startsAt.toUtc().toIso8601String(),
          'allDay': allDay,
        });
        final d = res.data!;
        return SavedEvent(
          event: EventItem.fromJson(d['event'] as Map<String, dynamic>),
          conflicts: [for (final c in d['conflicts'] as List) EventItem.fromJson(c as Map<String, dynamic>)],
        );
      });
}

final planningRepositoryProvider = Provider<PlanningRepository>((ref) => ApiPlanningRepository(ref.watch(apiClientProvider)));

final todayProvider = FutureProvider.autoDispose.family<TodayData, String>((ref, familyId) => ref.watch(planningRepositoryProvider).today(familyId));

/// Monday-based week window for the calendar, in device-local time.
class WeekKey {
  const WeekKey(this.familyId, this.monday);

  final String familyId;
  final DateTime monday;

  @override
  bool operator ==(Object other) => other is WeekKey && other.familyId == familyId && other.monday == monday;

  @override
  int get hashCode => Object.hash(familyId, monday);
}

DateTime mondayOf(DateTime day) {
  final d = DateTime(day.year, day.month, day.day);
  return d.subtract(Duration(days: d.weekday - DateTime.monday));
}

final weekEventsProvider = FutureProvider.autoDispose.family<List<EventItem>, WeekKey>(
    (ref, key) => ref.watch(planningRepositoryProvider).events(key.familyId, key.monday, key.monday.add(const Duration(days: 7))));

final tasksProvider = FutureProvider.autoDispose.family<List<TaskItem>, (String, String)>(
    (ref, args) => ref.watch(planningRepositoryProvider).tasks(args.$1, state: args.$2));
