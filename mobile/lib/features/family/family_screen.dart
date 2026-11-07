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
        title: Text(family.valueOrNull?.name ?? l10n.tabFamily),
        actions: [
          IconButton(
            tooltip: l10n.signOut,
            icon: const Icon(Icons.logout),
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
            padding: const EdgeInsets.all(HallySpace.md),
            children: [
              Text(l10n.familyMembers, style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: HallySpace.sm),
              for (final m in f.members)
                Card(
                  child: ListTile(
                    leading: CircleAvatar(
                      backgroundColor: HallyColors.lavenderSoft,
                      child: Text(m.displayName.characters.first.toUpperCase()),
                    ),
                    title: Text(m.displayName),
                    subtitle: Text(m.role == 'Owner' ? l10n.roleOwner : l10n.roleAdult),
                  ),
                ),
              const SizedBox(height: HallySpace.md),
              OutlinedButton.icon(
                onPressed: () => context.push('/family/${f.id}/invite'),
                icon: const Icon(Icons.person_add_alt),
                label: Text(l10n.familyInviteMore),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
