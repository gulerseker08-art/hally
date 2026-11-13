import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:share_plus/share_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../family/family_repository.dart';
import 'privacy_repository.dart';

/// Plain-language privacy summary, data export, leaving the family and deleting the account.
class PrivacyScreen extends ConsumerStatefulWidget {
  const PrivacyScreen({super.key});

  @override
  ConsumerState<PrivacyScreen> createState() => _PrivacyScreenState();
}

class _PrivacyScreenState extends ConsumerState<PrivacyScreen> {
  bool _busy = false;

  Future<void> _guarded(Future<void> Function() action) async {
    setState(() => _busy = true);
    final messenger = ScaffoldMessenger.of(context);
    final l10n = AppLocalizations.of(context);
    try {
      await action();
    } catch (_) {
      messenger.showSnackBar(SnackBar(content: Text(l10n.errorGeneric)));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _export() => _guarded(() async {
        final bytes = await ref.read(privacyRepositoryProvider).export();
        final name = 'hally-export-${DateFormat('yyyy-MM-dd').format(DateTime.now())}.json';
        // ignore: deprecated_member_use
        await Share.shareXFiles([XFile.fromData(bytes, mimeType: 'application/json', name: name)]);
      });

  Future<void> _leave(String familyId, bool alone) async {
    final l10n = AppLocalizations.of(context);
    final ok = await _confirm(
      title: l10n.privacyLeaveTitle,
      body: alone ? l10n.privacyLeaveAloneBody : l10n.privacyLeaveBody,
      action: l10n.privacyLeaveAction,
    );
    if (ok != true) return;
    await _guarded(() async {
      await ref.read(privacyRepositoryProvider).leaveFamily(familyId);
      ref.invalidate(meProvider); // the router moves on to family setup
    });
  }

  Future<void> _delete() async {
    final l10n = AppLocalizations.of(context);
    final first = await _confirm(title: l10n.privacyDeleteTitle, body: l10n.privacyDeleteBody, action: l10n.privacyDeleteContinue);
    if (first != true || !mounted) return;
    final typed = await showDialog<bool>(context: context, builder: (_) => _TypeToConfirm(word: l10n.privacyDeleteWord));
    if (typed != true || !mounted) return;
    await _guarded(() async {
      final messenger = ScaffoldMessenger.of(context);
      await ref.read(privacyRepositoryProvider).deleteAccount();
      await ref.read(authRepositoryProvider).signOut();
      messenger.showSnackBar(SnackBar(content: Text(l10n.privacyDeleted)));
      ref.invalidate(meProvider); // the router moves on to the welcome screen
    });
  }

  Future<bool?> _confirm({required String title, required String body, required String action}) => showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(title),
          content: Text(body),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context, false), child: Text(AppLocalizations.of(context).cancel)),
            TextButton(
              key: const Key('privacy.confirm'),
              style: TextButton.styleFrom(foregroundColor: HallyColors.error),
              onPressed: () => Navigator.pop(context, true),
              child: Text(action),
            ),
          ],
        ),
      );

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final info = ref.watch(privacyInfoProvider).valueOrNull;
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    final family = familyId == null ? null : ref.watch(familyProvider(familyId)).valueOrNull;
    final theme = Theme.of(context);

    final points = [
      (Icons.verified_user_rounded, HallyColors.lavender, l10n.privacyPointApproval),
      (
        Icons.psychology_rounded,
        HallyColors.sky,
        info?.aiProviderNoTraining == true ? l10n.privacyPointAiNoTraining : l10n.privacyPointAiProcessing,
      ),
      (Icons.lock_rounded, HallyColors.mint, l10n.privacyPointPhotos(info?.originalsRetentionDays ?? 30)),
      (Icons.child_care_rounded, HallyColors.apricot, l10n.privacyPointChildren),
      (Icons.link_rounded, HallyColors.rose, l10n.privacyPointLinks),
      (Icons.analytics_rounded, HallyColors.lavender, l10n.privacyPointAnalytics),
    ];

    return Scaffold(
      appBar: AppBar(title: Text(l10n.privacyTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.sm, HallySpace.md, HallySpace.xl),
        children: [
          for (final (i, (icon, color, text)) in points.indexed)
            HallyEntrance(
              index: i,
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(HallySpace.md),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      HallyIconBadge(icon: icon, color: color, size: 40),
                      const SizedBox(width: HallySpace.md),
                      Expanded(child: Text(text, key: Key('privacy.point.$i'), style: theme.textTheme.bodyMedium)),
                    ],
                  ),
                ),
              ),
            ),
          if (info?.policyUrl != null)
            TextButton(
              onPressed: () => launchUrl(Uri.parse(info!.policyUrl!), mode: LaunchMode.externalApplication),
              child: Text(l10n.privacyPolicy),
            ),
          HallySectionHeader(title: l10n.privacyYourData),
          Card(
            child: Column(
              children: [
                ListTile(
                  key: const Key('privacy.export'),
                  leading: const Icon(Icons.download_rounded),
                  title: Text(l10n.privacyExport),
                  subtitle: Text(l10n.privacyExportBody),
                  enabled: !_busy,
                  onTap: _export,
                ),
                if (family != null)
                  ListTile(
                    key: const Key('privacy.leave'),
                    leading: const Icon(Icons.logout_rounded),
                    title: Text(l10n.privacyLeave(family.name)),
                    enabled: !_busy,
                    onTap: () => _leave(family.id, family.members.length <= 1),
                  ),
                ListTile(
                  key: const Key('privacy.delete'),
                  leading: const Icon(Icons.delete_forever_rounded, color: HallyColors.error),
                  title: Text(l10n.privacyDelete, style: const TextStyle(color: HallyColors.error)),
                  subtitle: Text(l10n.privacyDeleteHint),
                  enabled: !_busy,
                  onTap: _delete,
                ),
              ],
            ),
          ),
          if (info?.contactEmail != null)
            Padding(
              padding: const EdgeInsets.only(top: HallySpace.md),
              child: Text(l10n.privacyContact(info!.contactEmail!), style: theme.textTheme.bodySmall, textAlign: TextAlign.center),
            ),
        ],
      ),
    );
  }
}

/// Second step of account deletion: type the word to enable the button.
class _TypeToConfirm extends StatefulWidget {
  const _TypeToConfirm({required this.word});

  final String word;

  @override
  State<_TypeToConfirm> createState() => _TypeToConfirmState();
}

class _TypeToConfirmState extends State<_TypeToConfirm> {
  /// Case-insensitive for Turkish too: "sil", "SIL" and "SİL" all match.
  static String _normalize(String s) => s.trim().toLowerCase().replaceAll('\u0307', '').replaceAll('ı', 'i');

  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final matches = _normalize(_controller.text) == _normalize(widget.word);
    return AlertDialog(
      title: Text(l10n.privacyDeleteTypeTitle(widget.word)),
      content: TextField(
        key: const Key('privacy.deleteWord'),
        controller: _controller,
        autofocus: true,
        onChanged: (_) => setState(() {}),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: Text(l10n.cancel)),
        FilledButton(
          key: const Key('privacy.deleteFinal'),
          style: FilledButton.styleFrom(backgroundColor: HallyColors.error),
          onPressed: matches ? () => Navigator.pop(context, true) : null,
          child: Text(l10n.privacyDelete),
        ),
      ],
    );
  }
}
