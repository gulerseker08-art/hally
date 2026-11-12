import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'family_repository.dart';

class FamilyScreen extends ConsumerWidget {
  const FamilyScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());

    final family = ref.watch(familyProvider(familyId));
    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.tabFamily),
        actions: [
          IconButton(
            tooltip: l10n.signOut,
            icon: const Icon(Icons.logout_rounded),
            onPressed: () async {
              await ref.read(authRepositoryProvider).signOut();
              ref.invalidate(meProvider);
            },
          ),
        ],
      ),
      body: family.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(familyProvider(familyId))),
        data: (f) => RefreshIndicator(
          onRefresh: () => ref.refresh(familyProvider(familyId).future),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.sm, HallySpace.md, 120),
            children: [
              HallyEntrance(child: _FamilyHero(family: f)),
              HallySectionHeader(title: l10n.familyMembers, count: f.members.length),
              for (final (i, m) in f.members.indexed)
                HallyEntrance(
                  index: i + 1,
                  child: Card(
                    child: Padding(
                      padding: const EdgeInsets.all(HallySpace.md - 2),
                      child: Row(
                        children: [
                          HallyAvatar(name: m.displayName, size: 46),
                          const SizedBox(width: HallySpace.md),
                          Expanded(child: Text(m.displayName, style: Theme.of(context).textTheme.titleMedium)),
                          HallyPill(
                            label: m.role == 'Owner' ? l10n.roleOwner : l10n.roleAdult,
                            icon: m.role == 'Owner' ? Icons.star_rounded : null,
                            color: m.role == 'Owner' ? HallyColors.apricot : HallyColors.lavender,
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              const SizedBox(height: HallySpace.sm),
              HallyPressable(
                onTap: () => context.push('/family/${f.id}/invite'),
                child: Container(
                  padding: const EdgeInsets.all(HallySpace.md),
                  decoration: BoxDecoration(
                    color: HallyColors.softOf(HallyColors.mint, Theme.of(context).brightness),
                    borderRadius: BorderRadius.circular(HallyRadius.lg),
                    border: Border.all(color: HallyColors.mint.withValues(alpha: 0.4), width: 1.5),
                  ),
                  child: Row(
                    children: [
                      const HallyIconBadge(icon: Icons.person_add_alt_1_rounded, color: HallyColors.mint, gradient: HallyGradients.mint, size: 46),
                      const SizedBox(width: HallySpace.md),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(l10n.familyInviteMore, style: Theme.of(context).textTheme.titleMedium),
                            const SizedBox(height: 2),
                            Text(l10n.familyInviteBody, style: Theme.of(context).textTheme.bodySmall),
                          ],
                        ),
                      ),
                      const Icon(Icons.chevron_right_rounded, color: HallyColors.mintDeep),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Gradient card with the family name and overlapping member avatars.
class _FamilyHero extends StatelessWidget {
  const _FamilyHero({required this.family});

  final FamilyDetails family;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final shown = family.members.take(5).toList();
    return Container(
      decoration: BoxDecoration(
        gradient: HallyGradients.hero,
        borderRadius: BorderRadius.circular(HallyRadius.lg),
        boxShadow: HallyShadows.glow(HallyColors.lavender),
      ),
      clipBehavior: Clip.antiAlias,
      child: Stack(
        children: [
          const Positioned(right: -40, top: -50, child: HallyGlow(color: Color(0xFFF0A36A), size: 180)),
          Padding(
            padding: const EdgeInsets.all(HallySpace.lg),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  height: 48,
                  child: Stack(
                    children: [
                      for (final (i, m) in shown.indexed)
                        Positioned(left: i * 32.0, child: HallyAvatar(name: m.displayName, size: 48, ring: true)),
                    ],
                  ),
                ),
                const SizedBox(height: HallySpace.md),
                Text(family.name, style: theme.textTheme.headlineMedium?.copyWith(color: Colors.white)),
                const SizedBox(height: HallySpace.xs),
                Row(
                  children: [
                    const Icon(Icons.favorite_rounded, size: 16, color: Color(0xFFFFC9B0)),
                    const SizedBox(width: 6),
                    Text(
                      l10n.familyHeroBody(family.members.length),
                      style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
