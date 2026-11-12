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

abstract interface class PlanningRepository {
  Future<TodayData> today(String familyId);
  Future<void> completeTask(String familyId, String taskId);
}

class ApiPlanningRepository implements PlanningRepository {
  ApiPlanningRepository(this._api);

  final ApiClient _api;

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
  Future<void> completeTask(String familyId, String taskId) async {
    try {
      await _api.dio.post<dynamic>('/api/v1/families/$familyId/tasks/$taskId/complete');
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }
}

final planningRepositoryProvider = Provider<PlanningRepository>((ref) => ApiPlanningRepository(ref.watch(apiClientProvider)));

final todayProvider = FutureProvider.autoDispose.family<TodayData, String>((ref, familyId) => ref.watch(planningRepositoryProvider).today(familyId));
