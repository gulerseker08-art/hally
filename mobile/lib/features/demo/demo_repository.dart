import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../capture/capture_models.dart';

class DemoItem {
  const DemoItem({required this.isEvent, required this.payload});

  final bool isEvent;
  final ProposalPayload payload;
}

abstract interface class DemoRepository {
  Future<List<DemoItem>> scanSample(String sampleId, String locale);
  Future<List<DemoItem>> scanImage(List<int> bytes, String fileName, String contentType, String locale);
}

class ApiDemoRepository implements DemoRepository {
  ApiDemoRepository(this._api);

  final ApiClient _api;

  Future<List<DemoItem>> _scan(FormData form) async {
    try {
      final res = await _api.dio.post<Map<String, dynamic>>(
        '/api/v1/demo/scan',
        data: form,
        options: Options(extra: {'anonymous': true}, receiveTimeout: const Duration(seconds: 90)),
      );
      return [
        for (final i in res.data!['items'] as List)
          DemoItem(
            isEvent: (i as Map<String, dynamic>)['kind'] == 'CreateEvent',
            payload: ProposalPayload.fromJson(i['payload'] as Map<String, dynamic>),
          ),
      ];
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  @override
  Future<List<DemoItem>> scanSample(String sampleId, String locale) =>
      _scan(FormData.fromMap({'sampleId': sampleId, 'locale': locale}));

  @override
  Future<List<DemoItem>> scanImage(List<int> bytes, String fileName, String contentType, String locale) => _scan(FormData.fromMap({
        'locale': locale,
        'file': MultipartFile.fromBytes(bytes, filename: fileName, contentType: DioMediaType.parse(contentType)),
      }));
}

final demoRepositoryProvider = Provider<DemoRepository>((ref) => ApiDemoRepository(ref.watch(apiClientProvider)));

/// Text the user scanned in the demo. After sign-up and family creation it is captured for real,
/// so the proposals they saw land in "Waiting for you".
final pendingDemoTextProvider = StateProvider<String?>((ref) => null);
