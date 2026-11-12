import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../l10n/gen/app_localizations.dart';
import 'capture_models.dart';
import 'capture_repository.dart';

/// Bottom sheet opened by the central "Yakala" tab.
Future<void> showCaptureSheet(BuildContext context, WidgetRef ref) {
  final l10n = AppLocalizations.of(context);
  return showModalBottomSheet<void>(
    context: context,
    showDragHandle: true,
    builder: (sheetContext) => SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(HallySpace.md, 0, HallySpace.md, HallySpace.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(l10n.captureTitle, style: Theme.of(sheetContext).textTheme.titleLarge),
            const SizedBox(height: HallySpace.md),
            _Option(
              key: const Key('capture.camera'),
              icon: Icons.photo_camera_outlined,
              color: HallyColors.lavender,
              label: l10n.captureCamera,
              onTap: () => _pick(sheetContext, context, ref, ImageSource.camera, 'Camera'),
            ),
            _Option(
              key: const Key('capture.gallery'),
              icon: Icons.photo_library_outlined,
              color: HallyColors.mint,
              label: l10n.captureGallery,
              onTap: () => _pick(sheetContext, context, ref, ImageSource.gallery, 'Gallery'),
            ),
            _Option(
              key: const Key('capture.text'),
              icon: Icons.content_paste,
              color: HallyColors.apricot,
              label: l10n.captureText,
              onTap: () {
                Navigator.of(sheetContext).pop();
                context.push('/capture/text');
              },
            ),
          ],
        ),
      ),
    ),
  );
}

Future<void> _pick(BuildContext sheetContext, BuildContext context, WidgetRef ref, ImageSource source, String sourceName) async {
  Navigator.of(sheetContext).pop();
  final file = await ImagePicker().pickImage(source: source, maxWidth: 2400, imageQuality: 85);
  if (file == null || !context.mounted) return;
  final familyId = ref.read(meProvider).valueOrNull?.primaryFamily?.id;
  if (familyId == null) return;

  ref.read(analyticsProvider).track(AnalyticsEvent.captureStarted, {'source': sourceName.toLowerCase()});
  final bytes = await file.readAsBytes();
  final name = file.name.toLowerCase();
  final type = name.endsWith('.png') ? 'image/png' : name.endsWith('.heic') ? 'image/heic' : name.endsWith('.webp') ? 'image/webp' : 'image/jpeg';
  if (!context.mounted) return;
  await runCapture(context, ref, () => ref.read(captureRepositoryProvider).captureImage(familyId, bytes, file.name, type, source: sourceName));
}

/// Shows a progress dialog while the API extracts, then opens the review screen.
Future<void> runCapture(BuildContext context, WidgetRef ref, Future<CaptureResult> Function() action) async {
  final l10n = AppLocalizations.of(context);
  final navigator = Navigator.of(context, rootNavigator: true);
  showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (_) => AlertDialog(
      content: Row(
        children: [
          const CircularProgressIndicator(),
          const SizedBox(width: HallySpace.md),
          Expanded(child: Text(l10n.captureWorking)),
        ],
      ),
    ),
  );
  try {
    final result = await action();
    navigator.pop();
    ref.read(lastCaptureProvider.notifier).state = result;
    if (context.mounted) context.push('/capture/review');
  } catch (_) {
    navigator.pop();
    if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.captureFailed)));
  }
}

class _Option extends StatelessWidget {
  const _Option({super.key, required this.icon, required this.color, required this.label, required this.onTap});

  final IconData icon;
  final Color color;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => ListTile(
        leading: CircleAvatar(backgroundColor: color.withValues(alpha: 0.15), child: Icon(icon, color: color)),
        title: Text(label),
        onTap: onTap,
      );
}
