import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'family_repository.dart';

/// Invite token waiting for the user to sign in (from a deep link opened while signed out).
final pendingInviteTokenProvider = StateProvider<String?>((ref) => null);

final invitationPreviewProvider =
    FutureProvider.autoDispose.family<InvitationPreview, String>((ref, token) => ref.watch(familyRepositoryProvider).preview(token));

/// Paste an invite link or code. Reached from Welcome ("I have an invite link").
class EnterInviteScreen extends StatefulWidget {
  const EnterInviteScreen({super.key});

  @override
  State<EnterInviteScreen> createState() => _EnterInviteScreenState();
}

class _EnterInviteScreenState extends State<EnterInviteScreen> {
  final _input = TextEditingController();

  @override
  void dispose() {
    _input.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.joinTitle)),
      body: HallyPage(
        bottom: FilledButton(
          onPressed: () {
            final token = parseInviteToken(_input.text);
            if (token.isNotEmpty) context.go('/i/$token');
          },
          child: Text(l10n.joinContinue),
        ),
        children: [
          TextField(controller: _input, decoration: InputDecoration(labelText: l10n.joinCodeLabel)),
        ],
      ),
    );
  }
}

/// Deep link target for https://{host}/i/{token}.
class JoinFamilyScreen extends ConsumerStatefulWidget {
  const JoinFamilyScreen({super.key, required this.token});

  final String token;

  @override
  ConsumerState<JoinFamilyScreen> createState() => _JoinFamilyScreenState();
}

class _JoinFamilyScreenState extends ConsumerState<JoinFamilyScreen> {
  bool _busy = false;

  Future<void> _accept(bool signedIn) async {
    if (!signedIn) {
      ref.read(pendingInviteTokenProvider.notifier).state = widget.token;
      context.go('/sign-in');
      return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(familyRepositoryProvider).accept(widget.token);
      ref.read(analyticsProvider).track(AnalyticsEvent.invitationAccepted);
      ref.read(pendingInviteTokenProvider.notifier).state = null;
      ref.invalidate(meProvider);
      if (mounted) context.go('/today');
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).errorGeneric)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final preview = ref.watch(invitationPreviewProvider(widget.token));
    final signedIn = ref.watch(meProvider).valueOrNull != null;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.joinTitle)),
      body: preview.when(
        loading: () => const HallyLoading(),
        error: (_, __) => HallyErrorState(onRetry: () => ref.invalidate(invitationPreviewProvider(widget.token))),
        data: (p) => HallyPage(
          bottom: p.isPending
              ? FilledButton(
                  key: const Key('join.accept'),
                  onPressed: _busy ? null : () => _accept(signedIn),
                  child: Text(l10n.joinAccept),
                )
              : null,
          children: [
            const CircleAvatar(
              radius: 40,
              backgroundColor: HallyColors.lavenderSoft,
              child: Icon(Icons.family_restroom, size: 40, color: HallyColors.lavender),
            ),
            const SizedBox(height: HallySpace.lg),
            Text(
              switch (p.status) {
                'pending' => l10n.joinBody(p.invitedBy, p.familyName),
                'expired' || 'revoked' => l10n.joinExpired,
                _ => l10n.joinUsed,
              },
              style: Theme.of(context).textTheme.titleLarge,
            ),
          ],
        ),
      ),
    );
  }
}
