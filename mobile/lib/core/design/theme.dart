import 'package:flutter/material.dart';

import 'tokens.dart';

ThemeData buildHallyTheme(Brightness brightness) {
  final dark = brightness == Brightness.dark;
  final scheme = ColorScheme.fromSeed(
    seedColor: HallyColors.lavender,
    brightness: brightness,
    primary: HallyColors.lavender,
    secondary: HallyColors.mint,
    tertiary: HallyColors.apricot,
    error: HallyColors.error,
    surface: dark ? HallyColors.surfaceDark : HallyColors.surface,
  );

  final base = ThemeData(useMaterial3: true, colorScheme: scheme, fontFamily: HallyFonts.body);
  final text = base.textTheme.copyWith(
    displaySmall: base.textTheme.displaySmall?.copyWith(fontFamily: HallyFonts.display, fontWeight: FontWeight.w700),
    headlineMedium: base.textTheme.headlineMedium?.copyWith(fontFamily: HallyFonts.display, fontWeight: FontWeight.w700),
    headlineSmall: base.textTheme.headlineSmall?.copyWith(fontFamily: HallyFonts.display, fontWeight: FontWeight.w700),
    titleLarge: base.textTheme.titleLarge?.copyWith(fontFamily: HallyFonts.display, fontWeight: FontWeight.w600),
  );

  final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(HallyRadius.md));
  return base.copyWith(
    scaffoldBackgroundColor: dark ? HallyColors.canvasDark : HallyColors.canvas,
    textTheme: text,
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52), shape: shape),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(52), shape: shape),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(HallyRadius.sm)),
    ),
  );
}
