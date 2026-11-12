import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_repository.dart';
import 'assistant_repository.dart';

/// Chat with Hally. Answers come from the family's own data; anything to add arrives as a suggestion to approve.
class AssistantScreen extends ConsumerStatefulWidget {
  const AssistantScreen({super.key});

  @override
  ConsumerState<AssistantScreen> createState() => _AssistantScreenState();
}

class _AssistantScreenState extends ConsumerState<AssistantScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final _entries = <ChatEntry>[];
  String? _conversationId;
  bool _sending = false;

  @override
  void dispose() {
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _send(String familyId, String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty || _sending) return;
    final l10n = AppLocalizations.of(context);
    setState(() {
      _entries.add(ChatEntry(id: 'local-${_entries.length}', fromUser: true, text: trimmed));
      _sending = true;
      _input.clear();
    });
    _toBottom();
    try {
      final reply = await ref.read(assistantRepositoryProvider).send(familyId, trimmed, conversationId: _conversationId);
      if (!mounted) return;
      setState(() {
        _conversationId = reply.conversationId;
        _entries.add(reply.reply);
      });
    } catch (_) {
      if (mounted) setState(() => _entries.add(ChatEntry(id: 'err-${_entries.length}', fromUser: false, text: l10n.assistantError)));
    } finally {
      if (mounted) setState(() => _sending = false);
      _toBottom();
    }
  }

  void _toBottom() => WidgetsBinding.instance.addPostFrameCallback((_) {
        if (_scroll.hasClients) _scroll.animateTo(_scroll.position.maxScrollExtent, duration: HallyMotion.medium, curve: HallyMotion.curve);
      });

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final familyId = ref.watch(meProvider).valueOrNull?.primaryFamily?.id;
    if (familyId == null) return const Scaffold(body: HallyLoading());
    final starters = [l10n.assistantStarterTomorrow, l10n.assistantStarterWeek, l10n.assistantStarterAdd];

    return Scaffold(
      appBar: AppBar(title: Text(l10n.tabAssistant)),
      body: Column(
        children: [
          Expanded(
            child: _entries.isEmpty
                ? ListView(
                    padding: const EdgeInsets.all(HallySpace.lg),
                    children: [
                      HallyEmptyState(icon: Icons.auto_awesome_rounded, color: HallyColors.lavender, title: l10n.assistantEmptyTitle, body: l10n.assistantEmptyBody),
                      const SizedBox(height: HallySpace.md),
                      Wrap(
                        alignment: WrapAlignment.center,
                        spacing: HallySpace.sm,
                        runSpacing: HallySpace.sm,
                        children: [
                          for (final (i, s) in starters.indexed)
                            ActionChip(key: Key('assistant.starter.$i'), label: Text(s), onPressed: () => i == 2 ? _input.text = s : _send(familyId, s)),
                        ],
                      ),
                    ],
                  )
                : ListView.builder(
                    controller: _scroll,
                    padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.md, HallySpace.md, HallySpace.md),
                    itemCount: _entries.length + (_sending ? 1 : 0),
                    itemBuilder: (context, i) => i == _entries.length ? const _Typing() : _Bubble(entry: _entries[i]),
                  ),
          ),
          SafeArea(
            top: false,
            minimum: const EdgeInsets.only(bottom: 92), // clear the floating tab bar
            child: Padding(
              padding: const EdgeInsets.fromLTRB(HallySpace.md, HallySpace.sm, HallySpace.sm, 0),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      key: const Key('assistant.input'),
                      controller: _input,
                      minLines: 1,
                      maxLines: 4,
                      maxLength: 2000,
                      textInputAction: TextInputAction.send,
                      onSubmitted: (t) => _send(familyId, t),
                      decoration: InputDecoration(hintText: l10n.assistantHint, counterText: ''),
                    ),
                  ),
                  IconButton.filled(
                    key: const Key('assistant.send'),
                    tooltip: l10n.assistantSend,
                    onPressed: _sending ? null : () => _send(familyId, _input.text),
                    icon: const Icon(Icons.arrow_upward_rounded),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Bubble extends ConsumerWidget {
  const _Bubble({required this.entry});

  final ChatEntry entry;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context);
    final mine = entry.fromUser;
    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.82),
        child: Container(
          margin: const EdgeInsets.only(bottom: HallySpace.sm),
          padding: const EdgeInsets.symmetric(horizontal: HallySpace.md, vertical: HallySpace.sm + 2),
          decoration: BoxDecoration(
            gradient: mine ? HallyGradients.lavender : null,
            color: mine ? null : theme.colorScheme.surface,
            border: mine ? null : Border.all(color: theme.colorScheme.outlineVariant),
            borderRadius: BorderRadius.only(
              topLeft: const Radius.circular(HallyRadius.md),
              topRight: const Radius.circular(HallyRadius.md),
              bottomLeft: Radius.circular(mine ? HallyRadius.md : 4),
              bottomRight: Radius.circular(mine ? 4 : HallyRadius.md),
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SelectableText(entry.text, style: theme.textTheme.bodyMedium?.copyWith(color: mine ? Colors.white : null, height: 1.4)),
              if (entry.proposals.isNotEmpty) ...[
                const SizedBox(height: HallySpace.sm),
                for (final p in entry.proposals)
                  Padding(
                    padding: const EdgeInsets.only(bottom: HallySpace.xs),
                    child: HallyPill(
                      label: [p.payload.title, if (p.payload.date != null) p.payload.date!, if (p.payload.time != null) p.payload.time!].join(' · '),
                      icon: p.isEvent ? Icons.event_rounded : Icons.check_circle_outline_rounded,
                      color: p.isEvent ? HallyColors.apricot : HallyColors.lavender,
                    ),
                  ),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    key: Key('assistant.review.${entry.id}'),
                    onPressed: () {
                      ref.read(lastCaptureProvider.notifier).state = null;
                      context.push('/capture/review');
                    },
                    icon: const Icon(Icons.fact_check_outlined),
                    label: Text(l10n.assistantReview),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _Typing extends StatelessWidget {
  const _Typing();

  @override
  Widget build(BuildContext context) => const Align(
        alignment: Alignment.centerLeft,
        child: Padding(
          padding: EdgeInsets.all(HallySpace.sm),
          child: SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2)),
        ),
      );
}
