import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/analytics/analytics.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../capture/capture_repository.dart';
import '../demo/demo_repository.dart';
import 'family_repository.dart';

/// IANA zones offered at setup, launch markets first.
const supportedTimeZones = [
  'Europe/Istanbul',
  'Europe/London',
  'Europe/Berlin',
  'America/New_York',
  'America/Chicago',
  'America/Los_Angeles',
];

class FamilySetupScreen extends ConsumerStatefulWidget {
  const FamilySetupScreen({super.key});

  @override
  ConsumerState<FamilySetupScreen> createState() => _FamilySetupScreenState();
}

class _FamilySetupScreenState extends ConsumerState<FamilySetupScreen> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  String? _timeZone;
  bool _busy = false;

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _submit(String timeZone) async {
    if (!_form.currentState!.validate()) return;
    setState(() => _busy = true);
    try {
      final family = await ref.read(familyRepositoryProvider).create(_name.text.trim(), timeZone);
      ref.read(analyticsProvider).track(AnalyticsEvent.familyCreated, {'time_zone': timeZone});
      await _saveDemoItems(family.id);
      ref.invalidate(meProvider);
      if (mounted) context.go('/family/${family.id}/invite');
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(AppLocalizations.of(context).errorGeneric)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// "Save these to your family": the sample scanned before sign-up is captured for real, so its
  /// proposals wait for approval on Today. Best effort: onboarding continues if it fails.
  Future<void> _saveDemoItems(String familyId) async {
    final text = ref.read(pendingDemoTextProvider);
    if (text == null) return;
    ref.read(pendingDemoTextProvider.notifier).state = null;
    try {
      await ref.read(captureRepositoryProvider).captureText(familyId, text, source: 'Text');
    } catch (_) {
      // The user can capture again from Today.
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final defaultZone = Localizations.localeOf(context).languageCode == 'tr' ? 'Europe/Istanbul' : 'Europe/London';
    final zone = _timeZone ?? defaultZone;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.familySetupTitle)),
      body: Form(
        key: _form,
        child: HallyPage(
          bottom: FilledButton(
            key: const Key('familySetup.create'),
            onPressed: _busy ? null : () => _submit(zone),
            child: _busy ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2)) : Text(l10n.familySetupCreate),
          ),
          children: [
            TextFormField(
              key: const Key('familySetup.name'),
              controller: _name,
              maxLength: 80,
              decoration: InputDecoration(labelText: l10n.familySetupNameLabel, hintText: l10n.familySetupNameHint),
              validator: (v) => (v == null || v.trim().isEmpty) ? l10n.familyNameRequired : null,
            ),
            const SizedBox(height: HallySpace.md),
            DropdownButtonFormField<String>(
              initialValue: zone,
              decoration: InputDecoration(labelText: l10n.familySetupTimeZone(zone)),
              items: [for (final z in supportedTimeZones) DropdownMenuItem(value: z, child: Text(z))],
              onChanged: (z) => setState(() => _timeZone = z),
            ),
          ],
        ),
      ),
    );
  }
}
