import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';

class PrivacyInfo {
  const PrivacyInfo({required this.aiProviderNoTraining, this.aiProviderName, required this.originalsRetentionDays, this.contactEmail, this.policyUrl});

  factory PrivacyInfo.fromJson(Map<String, dynamic> j) => PrivacyInfo(
        aiProviderNoTraining: j['aiProviderNoTraining'] as bool? ?? false,
        aiProviderName: j['aiProviderName'] as String?,
        originalsRetentionDays: j['originalsRetentionDays'] as int? ?? 30,
        contactEmail: j['contactEmail'] as String?,
        policyUrl: j['policyUrl'] as String?,
      );

  /// True only when the operator confirmed the AI provider does not train on API data.
  final bool aiProviderNoTraining;
  final String? aiProviderName;
  final int originalsRetentionDays;
  final String? contactEmail;
  final String? policyUrl;
}

abstract interface class PrivacyRepository {
  Future<PrivacyInfo> info();
  Future<Uint8List> export();
  Future<void> leaveFamily(String familyId);
  Future<void> deleteAccount();
}

class ApiPrivacyRepository implements PrivacyRepository {
  ApiPrivacyRepository(this._api);

  final ApiClient _api;

  Future<T> _guard<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  @override
  Future<PrivacyInfo> info() =>
      _guard(() async => PrivacyInfo.fromJson((await _api.dio.get<Map<String, dynamic>>('/api/v1/privacy')).data!));

  @override
  Future<Uint8List> export() => _guard(() async {
        final res = await _api.dio.get<List<int>>('/api/v1/me/export', options: Options(responseType: ResponseType.bytes));
        return Uint8List.fromList(res.data!);
      });

  @override
  Future<void> leaveFamily(String familyId) => _guard(() => _api.dio.post<void>('/api/v1/families/$familyId/leave'));

  @override
  Future<void> deleteAccount() => _guard(() => _api.dio.delete<void>('/api/v1/me', queryParameters: {'confirm': true}));
}

final privacyRepositoryProvider = Provider<PrivacyRepository>((ref) => ApiPrivacyRepository(ref.watch(apiClientProvider)));

final privacyInfoProvider = FutureProvider<PrivacyInfo>((ref) => ref.watch(privacyRepositoryProvider).info());
