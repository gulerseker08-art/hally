import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../auth/token_storage.dart';
import '../config/app_config.dart';

/// Error carrying the RFC 9457 problem `code` returned by the API.
class ApiException implements Exception {
  ApiException(this.status, this.code, this.message, {this.trigger});

  final int? status;
  final String? code;
  final String message;

  /// Set on 402 answers: why the family needs Family Plus (scan_limit, adult_limit, share_link_limit, advanced_reminder).
  final String? trigger;

  bool get needsPlus => status == 402;

  @override
  String toString() => 'ApiException($status, $code, $message)';
}

/// Dio client: attaches the bearer token, adds Idempotency-Key to POSTs, refreshes once on 401.
class ApiClient {
  ApiClient(this._storage, {Dio? dio, this.onSessionExpired})
      : dio = dio ?? Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl, connectTimeout: const Duration(seconds: 15))) {
    this.dio.interceptors.add(QueuedInterceptorsWrapper(onRequest: _onRequest, onError: _onError));
  }

  final Dio dio;
  final TokenStorage _storage;
  final void Function()? onSessionExpired;
  static const _uuid = Uuid();

  Future<void> _onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    final tokens = await _storage.read();
    if (tokens != null && options.extra['anonymous'] != true) {
      options.headers['Authorization'] = 'Bearer ${tokens.accessToken}';
    }
    if (options.method == 'POST') {
      options.headers.putIfAbsent('Idempotency-Key', () => _uuid.v4());
    }
    handler.next(options);
  }

  Future<void> _onError(DioException error, ErrorInterceptorHandler handler) async {
    final request = error.requestOptions;
    final isAuthCall = request.path.startsWith('/api/v1/auth/');
    if (error.response?.statusCode != 401 || isAuthCall || request.extra['retried'] == true) {
      return handler.next(error);
    }

    final tokens = await _storage.read();
    if (tokens == null) return handler.next(error);

    try {
      final res = await Dio(BaseOptions(baseUrl: dio.options.baseUrl))
          .post<Map<String, dynamic>>('/api/v1/auth/refresh', data: {'refreshToken': tokens.refreshToken});
      final body = res.data!;
      await _storage.write(StoredTokens(
        accessToken: body['accessToken'] as String,
        refreshToken: body['refreshToken'] as String,
      ));
      request.extra['retried'] = true;
      request.headers['Authorization'] = 'Bearer ${body['accessToken']}';
      handler.resolve(await dio.fetch<dynamic>(request));
    } on DioException {
      await _storage.clear();
      onSessionExpired?.call();
      handler.next(error);
    }
  }

  static ApiException toApiException(Object error) {
    if (error is DioException) {
      final data = error.response?.data;
      if (data is Map) {
        return ApiException(
          error.response?.statusCode,
          data['code'] as String?,
          (data['title'] ?? error.message ?? '').toString(),
          trigger: data['trigger'] as String?,
        );
      }
      return ApiException(error.response?.statusCode, null, error.message ?? 'network error');
    }
    return ApiException(null, null, error.toString());
  }
}

final apiClientProvider = Provider<ApiClient>((ref) => ApiClient(ref.watch(tokenStorageProvider)));
