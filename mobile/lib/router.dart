import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/auth/auth_repository.dart';
import 'core/design/widgets.dart';
import 'features/auth/sign_in_screen.dart';
import 'features/capture/capture_text_screen.dart';
import 'features/capture/review_screen.dart';
import 'features/demo/demo_scan_screen.dart';
import 'features/family/family_screen.dart';
import 'features/family/family_setup_screen.dart';
import 'features/family/invite_partner_screen.dart';
import 'features/family/join_family_screen.dart';
import 'features/home/home_shell.dart';
import 'features/home/today_screen.dart';
import 'features/notifications/notifications_screen.dart';
import 'features/planning/calendar_screen.dart';
import 'features/planning/tasks_screen.dart';
import 'features/onboarding/trust_screen.dart';
import 'features/onboarding/welcome_screen.dart';

const _publicRoutes = {'/welcome', '/trust', '/demo', '/sign-in', '/join'};

/// Pure redirect rules so they can be unit tested.
String? resolveRedirect({
  required String location,
  required bool loading,
  required Me? me,
  required String? pendingInviteToken,
}) {
  final isInvite = location.startsWith('/i/');
  if (loading) return location == '/' ? null : (isInvite ? null : '/');

  if (me == null) {
    if (isInvite || _publicRoutes.contains(location)) return null;
    return '/welcome';
  }

  if (pendingInviteToken != null && !isInvite) return '/i/$pendingInviteToken';
  if (isInvite) return null;

  if (me.primaryFamily == null) {
    return location == '/family/new' ? null : '/family/new';
  }

  final onboarding = location == '/' || _publicRoutes.contains(location) || location == '/family/new';
  return onboarding ? '/today' : null;
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier(0);
  ref.listen(meProvider, (_, __) => refresh.value++);
  ref.listen(pendingInviteTokenProvider, (_, __) => refresh.value++);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/',
    refreshListenable: refresh,
    redirect: (context, state) {
      final me = ref.read(meProvider);
      return resolveRedirect(
        location: state.matchedLocation,
        loading: me.isLoading && !me.hasValue,
        me: me.valueOrNull,
        pendingInviteToken: ref.read(pendingInviteTokenProvider),
      );
    },
    routes: [
      GoRoute(path: '/', builder: (_, __) => const Scaffold(body: HallyLoading())),
      GoRoute(path: '/welcome', builder: (_, __) => const WelcomeScreen()),
      GoRoute(path: '/trust', builder: (_, __) => const TrustScreen()),
      GoRoute(path: '/demo', builder: (_, __) => const DemoScanScreen()),
      GoRoute(path: '/sign-in', builder: (_, __) => const SignInScreen()),
      GoRoute(path: '/join', builder: (_, __) => const EnterInviteScreen()),
      GoRoute(path: '/i/:token', builder: (_, s) => JoinFamilyScreen(token: s.pathParameters['token']!)),
      GoRoute(path: '/family/new', builder: (_, __) => const FamilySetupScreen()),
      GoRoute(path: '/family/:id/invite', builder: (_, s) => InvitePartnerScreen(familyId: s.pathParameters['id']!)),
      GoRoute(path: '/capture/text', builder: (_, __) => const CaptureTextScreen()),
      GoRoute(path: '/capture/review', builder: (_, __) => const ReviewScreen()),
      GoRoute(path: '/notifications', builder: (_, __) => const NotificationsScreen()),
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => HomeShell(shell: shell),
        branches: [
          StatefulShellBranch(routes: [GoRoute(path: '/today', builder: (_, __) => const TodayScreen())]),
          StatefulShellBranch(routes: [GoRoute(path: '/calendar', builder: (_, __) => const CalendarScreen())]),
          StatefulShellBranch(routes: [GoRoute(path: '/tasks', builder: (_, __) => const TasksScreen())]),
          StatefulShellBranch(routes: [GoRoute(path: '/family', builder: (_, __) => const FamilyScreen())]),
        ],
      ),
    ],
  );
});
