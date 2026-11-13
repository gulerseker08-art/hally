import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_client.dart';

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
  static const summaryCardShared = 'summary_card_shared';
  static const captureStarted = 'capture_started';
  static const proposalApproved = 'proposal_approved';
  static const proposalRejected = 'proposal_rejected';
  static const paywallViewed = 'paywall_viewed';
  static const paywallDismissed = 'paywall_dismissed';
  static const purchaseStarted = 'purchase_started';
}

/// Development sink. Replace with a vendor implementation behind the same interface.
class DebugAnalytics implements Analytics {
  @override
  void track(String event, [Map<String, Object?> properties = const {}]) {
    debugPrint('[analytics] $event $properties');
  }
}

/// Sends the events only the app can see to the API (which allow-lists names and property keys).
/// Events the server records itself (captures, proposals, invitations...) are only logged here.
class ApiAnalytics implements Analytics {
  ApiAnalytics(this._api);

  final ApiClient _api;

  static const serverEvents = {
    AnalyticsEvent.paywallViewed,
    AnalyticsEvent.paywallDismissed,
    AnalyticsEvent.purchaseStarted,
    AnalyticsEvent.summaryCardShared,
    AnalyticsEvent.invitationShared,
  };

  @override
  void track(String event, [Map<String, Object?> properties = const {}]) {
    if (kDebugMode) debugPrint('[analytics] $event $properties');
    if (!serverEvents.contains(event)) return;
    // Fire and forget: analytics must never break or slow down a screen.
    _api.dio
        .post<void>('/api/v1/analytics/events', data: {'name': event, 'properties': properties})
        .then((_) {}, onError: (_) {});
  }
}

final analyticsProvider = Provider<Analytics>((ref) =>
    Platform.environment.containsKey('FLUTTER_TEST') ? DebugAnalytics() : ApiAnalytics(ref.watch(apiClientProvider)));
