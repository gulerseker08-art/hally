import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Product analytics boundary. Never pass PII (names, emails, child names, free text).
abstract interface class Analytics {
  void track(String event, [Map<String, Object?> properties = const {}]);
}

abstract final class AnalyticsEvent {
  static const onboardingStarted = 'onboarding_started';
  static const trustAccepted = 'onboarding_trust_accepted';
  static const signInStarted = 'auth_sign_in_started';
  static const familyCreated = 'family_created';
  static const invitationShared = 'invitation_shared';
  static const invitationAccepted = 'invitation_accepted';
  static const demoScanned = 'onboarding_demo_scanned';
  static const captureStarted = 'capture_started';
  static const proposalApproved = 'proposal_approved';
  static const proposalRejected = 'proposal_rejected';
}

/// Development sink. Replace with a vendor implementation behind the same interface.
class DebugAnalytics implements Analytics {
  @override
  void track(String event, [Map<String, Object?> properties = const {}]) {
    debugPrint('[analytics] $event $properties');
  }
}

final analyticsProvider = Provider<Analytics>((ref) => DebugAnalytics());
