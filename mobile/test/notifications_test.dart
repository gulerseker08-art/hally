import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/notifications/notifications_repository.dart';
import 'package:hally/features/notifications/notifications_screen.dart';
import 'package:hally/features/planning/planning_repository.dart';
import 'package:hally/features/planning/tasks_screen.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

class _FakeNotifications implements NotificationsRepository {
  var settingsValue = const NotificationSettings(eveningPrepEnabled: true, eveningPrepHour: 20);
  var readAllCalls = 0;
  final reminders = <(String, String, DateTime, bool)>[];

  @override
  Future<Inbox> inbox() async => Inbox(unread: 1, items: [
        InboxItem(id: 'n1', kind: 'EveningPrep', title: 'Yarın için hazırlık', body: '1 etkinlik: Yüzme 09:00', createdAt: DateTime(2026, 11, 12, 20), read: false),
      ]);

  @override
  Future<void> readAll() async => readAllCalls++;

  @override
  Future<NotificationSettings> settings() async => settingsValue;

  @override
  Future<NotificationSettings> saveSettings(NotificationSettings s) async => settingsValue = s;

  @override
  Future<void> createReminder(String familyId, {required String target, required String targetId, required DateTime fireAt, required bool forEveryone}) async =>
      reminders.add((target, targetId, fireAt, forEveryone));
}

class _OneTask implements PlanningRepository {
  _OneTask(this.due);
  final DateTime due;

  @override
  Future<List<TaskItem>> tasks(String familyId, {required String state}) async =>
      state == 'Open' ? [TaskItem(id: 't1', title: 'Veli toplantısı', dueAt: due, allDay: false, state: 'Open')] : [];

  @override
  dynamic noSuchMethod(Invocation invocation) => throw UnimplementedError();
}

Widget _app(Widget child, List<Override> overrides) => ProviderScope(
      overrides: [meProvider.overrideWith((ref) async => _me), ...overrides],
      child: MaterialApp(
        theme: buildHallyTheme(Brightness.light),
        locale: const Locale('tr'),
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        supportedLocales: AppLocalizations.supportedLocales,
        home: child,
      ),
    );

void main() {
  test('reminder presets stay between now and the item time', () {
    final now = DateTime(2026, 11, 12, 10);
    final at = DateTime(2026, 11, 13, 18, 30);
    final presets = reminderPresets(at, false, now);
    expect(presets.map((p) => p.$1), [ReminderPreset.fifteenBefore, ReminderPreset.hourBefore, ReminderPreset.morningOf, ReminderPreset.eveningBefore]);
    expect(presets.last.$2, DateTime(2026, 11, 12, 20));

    // Due in 30 minutes: only "15 minutes before" is still possible.
    expect(reminderPresets(DateTime(2026, 11, 12, 10, 30), false, now).map((p) => p.$1), [ReminderPreset.fifteenBefore]);
    // All-day item: the morning of the day is allowed even though it is after midnight.
    expect(reminderPresets(DateTime(2026, 11, 14), true, now).map((p) => p.$1), [ReminderPreset.morningOf, ReminderPreset.eveningBefore]);
    expect(reminderPresets(null, false, now), isEmpty);
  });

  testWidgets('inbox shows entries, marks them read and toggles evening prep', (tester) async {
    final repo = _FakeNotifications();
    await tester.pumpWidget(_app(const NotificationsScreen(), [notificationsRepositoryProvider.overrideWithValue(repo)]));
    await tester.pumpAndSettle();

    expect(find.text('Yarın için hazırlık'), findsOneWidget);
    expect(repo.readAllCalls, 1);

    await tester.tap(find.byKey(const Key('prep.enabled')));
    await tester.pumpAndSettle();
    expect(repo.settingsValue.eveningPrepEnabled, isFalse);
    expect(find.byKey(const Key('prep.hour')), findsNothing);
  });

  testWidgets('a task reminder can be set for every adult', (tester) async {
    final repo = _FakeNotifications();
    final due = DateTime.now().add(const Duration(days: 2));
    await tester.pumpWidget(_app(const TasksScreen(), [
      notificationsRepositoryProvider.overrideWithValue(repo),
      planningRepositoryProvider.overrideWithValue(_OneTask(due)),
    ]));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('task.t1.remind')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('reminder.everyone')));
    await tester.pump();
    await tester.ensureVisible(find.byKey(const Key('reminder.hourBefore')));
    await tester.tap(find.byKey(const Key('reminder.hourBefore')));
    await tester.pumpAndSettle();

    expect(repo.reminders, hasLength(1));
    final (target, id, at, everyone) = repo.reminders.single;
    expect((target, id, everyone), ('Task', 't1', true));
    expect(at, due.subtract(const Duration(hours: 1)));
    expect(find.text('Hatırlatma kuruldu'), findsOneWidget);
  });
}
