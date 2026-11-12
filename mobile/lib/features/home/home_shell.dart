import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_sheet.dart';

/// Tabs: Today · Capture (centre action, opens the capture sheet) · Family.
class HomeShell extends ConsumerWidget {
  const HomeShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    // Branch index 0 → tab 0, branch 1 → tab 2; tab 1 is the capture action.
    final selected = shell.currentIndex == 0 ? 0 : 2;
    return Scaffold(
      body: shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: selected,
        onDestinationSelected: (i) {
          if (i == 1) {
            showCaptureSheet(context, ref);
            return;
          }
          final branch = i == 0 ? 0 : 1;
          shell.goBranch(branch, initialLocation: branch == shell.currentIndex);
        },
        destinations: [
          NavigationDestination(icon: const Icon(Icons.wb_sunny_outlined), label: l10n.tabToday),
          NavigationDestination(
            key: const Key('tab.capture'),
            icon: const Icon(Icons.add_circle, size: 36),
            label: l10n.tabCapture,
          ),
          NavigationDestination(icon: const Icon(Icons.family_restroom_outlined), label: l10n.tabFamily),
        ],
      ),
    );
  }
}
