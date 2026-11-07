import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/config/app_config.dart';
import 'core/design/theme.dart';
import 'l10n/gen/app_localizations.dart';
import 'router.dart';

class HallyApp extends ConsumerWidget {
  const HallyApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      title: AppConfig.appName,
      debugShowCheckedModeBanner: false,
      theme: buildHallyTheme(Brightness.light),
      darkTheme: buildHallyTheme(Brightness.dark),
      routerConfig: ref.watch(routerProvider),
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      // Turkish is the default for unsupported device languages (Türkiye is the launch market).
      localeResolutionCallback: (device, supported) =>
          supported.firstWhere((l) => l.languageCode == device?.languageCode, orElse: () => const Locale('tr')),
    );
  }
}
