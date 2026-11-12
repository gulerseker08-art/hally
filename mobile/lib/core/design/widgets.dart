import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../l10n/gen/app_localizations.dart';
import 'tokens.dart';

/// Standard padded page body used by onboarding-style screens.
class HallyPage extends StatelessWidget {
  const HallyPage({super.key, required this.children, this.bottom});

  final List<Widget> children;
  final Widget? bottom;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(HallySpace.lg, HallySpace.sm, HallySpace.lg, HallySpace.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(child: ListView(children: children)),
            if (bottom != null) bottom!,
          ],
        ),
      ),
    );
  }
}

/// Rounded square with a gradient (or tinted) fill holding an icon.
class HallyIconBadge extends StatelessWidget {
  const HallyIconBadge({super.key, required this.icon, required this.color, this.gradient, this.size = 44, this.glow = false});

  final IconData icon;
  final Color color;
  final Gradient? gradient;
  final double size;
  final bool glow;

  @override
  Widget build(BuildContext context) {
    final brightness = Theme.of(context).brightness;
    final filled = gradient != null;
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        gradient: gradient,
        color: filled ? null : HallyColors.softOf(color, brightness),
        borderRadius: BorderRadius.circular(size * 0.32),
        boxShadow: glow ? HallyShadows.glow(color) : null,
      ),
      child: Icon(icon, color: filled ? Colors.white : color, size: size * 0.5),
    );
  }
}

/// Small rounded label, e.g. "Görev", "3".
class HallyPill extends StatelessWidget {
  const HallyPill({super.key, required this.label, required this.color, this.icon, this.solid = false});

  final String label;
  final Color color;
  final IconData? icon;
  final bool solid;

  @override
  Widget build(BuildContext context) {
    final brightness = Theme.of(context).brightness;
    final fg = solid ? Colors.white : color;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: solid ? color : HallyColors.softOf(color, brightness),
        borderRadius: BorderRadius.circular(HallyRadius.pill),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[Icon(icon, size: 14, color: fg), const SizedBox(width: 4)],
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(color: fg, fontWeight: FontWeight.w700, fontSize: 12, letterSpacing: 0.2),
            ),
          ),
        ],
      ),
    );
  }
}

/// Section title with an accent dot and optional count.
class HallySectionHeader extends StatelessWidget {
  const HallySectionHeader({super.key, required this.title, this.color, this.count});

  final String title;
  final Color? color;
  final int? count;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = color ?? theme.colorScheme.primary;
    return Padding(
      padding: const EdgeInsets.fromLTRB(HallySpace.xs, HallySpace.lg, HallySpace.xs, HallySpace.sm + 2),
      child: Row(
        children: [
          Container(width: 8, height: 8, decoration: BoxDecoration(color: accent, shape: BoxShape.circle)),
          const SizedBox(width: HallySpace.sm),
          Expanded(child: Text(title, style: theme.textTheme.titleMedium?.copyWith(fontSize: 17))),
          if (count != null) HallyPill(label: '$count', color: accent),
        ],
      ),
    );
  }
}

/// Scales down slightly while pressed and gives a light haptic tap. Wrap any tappable card with it.
class HallyPressable extends StatefulWidget {
  const HallyPressable({super.key, required this.child, required this.onTap, this.borderRadius = HallyRadius.lg});

  final Widget child;
  final VoidCallback? onTap;
  final double borderRadius;

  @override
  State<HallyPressable> createState() => _HallyPressableState();
}

class _HallyPressableState extends State<HallyPressable> {
  bool _down = false;

  void _set(bool v) {
    if (widget.onTap != null && _down != v) setState(() => _down = v);
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTapDown: (_) => _set(true),
      onTapUp: (_) => _set(false),
      onTapCancel: () => _set(false),
      onTap: widget.onTap == null
          ? null
          : () {
              HapticFeedback.selectionClick();
              widget.onTap!();
            },
      child: AnimatedScale(
        scale: _down ? 0.97 : 1,
        duration: HallyMotion.fast,
        curve: HallyMotion.curve,
        child: widget.child,
      ),
    );
  }
}

/// Fades and slides its child in once, optionally delayed by [index] for staggered lists.
class HallyEntrance extends StatelessWidget {
  const HallyEntrance({super.key, required this.child, this.index = 0});

  final Widget child;
  final int index;

  @override
  Widget build(BuildContext context) {
    final delay = math.min(index, 8) * 0.06;
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: Duration(milliseconds: 420 + (delay * 1000).round()),
      curve: Interval(delay / (0.42 + delay), 1, curve: HallyMotion.curve),
      builder: (_, t, child) => Opacity(
        opacity: t,
        child: Transform.translate(offset: Offset(0, (1 - t) * 16), child: child),
      ),
      child: child,
    );
  }
}

