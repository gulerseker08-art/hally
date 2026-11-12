class ProposalPayload {
  const ProposalPayload({
    required this.title,
    this.notes,
    this.date,
    this.time,
    this.location,
    this.childId,
    this.assigneeUserId,
    this.needsInfo = const [],
    this.confidence = 'medium',
    this.sourceQuote,
  });

  factory ProposalPayload.fromJson(Map<String, dynamic> j) => ProposalPayload(
        title: j['title'] as String,
        notes: j['notes'] as String?,
        date: j['date'] as String?,
        time: j['time'] as String?,
        location: j['location'] as String?,
        childId: j['childId'] as String?,
        assigneeUserId: j['assigneeUserId'] as String?,
        needsInfo: [for (final n in (j['needsInfo'] as List? ?? const [])) n as String],
        confidence: (j['confidence'] as String?) ?? 'medium',
        sourceQuote: j['sourceQuote'] as String?,
      );

  final String title;
  final String? notes;
  final String? date;
  final String? time;
  final String? location;
  final String? childId;
  final String? assigneeUserId;
  final List<String> needsInfo;
  final String confidence;
  final String? sourceQuote;
}

class Proposal {
  const Proposal({required this.id, required this.kind, required this.status, required this.payload});

  factory Proposal.fromJson(Map<String, dynamic> j) => Proposal(
        id: j['id'] as String,
        kind: j['kind'] as String,
        status: j['status'] as String,
        payload: ProposalPayload.fromJson(j['payload'] as Map<String, dynamic>),
      );

  final String id;
  final String kind;
  final String status;
  final ProposalPayload payload;

  bool get isEvent => kind == 'CreateEvent';
}

class CaptureResult {
  const CaptureResult({
    required this.id,
    required this.status,
    required this.failureCode,
    required this.proposals,
    required this.duplicatesSkipped,
  });

  factory CaptureResult.fromJson(Map<String, dynamic> j) => CaptureResult(
        id: j['id'] as String,
        status: j['status'] as String,
        failureCode: j['failureCode'] as String?,
        proposals: [for (final p in j['proposals'] as List) Proposal.fromJson(p as Map<String, dynamic>)],
        duplicatesSkipped: (j['duplicatesSkipped'] as num?)?.toInt() ?? 0,
      );

  final String id;
  final String status;
  final String? failureCode;
  final List<Proposal> proposals;
  final int duplicatesSkipped;

  bool get failed => status == 'Failed';
}

/// User edits applied when approving a proposal.
class ProposalEdits {
  const ProposalEdits({this.title, this.date, this.time, this.assigneeUserId});

  final String? title;
  final String? date;
  final String? time;
  final String? assigneeUserId;

  Map<String, dynamic> toJson() => {
        if (title != null) 'title': title,
        if (date != null) 'date': date,
        if (time != null) 'time': time,
        if (assigneeUserId != null) 'assigneeUserId': assigneeUserId,
      };
}
