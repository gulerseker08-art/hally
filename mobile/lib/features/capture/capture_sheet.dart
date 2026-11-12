import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'capture_models.dart';
import 'capture_repository.dart';

/// Bottom sheet opened by the central "Yakala" tab.
Future<void> showCaptureSheet(BuildContext context, WidgetRef ref) {
  final l10n = AppLocalizations.of(context);
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    builder: (sheetContext) {
      final theme = Theme.of(sheetContext);
      return SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(HallySpace.lg, 0, HallySpace.lg, HallySpace.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  const HallyIconBadge(icon: Icons.auto_awesome, color: HallyColors.rose, gradient: HallyGradients.magic, size: 40),
                  const SizedBox(width: HallySpace.md),
                  Expanded(child: Text(l10n.captureTitle, style: theme.textTheme.headlineSmall)),
                ],
              ),
              const SizedBox(height: HallySpace.sm),
              Text(l10n.captureSubtitle, style: theme.textTheme.bodyMedium),
              const SizedBox(height: HallySpace.lg),
              CaptureOption(
                key: const Key('capture.camera'),
                icon: Icons.photo_camera_rounded,
                gradient: HallyGradients.lavender,
                color: HallyColors.lavender,
                label: l10n.captureCamera,
                hint: l10n.captureCameraHint,
                onTap: () => _pick(sheetContext, context, ref, ImageSource.camera, 'Camera'),
              ),
              CaptureOption(
                key: const Key('capture.gallery'),
                icon: Icons.photo_library_rounded,
                gradient: HallyGradients.mint,
                color: HallyColors.mint,
                label: l10n.captureGallery,
                hint: l10n.captureGalleryHint,
                onTap: () => _pick(sheetContext, context, ref, ImageSource.gallery, 'Gallery'),
              ),
              CaptureOption(
                key: const Key('capture.text'),
                icon: Icons.chat_rounded,
                gradient: HallyGradients.apricot,
                color: HallyColors.apricot,
                label: l10n.captureText,
                hint: l10n.captureTextShort,
                onTap: () {
                  Navigator.of(sheetContext).pop();
                  context.push('/capture/text');
                },
              ),
            ],
          ),
        ),
      );
    },
  );
}

/// Starts a photo capture without the sheet (used by the quick actions on Today).
Future<void> startPhotoCapture(BuildContext context, WidgetRef ref, {required bool camera}) => _pick(
      null,
      context,
      ref,
      camera ? ImageSource.camera : ImageSource.gallery,
      camera ? 'Camera' : 'Gallery',
    );

Future<void> _pick(BuildContext? sheetContext, BuildContext context, WidgetRef ref, ImageSource source, String sourceName) async {
  if (sheetContext != null) Navigator.of(sheetContext).pop();
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
    builder: (_) => const CaptureProgressDialog(),
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

/// Large tappable row in the capture sheet.
class CaptureOption extends StatelessWidget {
  const CaptureOption({
    super.key,
    required this.icon,
    required this.gradient,
    required this.color,
    required this.label,
    required this.hint,
    required this.onTap,
  });

  final IconData icon;
  final Gradient gradient;
  final Color color;
  final String label;
  final String hint;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: HallySpace.sm + 2),
      child: HallyPressable(
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.all(HallySpace.md - 2),
          decoration: BoxDecoration(
            color: HallyColors.softOf(color, theme.brightness).withValues(alpha: 0.08),
            borderRadius: BorderRadius.circular(HallyRadius.lg),
            border: Border.all(color: HallyColors.softOf(color, theme.brightness)),
          ),
          child: Row(
            children: [
              HallyIconBadge(icon: icon, color: color, gradient: gradient, size: 48, glow: true),
              const SizedBox(width: HallySpace.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label, style: theme.textTheme.titleMedium),
                    const SizedBox(height: 2),
                    Text(hint, style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
              Icon(Icons.chevron_right_rounded, color: theme.colorScheme.onSurfaceVariant),
            ],
          ),
        ),
      ),
    );
  }
}

/// Shown while the API extracts tasks: a breathing gradient orb and rotating status lines.
class CaptureProgressDialog extends StatefulWidget {
  const CaptureProgressDialog({super.key});

  @override
  State<CaptureProgressDialog> createState() => _CaptureProgressDialogState();
}

class _CaptureProgressDialogState extends State<CaptureProgressDialog> with SingleTickerProviderStateMixin {
  late final _pulse = AnimationController(vsync: this, duration: const Duration(milliseconds: 1400))..repeat(reverse: true);
  Timer? _timer;
  int _step = 0;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(milliseconds: 1800), (_) => setState(() => _step = (_step + 1) % 3));
  }

  @override
  void dispose() {
    _timer?.cancel();
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final steps = [l10n.captureStepRead, l10n.captureStepDates, l10n.captureStepTasks];
    return Dialog(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(HallySpace.lg, HallySpace.xl, HallySpace.lg, HallySpace.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            AnimatedBuilder(
              animation: _pulse,
              builder: (_, child) {
                final t = Curves.easeInOut.transform(_pulse.value);
                return Transform.scale(
                  scale: 0.92 + t * 0.12,
                  child: Container(
                    width: 84,
                    height: 84,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      gradient: HallyGradients.magic,
                      boxShadow: [BoxShadow(color: HallyColors.rose.withValues(alpha: 0.25 + t * 0.25), blurRadius: 20 + t * 20)],
                    ),
                    child: child,
                  ),
                );
              },
              child: const Icon(Icons.auto_awesome, color: Colors.white, size: 38),
            ),
            const SizedBox(height: HallySpace.lg),
            Text(l10n.captureWorking, style: theme.textTheme.titleLarge),
            const SizedBox(height: HallySpace.sm),
            AnimatedSwitcher(
              duration: HallyMotion.medium,
              transitionBuilder: (child, a) => FadeTransition(
                opacity: a,
                child: SlideTransition(position: Tween(begin: const Offset(0, 0.3), end: Offset.zero).animate(a), child: child),
              ),
              child: Text(steps[_step], key: ValueKey(_step), style: theme.textTheme.bodyMedium),
            ),
            const SizedBox(height: HallySpace.lg),
            ClipRRect(
              borderRadius: BorderRadius.circular(HallyRadius.pill),
              child: const LinearProgressIndicator(minHeight: 6),
            ),
          ],
        ),
      ),
    );
  }
}
