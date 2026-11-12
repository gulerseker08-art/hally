import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import 'capture_models.dart';

abstract interface class CaptureRepository {
  Future<CaptureResult> captureText(String familyId, String text, {String source = 'Text'});
  Future<CaptureResult> captureImage(String familyId, List<int> bytes, String fileName, String contentType, {required String source});
  Future<CaptureResult> retry(String familyId, String captureId);
  Future<List<Proposal>> pending(String familyId);
  Future<void> approve(String familyId, String proposalId, ProposalEdits edits);
  Future<void> reject(String familyId, String proposalId);
}

class ApiCaptureRepository implements CaptureRepository {
  ApiCaptureRepository(this._api);

  final ApiClient _api;

  Future<T> _call<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  Future<CaptureResult> _post(String familyId, FormData form) => _call(() async {
        final res = await _api.dio.post<Map<String, dynamic>>(
          '/api/v1/families/$familyId/captures',
          data: form,
          options: Options(receiveTimeout: const Duration(seconds: 90)),
        );
        return CaptureResult.fromJson(res.data!);
      });

  @override
  Future<CaptureResult> captureText(String familyId, String text, {String source = 'Text'}) =>
      _post(familyId, FormData.fromMap({'text': text, 'source': source}));

  @override
  Future<CaptureResult> captureImage(String familyId, List<int> bytes, String fileName, String contentType,
          {required String source}) =>
      _post(
        familyId,
        FormData.fromMap({
          'source': source,
          'file': MultipartFile.fromBytes(bytes, filename: fileName, contentType: DioMediaType.parse(contentType)),
        }),
      );

  @override
  Future<CaptureResult> retry(String familyId, String captureId) => _call(() async {
        final res = await _api.dio.post<Map<String, dynamic>>('/api/v1/families/$familyId/captures/$captureId/retry');
        return CaptureResult.fromJson(res.data!);
      });

  @override
  Future<List<Proposal>> pending(String familyId) => _call(() async {
        final res = await _api.dio.get<List<dynamic>>('/api/v1/families/$familyId/proposals', queryParameters: {'status': 'pending'});
        return [for (final p in res.data!) Proposal.fromJson(p as Map<String, dynamic>)];
      });

  @override
  Future<void> approve(String familyId, String proposalId, ProposalEdits edits) => _call(() async {
        await _api.dio.post<dynamic>('/api/v1/families/$familyId/proposals/$proposalId/approve', data: edits.toJson());
      });

  @override
  Future<void> reject(String familyId, String proposalId) => _call(() async {
        await _api.dio.post<dynamic>('/api/v1/families/$familyId/proposals/$proposalId/reject');
      });
}

final captureRepositoryProvider = Provider<CaptureRepository>((ref) => ApiCaptureRepository(ref.watch(apiClientProvider)));

/// Latest capture result handed from the capture sheet to the review screen.
final lastCaptureProvider = StateProvider<CaptureResult?>((ref) => null);
