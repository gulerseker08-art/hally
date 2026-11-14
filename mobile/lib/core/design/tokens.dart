import 'package:flutter/material.dart';

/// Hally design tokens. Keep raw values here only; widgets read them through the theme or these classes.
abstract final class HallyColors {
  static const lavender = Color(0xFF7C6BD6);
  static const lavenderDeep = Color(0xFF5A48C2);
  static const lavenderSoft = Color(0xFFEDE9FB);
  static const mint = Color(0xFF3FB59A);
  static const mintDeep = Color(0xFF23937A);
  static const mintSoft = Color(0xFFE2F5EF);
  static const apricot = Color(0xFFE08A4C);
  static const apricotSoft = Color(0xFFFBEADD);
  static const rose = Color(0xFFE27AA6);
  static const roseSoft = Color(0xFFFBE7F0);
  static const sky = Color(0xFF4FA3E0);
  static const skySoft = Color(0xFFE3F0FB);
  static const ink = Color(0xFF1F1B2E);
  static const inkMuted = Color(0xFF6B6680);
  static const line = Color(0xFFECE9F5);
  static const canvas = Color(0xFFF7F5FD);
  static const surface = Color(0xFFFFFFFF);
  static const canvasDark = Color(0xFF14121C);
  static const surfaceDark = Color(0xFF1E1B29);
  static const lineDark = Color(0xFF2E2A3D);
  static const error = Color(0xFFD64545);

  /// Avatar colours, picked by a stable hash of the member's name.
  static const avatars = [lavender, mint, apricot, rose, sky];

  /// Accent colour for text on its own soft tint ([softOf]): darkened in light mode and lightened in dark mode
  /// so small labels reach WCAG AA (raw apricot or mint on their tint is only ~2.4:1).
  static Color readableOn(Color accent, Brightness brightness) => brightness == Brightness.dark
      ? Color.lerp(accent, Colors.white, 0.35)!
      : Color.lerp(accent, ink, 0.45)!;

  /// Tinted background for an accent colour that also works in dark mode.
  static Color softOf(Color accent, Brightness brightness) =>
      accent.withValues(alpha: brightness == Brightness.dark ? 0.22 : 0.13);
}

abstract final class HallyGradients {
  /// Brand hero: deep lavender into a warm rose glow.
  static const hero = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF5A48C2), Color(0xFF7C6BD6), Color(0xFFB57BD0)],
  );

  /// Sunrise accent used for the AI ("Hally") moments.
  static const magic = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF7C6BD6), Color(0xFFE27AA6), Color(0xFFF0A36A)],
  );

  /// Readable variant of [magic] for cards that carry white body text: every stop keeps white text at 4.6:1 or more.
  static const magicCard = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF5A48C2), Color(0xFF8A4FBF), Color(0xFFB4547C)],
  );

  /// Readable mint for surfaces with white text (swipe-to-complete).
  static const mintDeep = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF1B7A65), Color(0xFF23937A)],
  );

  static const mint = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF3FB59A), Color(0xFF6FD3B4)],
  );

  static const apricot = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFFE08A4C), Color(0xFFF2B36F)],
  );

  static const lavender = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF6A58D2), Color(0xFF9C8CF0)],
  );
}

abstract final class HallyShadows {
  static List<BoxShadow> soft(Brightness b) => b == Brightness.dark
      ? const []
      : [BoxShadow(color: const Color(0xFF3B2F7A).withValues(alpha: 0.07), blurRadius: 24, offset: const Offset(0, 8))];

  static List<BoxShadow> glow(Color c) => [BoxShadow(color: c.withValues(alpha: 0.35), blurRadius: 24, offset: const Offset(0, 10))];
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
  static const sm = 12.0;
  static const md = 18.0;
  static const lg = 24.0;
  static const xl = 32.0;
  static const pill = 999.0;
}

abstract final class HallyMotion {
  static const fast = Duration(milliseconds: 160);
  static const medium = Duration(milliseconds: 280);
  static const slow = Duration(milliseconds: 450);
  static const curve = Curves.easeOutCubic;
}

abstract final class HallyFonts {
  /// Display / headings. Bundled under assets/fonts (SIL OFL).
  static const display = 'Bricolage Grotesque';
  static const body = 'Figtree';
}
