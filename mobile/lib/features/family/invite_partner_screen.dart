import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/analytics/analytics.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'family_repository.dart';

class InvitePartnerScreen extends ConsumerStatefulWidget {
  const InvitePartnerScreen({super.key, required this.familyId});

  final String familyId;

  @override
  ConsumerState<InvitePartnerScreen> createState() => _InvitePartnerScreenState();
}

class _InvitePartnerScreenState extends ConsumerState<InvitePartnerScreen> {
  Invitation? _invitation;
  bool _busy = false;

  Future<Invitation?> _ensureInvitation() async {
    if (_invitation != null) return _invitation;
    setState(() => _busy = true);
    try {
      final inv = await ref.read(familyRepositoryProvider).invite(widget.familyId);
      setState(() => _invitation = inv);
      return inv;
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).errorGeneric)));
      }
      return null;
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _shareWhatsApp() async {
    final inv = await _ensureInvitation();
    if (inv == null) return;
    ref.read(analyticsProvider).track(AnalyticsEvent.invitationShared, {'channel': 'whatsapp'});
    await launchUrl(Uri.parse(inv.whatsAppUrl), mode: LaunchMode.externalApplication);
  }

  Future<void> _copy() async {
    final inv = await _ensureInvitation();
    if (inv == null || !mounted) return;
    final l10n = AppLocalizations.of(context);
    await Clipboard.setData(ClipboardData(text: inv.shareText));
    ref.read(analyticsProvider).track(AnalyticsEvent.invitationShared, {'channel': 'copy'});
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.inviteCopied)));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.inviteTitle)),
      body: HallyPage(
        bottom: Column(
          children: [
            FilledButton.icon(
              key: const Key('invite.whatsapp'),
              onPressed: _busy ? null : _shareWhatsApp,
              icon: const Icon(Icons.chat_outlined),
              label: Text(l10n.inviteWhatsApp),
            ),
            const SizedBox(height: HallySpace.sm),
            OutlinedButton.icon(
              key: const Key('invite.copy'),
              onPressed: _busy ? null : _copy,
              icon: const Icon(Icons.link),
              label: Text(l10n.inviteCopy),
            ),
            TextButton(onPressed: () => context.go('/today'), child: Text(l10n.inviteSkip)),
          ],
        ),
        children: [
          const CircleAvatar(
            radius: 40,
            backgroundColor: HallyColors.mintSoft,
            child: Icon(Icons.favorite_outline, size: 40, color: HallyColors.mint),
          ),
          const SizedBox(height: HallySpace.lg),
          Text(l10n.inviteBody, style: Theme.of(context).textTheme.bodyLarge),
          if (_invitation != null) ...[
            const SizedBox(height: HallySpace.lg),
            SelectableText(_invitation!.url, style: Theme.of(context).textTheme.bodySmall),
          ],
        ],
      ),
    );
  }
}
