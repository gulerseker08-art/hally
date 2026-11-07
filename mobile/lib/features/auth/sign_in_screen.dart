import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/analytics/analytics.dart';
import '../../core/api/api_client.dart';
import '../../core/auth/auth_repository.dart';
import '../../core/config/app_config.dart';
import '../../core/design/tokens.dart';
import '../../core/design/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

class SignInScreen extends ConsumerStatefulWidget {
  const SignInScreen({super.key});

  @override
  ConsumerState<SignInScreen> createState() => _SignInScreenState();
}

class _SignInScreenState extends ConsumerState<SignInScreen> {
  final _email = TextEditingController();
  final _name = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _email.dispose();
    _name.dispose();
    super.dispose();
  }

  Future<void> _run(String provider, Future<void> Function(AuthRepository repo, String locale) action) async {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).languageCode;
    ref.read(analyticsProvider).track(AnalyticsEvent.signInStarted, {'provider': provider});
    setState(() => _busy = true);
    try {
      await action(ref.read(authRepositoryProvider), locale);
      ref.invalidate(meProvider); // router redirects once the user is loaded
    } on ApiException catch (e) {
      if (mounted && e.code != 'auth.cancelled') {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.signInFailed)));
      }
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.signInFailed)));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final repo = ref.watch(authRepositoryProvider);
    return Scaffold(
      appBar: AppBar(),
      body: HallyPage(
        children: [
          Text(l10n.signInTitle, style: Theme.of(context).textTheme.headlineMedium),
          const SizedBox(height: HallySpace.xl),
          if (_busy) const HallyLoading(),
          if (!_busy) ...[
            FilledButton.icon(
              key: const Key('signIn.google'),
              icon: const Icon(Icons.g_mobiledata),
              onPressed: () => _run('google', (r, l) => r.signInWithGoogle(l)),
              label: Text(l10n.signInGoogle),
            ),
            if (repo.appleAvailable) ...[
              const SizedBox(height: HallySpace.sm),
              OutlinedButton.icon(
                key: const Key('signIn.apple'),
                icon: const Icon(Icons.apple),
                onPressed: () => _run('apple', (r, l) => r.signInWithApple(l)),
                label: Text(l10n.signInApple),
              ),
            ],
            if (AppConfig.enableDevSignIn) ...[
              const SizedBox(height: HallySpace.xl),
              const Divider(),
              const SizedBox(height: HallySpace.md),
              Text(l10n.signInDev, style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: HallySpace.sm),
              TextField(
                key: const Key('signIn.devEmail'),
                controller: _email,
                keyboardType: TextInputType.emailAddress,
                decoration: InputDecoration(labelText: l10n.signInDevEmail),
              ),
              const SizedBox(height: HallySpace.sm),
              TextField(
                key: const Key('signIn.devName'),
                controller: _name,
                decoration: InputDecoration(labelText: l10n.signInDevName),
              ),
              const SizedBox(height: HallySpace.sm),
              OutlinedButton(
                key: const Key('signIn.dev'),
                onPressed: () {
                  if (!_email.text.contains('@') || _name.text.trim().isEmpty) return;
                  _run('dev', (r, l) => r.signInWithDev(_email.text.trim(), _name.text.trim(), l));
                },
                child: Text(l10n.signInDev),
              ),
            ],
          ],
        ],
      ),
    );
  }
}
