import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/design/tokens.dart';
import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_sheet.dart';

/// Tabs: Today · Capture (raised centre action, opens the capture sheet) · Family.
class HomeShell extends ConsumerWidget {
  const HomeShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    void go(int branch) {
      HapticFeedback.selectionClick();
      shell.goBranch(branch, initialLocation: branch == shell.currentIndex);
    }

    return Scaffold(
      extendBody: true,
      body: shell,
      bottomNavigationBar: HallyNavBar(
        currentIndex: shell.currentIndex,
        items: [
          HallyNavItem(icon: Icons.wb_sunny_outlined, activeIcon: Icons.wb_sunny_rounded, label: l10n.tabToday),
          HallyNavItem(icon: Icons.people_alt_outlined, activeIcon: Icons.people_alt_rounded, label: l10n.tabFamily),
        ],
        centerLabel: l10n.tabCapture,
        onTap: go,
        onCenterTap: () {
          HapticFeedback.mediumImpact();
          showCaptureSheet(context, ref);
        },
      ),
    );
  }
}

class HallyNavItem {
  const HallyNavItem({required this.icon, required this.activeIcon, required this.label});

  final IconData icon;
  final IconData activeIcon;
  final String label;
}

/// Floating rounded bar with two tabs and a raised gradient capture button between them.
class HallyNavBar extends StatelessWidget {
  const HallyNavBar({
    super.key,
    required this.currentIndex,
    required this.items,
    required this.centerLabel,
    required this.onTap,
    required this.onCenterTap,
  });

  final int currentIndex;
  final List<HallyNavItem> items;
  final String centerLabel;
  final ValueChanged<int> onTap;
  final VoidCallback onCenterTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final dark = theme.brightness == Brightness.dark;
    return SafeArea(
      minimum: const EdgeInsets.fromLTRB(HallySpace.md, 0, HallySpace.md, HallySpace.sm),
      child: SizedBox(
        height: 84,
        child: Stack(
          clipBehavior: Clip.none,
          alignment: Alignment.bottomCenter,
          children: [
            Container(
              height: 68,
              decoration: BoxDecoration(
                color: theme.colorScheme.surface.withValues(alpha: dark ? 0.96 : 0.98),
                borderRadius: BorderRadius.circular(HallyRadius.lg + 4),
                border: Border.all(color: theme.colorScheme.outlineVariant),
                boxShadow: [
                  BoxShadow(color: const Color(0xFF2B2160).withValues(alpha: dark ? 0.4 : 0.12), blurRadius: 30, offset: const Offset(0, 12)),
                ],
              ),
              child: Row(
                children: [
                  Expanded(child: _Tab(item: items[0], selected: currentIndex == 0, onTap: () => onTap(0))),
                  const SizedBox(width: 88),
                  Expanded(child: _Tab(item: items[1], selected: currentIndex == 1, onTap: () => onTap(1))),
                ],
              ),
            ),
            Positioned(
              top: 0,
              child: Semantics(
                button: true,
                label: centerLabel,
                child: GestureDetector(
                  key: const Key('tab.capture'),
                  onTap: onCenterTap,
                  child: Column(
                    children: [
                      Container(
                        width: 62,
                        height: 62,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          gradient: HallyGradients.magic,
                          border: Border.all(color: theme.colorScheme.surface, width: 4),
                          boxShadow: HallyShadows.glow(HallyColors.rose),
                        ),
                        child: const Icon(Icons.add_rounded, color: Colors.white, size: 32),
                      ),
                      const SizedBox(height: 2),
                      Text(centerLabel, style: theme.textTheme.labelSmall?.copyWith(fontWeight: FontWeight.w700)),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Tab extends StatelessWidget {
  const _Tab({required this.item, required this.selected, required this.onTap});

  final HallyNavItem item;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = selected ? theme.colorScheme.primary : theme.colorScheme.onSurfaceVariant;
    return Semantics(
      selected: selected,
      button: true,
      child: InkWell(
        borderRadius: BorderRadius.circular(HallyRadius.lg),
        onTap: onTap,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            AnimatedContainer(
              duration: HallyMotion.medium,
              curve: HallyMotion.curve,
              padding: EdgeInsets.symmetric(horizontal: selected ? 18 : 10, vertical: 4),
              decoration: BoxDecoration(
                color: selected ? HallyColors.softOf(HallyColors.lavender, theme.brightness) : Colors.transparent,
                borderRadius: BorderRadius.circular(HallyRadius.pill),
              ),
              child: Icon(selected ? item.activeIcon : item.icon, color: color, size: 24),
            ),
            const SizedBox(height: 3),
            Text(
              item.label,
              style: theme.textTheme.labelSmall?.copyWith(color: color, fontWeight: selected ? FontWeight.w700 : FontWeight.w500),
            ),
          ],
        ),
      ),
    );
  }
}
