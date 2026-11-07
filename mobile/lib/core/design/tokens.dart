import 'package:flutter/material.dart';

/// Hally design tokens. Keep raw values here only; widgets read them through the theme.
abstract final class HallyColors {
  static const lavender = Color(0xFF7C6BD6);
  static const lavenderSoft = Color(0xFFEDE9FB);
  static const mint = Color(0xFF3FB59A);
  static const mintSoft = Color(0xFFE2F5EF);
  static const apricot = Color(0xFFE08A4C);
  static const apricotSoft = Color(0xFFFBEADD);
  static const ink = Color(0xFF1F1B2E);
  static const inkMuted = Color(0xFF6B6680);
  static const canvas = Color(0xFFFBFAFE);
  static const surface = Color(0xFFFFFFFF);
  static const canvasDark = Color(0xFF14121C);
  static const surfaceDark = Color(0xFF1E1B29);
  static const error = Color(0xFFD64545);
}

abstract final class HallySpace {
  static const xs = 4.0;
  static const sm = 8.0;
  static const md = 16.0;
  static const lg = 24.0;
  static const xl = 32.0;
  static const xxl = 48.0;
}

abstract final class HallyRadius {
  static const sm = 10.0;
  static const md = 16.0;
  static const lg = 24.0;
  static const pill = 999.0;
}

abstract final class HallyFonts {
  /// Display / headings. Bundle the font files under assets/fonts to activate; falls back to system.
  static const display = 'Bricolage Grotesque';
  static const body = 'Figtree';
}
