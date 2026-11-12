import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_repository.dart';
import '../capture/capture_sheet.dart';
import 'share_intake.dart';

/// Wraps the signed-in shell: anything shared into Hally becomes a capture and opens the review screen.
/// Shares that arrive before sign-in are picked up on cold start once the user reaches the shell.
class ShareListener extends ConsumerStatefulWidget {
  const ShareListener({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<ShareListener> createState() => _ShareListenerState();
}

class _ShareListenerState extends ConsumerState<ShareListener> {
  StreamSubscription<SharedItem>? _sub;

  @override
  void initState() {
    super.initState();
    final intake = ref.read(shareIntakeProvider);
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final first = await intake.initial();
      if (first != null) await _handle(first);
    });
    _sub = intake.stream().listen(_handle);
  }

  @override
  void dispose() {
    _sub?.cancel();
    super.dispose();
  }

  Future<void> _handle(SharedItem item) async {
    final intake = ref.read(shareIntakeProvider);
    final familyId = ref.read(meProvider).valueOrNull?.primaryFamily?.id;
    if (!mounted || familyId == null) return;
    intake.reset();
    ref.read(analyticsProvider).track(AnalyticsEvent.captureStarted, {'source': 'share'});
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).shareReceived)));
    final repo = ref.read(captureRepositoryProvider);
    switch (item) {
      case SharedText(:final text):
        await runCapture(context, ref, () => repo.captureText(familyId, text, source: 'Share'));
      case SharedImage(:final path, :final mimeType):
        final file = File(path);
        if (!await file.exists() || !mounted) return;
        final bytes = await file.readAsBytes();
        if (!mounted) return;
        await runCapture(context, ref, () => repo.captureImage(familyId, bytes, file.uri.pathSegments.last, mimeType, source: 'Share'));
    }
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
