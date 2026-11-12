import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../capture/capture_models.dart';

class ChatEntry {
  const ChatEntry({required this.id, required this.fromUser, required this.text, this.proposals = const [], this.fallback = false});

  final String id;
  final bool fromUser;
  final String text;
  final List<Proposal> proposals;
  final bool fallback;
}

class AssistantReply {
  const AssistantReply({required this.conversationId, required this.reply});

  final String conversationId;
  final ChatEntry reply;
}

abstract interface class AssistantRepository {
  Future<AssistantReply> send(String familyId, String text, {String? conversationId});
}

class ApiAssistantRepository implements AssistantRepository {
  ApiAssistantRepository(this._api);

  final ApiClient _api;

  @override
  Future<AssistantReply> send(String familyId, String text, {String? conversationId}) async {
    try {
      final d = (await _api.dio.post<Map<String, dynamic>>('/api/v1/families/$familyId/assistant/messages',
              data: {'conversationId': conversationId, 'text': text}))
          .data!;
      final r = d['reply'] as Map<String, dynamic>;
      return AssistantReply(
        conversationId: d['conversationId'] as String,
        reply: ChatEntry(
          id: r['id'] as String,
          fromUser: false,
          text: r['content'] as String,
          fallback: r['fallback'] as bool? ?? false,
          proposals: [for (final p in d['proposals'] as List) Proposal.fromJson(p as Map<String, dynamic>)],
        ),
      );
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }
}

final assistantRepositoryProvider = Provider<AssistantRepository>((ref) => ApiAssistantRepository(ref.watch(apiClientProvider)));
