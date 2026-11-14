import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'tokens.dart';

ThemeData buildHallyTheme(Brightness brightness) {
  final dark = brightness == Brightness.dark;
  final ink = dark ? const Color(0xFFF2F0FA) : HallyColors.ink;
  final muted = dark ? const Color(0xFFA9A4BD) : HallyColors.inkMuted;
  final surface = dark ? HallyColors.surfaceDark : HallyColors.surface;
  final line = dark ? HallyColors.lineDark : HallyColors.line;

  final scheme = ColorScheme.fromSeed(
    seedColor: HallyColors.lavender,
    brightness: brightness,
    // Light: deep lavender keeps white button text above WCAG AA (6.7:1; plain lavender is 4.3:1).
    // Dark: the light lavender needs dark text on top (6.5:1; white would be 2.8:1).
    primary: dark ? const Color(0xFF9C8CF0) : HallyColors.lavenderDeep,
    onPrimary: dark ? HallyColors.canvasDark : Colors.white,
    secondary: HallyColors.mint,
    tertiary: HallyColors.apricot,
    error: HallyColors.error,
    surface: surface,
    onSurface: ink,
    onSurfaceVariant: muted,
    outline: line,
    outlineVariant: line,
  );

  final base = ThemeData(useMaterial3: true, colorScheme: scheme, fontFamily: HallyFonts.body);
  TextStyle? display(TextStyle? s, FontWeight w, {double? spacing}) =>
      s?.copyWith(fontFamily: HallyFonts.display, fontWeight: w, color: ink, letterSpacing: spacing);
  final text = base.textTheme
      .apply(bodyColor: ink, displayColor: ink)
      .copyWith(
        displayMedium: display(base.textTheme.displayMedium, FontWeight.w800, spacing: -1.5),
        displaySmall: display(base.textTheme.displaySmall, FontWeight.w800, spacing: -1),
        headlineMedium: display(base.textTheme.headlineMedium, FontWeight.w700, spacing: -0.5),
        headlineSmall: display(base.textTheme.headlineSmall, FontWeight.w700, spacing: -0.3),
        titleLarge: display(base.textTheme.titleLarge, FontWeight.w700, spacing: -0.2),
        titleMedium: base.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600, color: ink),
        titleSmall: base.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600, color: ink),
        bodyLarge: base.textTheme.bodyLarge?.copyWith(height: 1.45, color: ink),
        bodyMedium: base.textTheme.bodyMedium?.copyWith(height: 1.4, color: muted),
        // Minimum readable sizes: Material's defaults (11–12px) are too small for tired parents on the go.
        bodySmall: base.textTheme.bodySmall?.copyWith(color: muted, fontSize: 13, height: 1.35),
        labelMedium: base.textTheme.labelMedium?.copyWith(fontSize: 13),
        labelSmall: base.textTheme.labelSmall?.copyWith(fontSize: 12),
        labelLarge: base.textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w700, fontSize: 16),
      );

  final buttonShape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(HallyRadius.md));
  const buttonText = TextStyle(fontFamily: HallyFonts.body, fontWeight: FontWeight.w700, fontSize: 16);

  return base.copyWith(
    scaffoldBackgroundColor: dark ? HallyColors.canvasDark : HallyColors.canvas,
    textTheme: text,
    appBarTheme: AppBarTheme(
      backgroundColor: Colors.transparent,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      foregroundColor: ink,
      systemOverlayStyle: dark ? SystemUiOverlayStyle.light : SystemUiOverlayStyle.dark,
      titleTextStyle: text.titleLarge?.copyWith(fontSize: 22),
    ),
    cardTheme: CardThemeData(
      color: surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      margin: const EdgeInsets.only(bottom: HallySpace.sm + 2),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(HallyRadius.lg),
        side: BorderSide(color: line),
      ),
      clipBehavior: Clip.antiAlias,
    ),
    listTileTheme: const ListTileThemeData(contentPadding: EdgeInsets.symmetric(horizontal: HallySpace.md, vertical: 2)),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(56),
        shape: buttonShape,
        textStyle: buttonText,
        elevation: 0,
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: const Size.fromHeight(56),
        shape: buttonShape,
        textStyle: buttonText,
        side: BorderSide(color: line, width: 1.5),
        foregroundColor: ink,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        textStyle: buttonText.copyWith(fontSize: 15),
        shape: buttonShape,
        foregroundColor: scheme.primary,
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: dark ? const Color(0xFF272336) : HallyColors.surface,
      contentPadding: const EdgeInsets.symmetric(horizontal: HallySpace.md, vertical: HallySpace.md),
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(HallyRadius.md), borderSide: BorderSide(color: line)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(HallyRadius.md), borderSide: BorderSide(color: line, width: 1.5)),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(HallyRadius.md),
        borderSide: BorderSide(color: scheme.primary, width: 2),
      ),
      hintStyle: TextStyle(color: muted),
    ),
    chipTheme: ChipThemeData(
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(HallyRadius.pill)),
      side: BorderSide(color: line),
      labelStyle: TextStyle(fontFamily: HallyFonts.body, fontWeight: FontWeight.w600, color: ink, fontSize: 13),
      backgroundColor: surface,
      selectedColor: HallyColors.softOf(HallyColors.lavender, brightness),
      checkmarkColor: scheme.primary,
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
    ),
    bottomSheetTheme: BottomSheetThemeData(
      backgroundColor: surface,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      dragHandleColor: line,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(HallyRadius.xl))),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: surface,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(HallyRadius.xl)),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: HallyColors.ink,
      contentTextStyle: const TextStyle(fontFamily: HallyFonts.body, color: Colors.white, fontWeight: FontWeight.w500),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(HallyRadius.md)),
    ),
    dividerTheme: DividerThemeData(color: line, thickness: 1),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: scheme.primary),
  );
}
