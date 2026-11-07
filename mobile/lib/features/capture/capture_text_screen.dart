import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'capture_repository.dart';
import 'capture_sheet.dart';

/// Paste a WhatsApp message or announcement text. Clipboard is read only when the user pastes.
class CaptureTextScreen extends ConsumerStatefulWidget {
  const CaptureTextScreen({super.key});

  @override
  ConsumerState<CaptureTextScreen> createState() => _CaptureTextScreenState();
}

class _CaptureTextScreenState extends ConsumerState<CaptureTextScreen> {
  final _text = TextEditingController();

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final familyId = ref.read(meProvider).valueOrNull?.primaryFamily?.id;
    final text = _text.text.trim();
    if (familyId == null || text.isEmpty) return;
    ref.read(analyticsProvider).track(AnalyticsEvent.captureStarted, {'source': 'text'});
    await runCapture(context, ref, () => ref.read(captureRepositoryProvider).captureText(familyId, text));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.captureText)),
      body: HallyPage(
        bottom: FilledButton(key: const Key('captureText.submit'), onPressed: _submit, child: Text(l10n.captureAnalyze)),
        children: [
          TextField(
            key: const Key('captureText.input'),
            controller: _text,
            minLines: 8,
            maxLines: 16,
            maxLength: 20000,
            decoration: InputDecoration(hintText: l10n.captureTextHint),
          ),
        ],
      ),
    );
  }
}
