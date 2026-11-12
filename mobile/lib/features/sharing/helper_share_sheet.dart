import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'sharing_repository.dart';

/// Creates a helper link (via [create]) and offers WhatsApp or copy. The link is only created once the user picks a channel.
Future<void> showHelperShareSheet(BuildContext context, WidgetRef ref, {required String subject, required Future<HelperLink> Function() create}) =>
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (_) => _HelperShareSheet(subject: subject, create: create),
    );

class _HelperShareSheet extends StatefulWidget {
  const _HelperShareSheet({required this.subject, required this.create});

  final String subject;
  final Future<HelperLink> Function() create;

  @override
  State<_HelperShareSheet> createState() => _HelperShareSheetState();
}

class _HelperShareSheetState extends State<_HelperShareSheet> {
  HelperLink? _link;
  bool _busy = false;

  Future<HelperLink?> _ensure() async {
    if (_link != null) return _link;
    setState(() => _busy = true);
    try {
      final link = await widget.create();
      if (mounted) setState(() => _link = link);
      return link;
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).errorGeneric)));
      return null;
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final link = _link;
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(HallySpace.md, 0, HallySpace.md, HallySpace.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Align(alignment: Alignment.centerLeft, child: HallyIconBadge(icon: Icons.volunteer_activism_rounded, color: HallyColors.mint)),
            const SizedBox(height: HallySpace.sm),
            Text(l10n.helperShareTitle, style: theme.textTheme.titleLarge),
            const SizedBox(height: HallySpace.xs),
            Text(l10n.helperShareBody(widget.subject), style: theme.textTheme.bodyMedium),
            const SizedBox(height: HallySpace.md),
            FilledButton.icon(
              key: const Key('helper.whatsapp'),
              onPressed: _busy
                  ? null
                  : () async {
                      final l = await _ensure();
                      if (l != null) await launchUrl(Uri.parse(l.whatsAppUrl), mode: LaunchMode.externalApplication);
                    },
              icon: const Icon(Icons.chat_outlined),
              label: Text(l10n.inviteWhatsApp),
            ),
            const SizedBox(height: HallySpace.sm),
            OutlinedButton.icon(
              key: const Key('helper.copy'),
              onPressed: _busy
                  ? null
                  : () async {
                      final messenger = ScaffoldMessenger.of(context);
                      final l = await _ensure();
                      if (l == null) return;
                      await Clipboard.setData(ClipboardData(text: l.shareText));
                      messenger.showSnackBar(SnackBar(content: Text(l10n.inviteCopied)));
                    },
              icon: const Icon(Icons.link),
              label: Text(l10n.inviteCopy),
            ),
            if (link != null) ...[
              const SizedBox(height: HallySpace.sm),
              Text(
                l10n.helperShareExpires(DateFormat.MMMd(Localizations.localeOf(context).toLanguageTag()).add_Hm().format(link.expiresAt)),
                style: theme.textTheme.bodySmall,
                textAlign: TextAlign.center,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
