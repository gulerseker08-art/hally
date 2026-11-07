import 'dart:io' show Platform;

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../api/api_client.dart';
import '../config/app_config.dart';
import 'token_storage.dart';

class Me {
  const Me({required this.id, required this.displayName, required this.locale, required this.families});

  factory Me.fromJson(Map<String, dynamic> json) => Me(
        id: json['id'] as String,
        displayName: json['displayName'] as String,
        locale: json['locale'] as String,
        families: [
          for (final f in json['families'] as List) FamilyRef(id: f['id'] as String, name: f['name'] as String),
        ],
      );

  final String id;
  final String displayName;
  final String locale;
  final List<FamilyRef> families;

  FamilyRef? get primaryFamily => families.isEmpty ? null : families.first;
}

class FamilyRef {
  const FamilyRef({required this.id, required this.name});

  final String id;
  final String name;
}

/// Talks to /api/v1/auth and /api/v1/me. Native sign-in SDKs only produce an ID token;
/// the backend verifies it and issues Hally tokens.
class AuthRepository {
  AuthRepository(this._api, this._storage);

  final ApiClient _api;
  final TokenStorage _storage;

  bool get appleAvailable => !kIsWeb && (Platform.isIOS || Platform.isMacOS);

  Future<void> signInWithGoogle(String locale) async {
    final google = GoogleSignIn(
      scopes: const ['email', 'profile'],
      serverClientId: AppConfig.googleServerClientId.isEmpty ? null : AppConfig.googleServerClientId,
    );
    final account = await google.signIn();
    if (account == null) throw ApiException(null, 'auth.cancelled', 'cancelled');
    final idToken = (await account.authentication).idToken;
    if (idToken == null) throw ApiException(null, 'auth.no_id_token', 'Google returned no ID token');
    await _exchange('google', idToken, account.displayName, locale);
  }

  Future<void> signInWithApple(String locale) async {
    final credential = await SignInWithApple.getAppleIDCredential(
      scopes: [AppleIDAuthorizationScopes.email, AppleIDAuthorizationScopes.fullName],
    );
    final token = credential.identityToken;
    if (token == null) throw ApiException(null, 'auth.no_id_token', 'Apple returned no identity token');
    final name = [credential.givenName, credential.familyName].whereType<String>().join(' ').trim();
    await _exchange('apple', token, name.isEmpty ? null : name, locale);
  }

  /// Backend dev provider; only accepted by Development/Testing API environments.
  Future<void> signInWithDev(String email, String name, String locale) =>
      _exchange('dev', 'dev:$email:$name', name, locale);

  Future<void> _exchange(String provider, String idToken, String? displayName, String locale) async {
    try {
      final res = await _api.dio.post<Map<String, dynamic>>(
        '/api/v1/auth/$provider',
        data: {'idToken': idToken, 'displayName': displayName, 'locale': locale},
        options: _anonymous,
      );
      final tokens = res.data!['tokens'] as Map<String, dynamic>;
      await _storage.write(StoredTokens(
        accessToken: tokens['accessToken'] as String,
        refreshToken: tokens['refreshToken'] as String,
      ));
    } catch (e) {
      throw ApiClient.toApiException(e);
    }
  }

  Future<Me?> currentUser() async {
    if (await _storage.read() == null) return null;
    try {
      final res = await _api.dio.get<Map<String, dynamic>>('/api/v1/me');
      return Me.fromJson(res.data!);
    } catch (e) {
      final error = ApiClient.toApiException(e);
      if (error.status == 401) {
        await _storage.clear();
        return null;
      }
      throw error;
    }
  }

  Future<void> signOut() => _storage.clear();

  static final _anonymous = Options(extra: {'anonymous': true});
}

final authRepositoryProvider =
    Provider<AuthRepository>((ref) => AuthRepository(ref.watch(apiClientProvider), ref.watch(tokenStorageProvider)));

/// Current signed-in user (null when signed out). Invalidate after sign-in, family changes or sign-out.
final meProvider = FutureProvider<Me?>((ref) => ref.watch(authRepositoryProvider).currentUser());