/// Soft blurred colour blobs used behind hero areas.
class HallyGlow extends StatelessWidget {
  const HallyGlow({super.key, required this.color, required this.size});

  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) => IgnorePointer(
        child: Container(
          width: size,
          height: size,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            gradient: RadialGradient(colors: [color.withValues(alpha: 0.55), color.withValues(alpha: 0)]),
          ),
        ),
      );
}

/// The Hally logo mark: a rounded tile with an "H" and a spark.
class HallyLogo extends StatelessWidget {
  const HallyLogo({super.key, this.size = 56, this.onDark = false});

  final double size;
  final bool onDark;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: onDark ? Colors.white : null,
        gradient: onDark ? null : HallyGradients.hero,
        borderRadius: BorderRadius.circular(size * 0.3),
        boxShadow: HallyShadows.glow(onDark ? Colors.black : HallyColors.lavender),
      ),
      child: Stack(
        alignment: Alignment.center,
        children: [
          Text(
            'H',
            style: TextStyle(
              fontFamily: HallyFonts.display,
              fontWeight: FontWeight.w800,
              fontSize: size * 0.56,
              height: 1,
              color: onDark ? HallyColors.lavenderDeep : Colors.white,
            ),
          ),
          Positioned(
            top: size * 0.14,
            right: size * 0.14,
            child: Icon(Icons.auto_awesome, size: size * 0.24, color: onDark ? HallyColors.apricot : const Color(0xFFFFD9B8)),
          ),
        ],
      ),
    );
  }
}

/// Avatar with a colour chosen from the name, so each family member keeps the same colour.
class HallyAvatar extends StatelessWidget {
  const HallyAvatar({super.key, required this.name, this.size = 44, this.ring = false});

  final String name;
  final double size;
  final bool ring;

  static Color colorFor(String name) => HallyColors.avatars[name.runes.fold<int>(7, (a, r) => (a * 31 + r) & 0x7fffffff) % HallyColors.avatars.length];

  @override
  Widget build(BuildContext context) {
    final color = colorFor(name);
    final initial = name.trim().isEmpty ? '?' : name.trim().characters.first.toUpperCase();
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [color, Color.lerp(color, Colors.white, 0.35)!],
        ),
        border: ring ? Border.all(color: Colors.white, width: 2.5) : null,
      ),
      alignment: Alignment.center,
      child: Text(
        initial,
        style: TextStyle(fontFamily: HallyFonts.display, fontWeight: FontWeight.w700, color: Colors.white, fontSize: size * 0.42),
      ),
    );
  }
}

/// Empty state that always teaches the next action.
class HallyEmptyState extends StatelessWidget {
  const HallyEmptyState({super.key, required this.icon, required this.title, required this.body, this.action, this.color});

  final IconData icon;
  final String title;
  final String body;
  final Widget? action;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = color ?? HallyColors.lavender;
    final brightness = theme.brightness;
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(HallySpace.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TweenAnimationBuilder<double>(
              tween: Tween(begin: 0.6, end: 1),
              duration: HallyMotion.slow,
              curve: Curves.easeOutBack,
              builder: (_, s, child) => Transform.scale(scale: s, child: child),
              child: SizedBox(
                width: 132,
                height: 132,
                child: Stack(
                  alignment: Alignment.center,
                  children: [
                    Container(decoration: BoxDecoration(shape: BoxShape.circle, color: HallyColors.softOf(accent, brightness))),
                    Container(
                      width: 92,
                      height: 92,
                      decoration: BoxDecoration(shape: BoxShape.circle, color: HallyColors.softOf(accent, brightness)),
                    ),
                    HallyIconBadge(
                      icon: icon,
                      color: accent,
                      size: 60,
                      glow: true,
                      gradient: LinearGradient(colors: [accent, Color.lerp(accent, Colors.white, 0.3)!]),
                    ),
                    const Positioned(top: 10, right: 18, child: Icon(Icons.auto_awesome, size: 18, color: HallyColors.apricot)),
                  ],
                ),
              ),
            ),
            const SizedBox(height: HallySpace.lg),
            Text(title, style: theme.textTheme.headlineSmall, textAlign: TextAlign.center),
            if (body.isNotEmpty) ...[
              const SizedBox(height: HallySpace.sm),
              Text(body, style: theme.textTheme.bodyLarge?.copyWith(color: theme.colorScheme.onSurfaceVariant), textAlign: TextAlign.center),
            ],
            if (action != null) ...[const SizedBox(height: HallySpace.lg), action!],
          ],
        ),
      ),
    );
  }
}

class HallyErrorState extends StatelessWidget {
  const HallyErrorState({super.key, required this.onRetry});

  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return HallyEmptyState(
      icon: Icons.cloud_off_outlined,
      color: HallyColors.apricot,
      title: l10n.errorGeneric,
      body: '',
      action: OutlinedButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh), label: Text(l10n.retry)),
    );
  }
}

class HallyLoading extends StatelessWidget {
  const HallyLoading({super.key});

  @override
  Widget build(BuildContext context) => const Center(child: CircularProgressIndicator(strokeCap: StrokeCap.round));
}
