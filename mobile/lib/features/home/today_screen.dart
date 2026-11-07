import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// Today. Tasks, events and pending proposals arrive in Sprint 2/3; the empty state
/// already points at the capture flow.
class TodayScreen extends ConsumerWidget {
  const TodayScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final me = ref.watch(meProvider).valueOrNull;
    return Scaffold(
      appBar: AppBar(title: Text(me?.primaryFamily?.name ?? l10n.tabToday)),
      body: HallyEmptyState(
        icon: Icons.photo_camera_outlined,
        title: l10n.todayEmptyTitle,
        body: l10n.todayEmptyBody,
      ),
    );
  }
}
