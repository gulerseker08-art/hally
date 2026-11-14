import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hally/core/auth/auth_repository.dart';
import 'package:hally/core/design/theme.dart';
import 'package:hally/features/capture/capture_models.dart';
import 'package:hally/features/demo/demo_repository.dart';
import 'package:hally/features/demo/demo_scan_screen.dart';
import 'package:hally/features/planning/calendar_screen.dart';
import 'package:hally/features/planning/planning_repository.dart';
import 'package:hally/features/planning/tasks_screen.dart';
import 'package:hally/features/share/share_intake.dart';
import 'package:hally/l10n/gen/app_localizations.dart';

const _me = Me(id: 'u1', displayName: 'Ayşe', locale: 'tr', families: [FamilyRef(id: 'f1', name: 'Yılmaz')]);

class _FakePlanning implements PlanningRepository {
  final open = <TaskItem>[const TaskItem(id: 't1', title: 'Beslenme çantası', allDay: false, state: 'Open')];
  final done = <TaskItem>[];
  final saved = <EventItem>[];
  final completed = <String>[];

  @override
  Future<void> completeTask(String familyId, String taskId) async {
    completed.add(taskId);
    final t = open.firstWhere((t) => t.id == taskId);
    open.remove(t);
    done.add(TaskItem(id: t.id, title: t.title, allDay: t.allDay, state: 'Done'));
  }

  @override
  Future<void> reopenTask(String familyId, String taskId) async {
    final t = done.firstWhere((t) => t.id == taskId);
    done.remove(t);
    open.add(TaskItem(id: t.id, title: t.title, allDay: t.allDay, state: 'Open'));
  }

  @override
  Future<List<TaskItem>> tasks(String familyId, {required String state}) async => List.of(state == 'Done' ? done : open);

  @override
  Future<TaskItem> createTask(String familyId, {required String title, DateTime? dueDate}) async {
    final t = TaskItem(id: 'n${open.length}', title: title, allDay: dueDate != null, dueAt: dueDate, state: 'Open');
    open.add(t);
    return t;
  }

  @override
  Future<List<EventItem>> events(String familyId, DateTime from, DateTime to) async =>
      saved.where((e) => !e.startsAt.isBefore(from) && e.startsAt.isBefore(to)).toList();

  @override
  Future<SavedEvent> createEvent(String familyId, {required String title, required DateTime startsAt, required bool allDay}) async {
    final e = EventItem(id: 'e${saved.length}', title: title, startsAt: startsAt, allDay: allDay);
    final conflicts = saved.where((x) => x.startsAt == startsAt).toList();
    saved.add(e);
    return SavedEvent(event: e, conflicts: conflicts);
  }

  @override
  Future<TodayData> today(String familyId) async => const TodayData(overdue: [], dueToday: [], events: [], waiting: []);
}

class _FakeDemo implements DemoRepository {
  @override
  Future<List<DemoItem>> scanSample(String sampleId, String locale) async => [
        const DemoItem(isEvent: false, payload: ProposalPayload(title: 'Kırmızı tişört giydir', date: '2026-11-13')),
        const DemoItem(isEvent: true, payload: ProposalPayload(title: 'Veli toplantısı', date: '2026-11-18', time: '18:00')),
      ];

  @override
  Future<List<DemoItem>> scanImage(List<int> bytes, String fileName, String contentType, String locale) => throw UnimplementedError();
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
  testWidgets('tasks: completing an open task moves it to done', (tester) async {
    final repo = _FakePlanning();
    await tester.pumpWidget(_app(const TasksScreen(), [planningRepositoryProvider.overrideWithValue(repo)]));
    await tester.pumpAndSettle();

    expect(find.text('Beslenme çantası'), findsOneWidget);
    await tester.tap(find.byKey(const Key('task.t1')));
    await tester.pumpAndSettle();
    expect(repo.completed, ['t1']);
    expect(find.text('Açık görev yok'), findsOneWidget);

    await tester.tap(find.text('Tamamlanan'));
    await tester.pumpAndSettle();
    expect(find.text('Beslenme çantası'), findsOneWidget);
  });

  testWidgets('tasks: undo on the snackbar reopens a completed task', (tester) async {
    final repo = _FakePlanning();
    await tester.pumpWidget(_app(const TasksScreen(), [planningRepositoryProvider.overrideWithValue(repo)]));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('task.t1')));
    await tester.pumpAndSettle();
    expect(find.text('Açık görev yok'), findsOneWidget);

    await tester.tap(find.byKey(const Key('task.undo')));
    await tester.pumpAndSettle();
    expect(repo.open.map((t) => t.id), ['t1']);
    expect(find.text('Beslenme çantası'), findsOneWidget);
  });

  testWidgets('tasks: quick add creates an open task', (tester) async {
    final repo = _FakePlanning();
    await tester.pumpWidget(_app(const TasksScreen(), [planningRepositoryProvider.overrideWithValue(repo)]));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('tasks.add')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('task.title')), 'İzin kağıdını imzala');
    await tester.tap(find.text('Kaydet'));
    await tester.pumpAndSettle();
    expect(find.text('İzin kağıdını imzala'), findsOneWidget);
  });

  testWidgets('calendar: added event shows on the selected day and conflicts are flagged', (tester) async {
    final repo = _FakePlanning();
    await tester.pumpWidget(_app(const CalendarScreen(), [planningRepositoryProvider.overrideWithValue(repo)]));
    await tester.pumpAndSettle();
    expect(find.text('Bu güne planlanmış bir şey yok'), findsOneWidget);

    for (final _ in [1, 2]) {
      await tester.tap(find.byKey(const Key('calendar.add')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('event.title')), 'Yüzme');
      await tester.tap(find.text('Kaydet'));
      await tester.pumpAndSettle();
    }
    expect(find.text('Yüzme'), findsNWidgets(2));
    expect(find.text('Bu saatte başka bir etkinlik var: Yüzme'), findsOneWidget);
  });

  testWidgets('demo: scanning the sample shows what Hally found', (tester) async {
    await tester.pumpWidget(_app(const DemoScanScreen(), [demoRepositoryProvider.overrideWithValue(_FakeDemo())]));
    await tester.tap(find.byKey(const Key('demo.scan')));
    await tester.pumpAndSettle();
    expect(find.text('2 şey buldum'), findsOneWidget);
    expect(find.text('Veli toplantısı'), findsOneWidget);
    expect(find.byKey(const Key('demo.save')), findsOneWidget);
  });

  test('share mapping keeps text and images, drops other files', () {
    expect(mapShared('text', '  Yarın gezi var  ', null), isA<SharedText>().having((t) => t.text, 'text', 'Yarın gezi var'));
    expect(mapShared('url', '', null), isNull);
    expect(mapShared('image', '/tmp/a.PNG', null), isA<SharedImage>().having((i) => i.mimeType, 'mime', 'image/png'));
    expect(mapShared('video', '/tmp/a.mp4', 'video/mp4'), isNull);
  });
}
