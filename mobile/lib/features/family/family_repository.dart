import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';

class FamilyMember {
  const FamilyMember({required this.userId, required this.displayName, required this.role});

  final String userId;
  final String displayName;
  final String role;
}

class FamilyDetails {
  const FamilyDetails({required this.id, required this.name, required this.timeZoneId, required this.members});

  factory FamilyDetails.fromJson(Map<String, dynamic> json) => FamilyDetails(
        id: json['id'] as String,
        name: json['name'] as String,
        timeZoneId: json['timeZoneId'] as String,
        members: [
          for (final m in json['members'] as List)
            FamilyMember(userId: m['userId'] as String, displayName: m['displayName'] as String, role: m['role'] as String),
        ],
      );

  final String id;
  final String name;
  final String timeZoneId;
  final List<FamilyMember> members;
}

class Invitation {
  const Invitation({required this.url, required this.shareText, required this.whatsAppUrl});

  final String url;
  final String shareText;
  final String whatsAppUrl;
}

class InvitationPreview {
  const InvitationPreview({required this.familyName, required this.invitedBy, required this.status});

  final String familyName;
  final String invitedBy;
  final String status;

  bool get isPending => status == 'pending';
}

abstract interface class FamilyRepository {
  Future<FamilyDetails> create(String name, String timeZoneId);
  Future<FamilyDetails> get(String familyId);
  Future<Invitation> invite(String familyId);
  Future<InvitationPreview> preview(String token);
  Future<String> accept(String token);
}

class ApiFamilyRepository implements FamilyRepository {
  ApiFamilyRepository(this._api);

  final ApiClient _api;

  Future<T> _call<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  @override
  Future<FamilyDetails> create(String name, String timeZoneId) => _call(() async {
        final res = await _api.dio.post<Map<String, dynamic>>('/api/v1/families', data: {'name': name, 'timeZoneId': timeZoneId});
        return FamilyDetails.fromJson(res.data!);
      });

  @override
  Future<FamilyDetails> get(String familyId) => _call(() async {
        final res = await _api.dio.get<Map<String, dynamic>>('/api/v1/families/$familyId');
        return FamilyDetails.fromJson(res.data!);
      });

  @override
  Future<Invitation> invite(String familyId) => _call(() async {
        final res = await _api.dio.post<Map<String, dynamic>>('/api/v1/families/$familyId/invitations');
        final d = res.data!;
        return Invitation(url: d['url'] as String, shareText: d['shareText'] as String, whatsAppUrl: d['whatsAppUrl'] as String);
      });

  @override
  Future<InvitationPreview> preview(String token) => _call(() async {
        final res = await _api.dio.get<Map<String, dynamic>>('/api/v1/invitations/$token');
        final d = res.data!;
        return InvitationPreview(familyName: d['familyName'] as String, invitedBy: d['invitedBy'] as String, status: d['status'] as String);
      });

  @override
  Future<String> accept(String token) => _call(() async {
        final res = await _api.dio.post<Map<String, dynamic>>('/api/v1/invitations/$token/accept');
        return res.data!['familyId'] as String;
      });
}

final familyRepositoryProvider = Provider<FamilyRepository>((ref) => ApiFamilyRepository(ref.watch(apiClientProvider)));

final familyProvider = FutureProvider.family<FamilyDetails, String>((ref, id) => ref.watch(familyRepositoryProvider).get(id));

/// Extracts the token from a pasted invite link (https://…/i/{token}) or a bare token.
String parseInviteToken(String input) {
  final trimmed = input.trim();
  final uri = Uri.tryParse(trimmed);
  if (uri != null && uri.pathSegments.length >= 2 && uri.pathSegments[uri.pathSegments.length - 2] == 'i') {
    return uri.pathSegments.last;
  }
  return trimmed;
}
