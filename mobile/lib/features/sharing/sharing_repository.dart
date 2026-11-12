import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';

class HelperLink {
  const HelperLink({required this.url, required this.shareText, required this.whatsAppUrl, required this.expiresAt});

  final String url;
  final String shareText;
  final String whatsAppUrl;
  final DateTime expiresAt;
}

class ActiveLink {
  const ActiveLink({required this.id, required this.kind, this.date, required this.expiresAt, required this.openCount});

  factory ActiveLink.fromJson(Map<String, dynamic> j) => ActiveLink(
        id: j['id'] as String,
        kind: j['kind'] as String,
        date: j['date'] as String?,
        expiresAt: DateTime.parse(j['expiresAt'] as String).toLocal(),
        openCount: j['openCount'] as int? ?? 0,
      );

  final String id;
  final String kind; // Task | Day
  final String? date;
  final DateTime expiresAt;
  final int openCount;
}

abstract interface class SharingRepository {
  Future<HelperLink> shareTask(String familyId, String taskId);
  Future<HelperLink> shareDay(String familyId, DateTime day);
  Future<List<ActiveLink>> active(String familyId);
  Future<void> revoke(String familyId, String linkId);
  Future<Uint8List> weeklyCard(String familyId, {required int offset, required bool includeChildNames});
}

class ApiSharingRepository implements SharingRepository {
  ApiSharingRepository(this._api);

  final ApiClient _api;

  Future<T> _guard<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  Future<HelperLink> _create(String familyId, Map<String, dynamic> body) => _guard(() async {
        final d = (await _api.dio.post<Map<String, dynamic>>('/api/v1/families/$familyId/share-links', data: body)).data!;
        return HelperLink(
          url: d['url'] as String,
          shareText: d['shareText'] as String,
          whatsAppUrl: d['whatsAppUrl'] as String,
          expiresAt: DateTime.parse(d['expiresAt'] as String).toLocal(),
        );
      });

  @override
  Future<HelperLink> shareTask(String familyId, String taskId) => _create(familyId, {'kind': 'Task', 'taskId': taskId});

  @override
  Future<HelperLink> shareDay(String familyId, DateTime day) => _create(familyId, {
        'kind': 'Day',
        'date': '${day.year.toString().padLeft(4, '0')}-${day.month.toString().padLeft(2, '0')}-${day.day.toString().padLeft(2, '0')}',
      });

  @override
  Future<List<ActiveLink>> active(String familyId) => _guard(() async {
        final res = await _api.dio.get<List<dynamic>>('/api/v1/families/$familyId/share-links');
        return [for (final l in res.data!) ActiveLink.fromJson(l as Map<String, dynamic>)];
      });

  @override
  Future<void> revoke(String familyId, String linkId) => _guard(() => _api.dio.delete<dynamic>('/api/v1/families/$familyId/share-links/$linkId'));

  @override
  Future<Uint8List> weeklyCard(String familyId, {required int offset, required bool includeChildNames}) => _guard(() async {
        final res = await _api.dio.get<List<int>>(
          '/api/v1/families/$familyId/weekly-summary/card.png',
          queryParameters: {'offset': offset, 'includeChildNames': includeChildNames},
          options: Options(responseType: ResponseType.bytes),
        );
        return Uint8List.fromList(res.data!);
      });
}

final sharingRepositoryProvider = Provider<SharingRepository>((ref) => ApiSharingRepository(ref.watch(apiClientProvider)));

final activeLinksProvider = FutureProvider.autoDispose.family<List<ActiveLink>, String>((ref, familyId) => ref.watch(sharingRepositoryProvider).active(familyId));
