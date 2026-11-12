import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:receive_sharing_intent/receive_sharing_intent.dart';

/// Something another app shared into Hally (a school message, a screenshot, a PDF page photo…).
sealed class SharedItem {
  const SharedItem();
}

class SharedText extends SharedItem {
  const SharedText(this.text);
  final String text;
}

class SharedImage extends SharedItem {
  const SharedImage(this.path, this.mimeType);
  final String path;
  final String mimeType;
}

/// Source of shared content. The real implementation wraps the OS share sheet; tests use [NoShareIntake].
abstract interface class ShareIntake {
  /// Content the app was launched with (cold start), if any.
  Future<SharedItem?> initial();

  /// Content shared while the app is running.
  Stream<SharedItem> stream();

  /// Marks the current share as consumed so it isn't replayed on next launch.
  void reset();
}

class NoShareIntake implements ShareIntake {
  const NoShareIntake();
  @override
  Future<SharedItem?> initial() async => null;
  @override
  Stream<SharedItem> stream() => const Stream.empty();
  @override
  void reset() {}
}

class PlatformShareIntake implements ShareIntake {
  @override
  Future<SharedItem?> initial() async => _first(await ReceiveSharingIntent.instance.getInitialMedia());

  @override
  Stream<SharedItem> stream() =>
      ReceiveSharingIntent.instance.getMediaStream().map(_first).where((i) => i != null).cast<SharedItem>();

  @override
  void reset() => ReceiveSharingIntent.instance.reset();

  /// Only the first item is used: one share = one capture (keeps the approval list readable).
  static SharedItem? _first(List<SharedMediaFile> files) {
    for (final f in files) {
      final mapped = mapShared(f.type.name, f.path, f.mimeType);
      if (mapped != null) return mapped;
    }
    return null;
  }
}

/// Maps a raw share to a [SharedItem]. Pure so it can be unit-tested.
SharedItem? mapShared(String type, String value, String? mimeType) {
  switch (type) {
    case 'text':
    case 'url':
      final text = value.trim();
      return text.isEmpty ? null : SharedText(text.length > 8000 ? text.substring(0, 8000) : text);
    case 'image':
      return SharedImage(value, mimeType ?? _mimeFromPath(value));
    default:
      return null; // video / arbitrary files are not supported yet
  }
}

String _mimeFromPath(String path) {
  final p = path.toLowerCase();
  if (p.endsWith('.png')) return 'image/png';
  if (p.endsWith('.heic')) return 'image/heic';
  if (p.endsWith('.webp')) return 'image/webp';
  return 'image/jpeg';
}

final shareIntakeProvider = Provider<ShareIntake>((ref) {
  if (kIsWeb || !(Platform.isAndroid || Platform.isIOS)) return const NoShareIntake();
  return PlatformShareIntake();
});
