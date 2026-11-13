/// Single place for brand and environment values. The product name is not final,
/// so never hard-code it elsewhere.
class AppConfig {
  const AppConfig._();

  static const appName = String.fromEnvironment('HALLY_APP_NAME', defaultValue: 'Hally');

  /// Backend base URL. Android emulators reach the host machine at 10.0.2.2.
  static const apiBaseUrl = String.fromEnvironment('HALLY_API_URL', defaultValue: 'http://localhost:8080');

  /// Google OAuth web client id used as serverClientId so the backend receives an ID token.
  static const googleServerClientId = String.fromEnvironment('HALLY_GOOGLE_SERVER_CLIENT_ID');

  /// Shows the developer sign-in (backend dev provider). Never enable for store builds.
  static const enableDevSignIn = bool.fromEnvironment('HALLY_DEV_SIGN_IN', defaultValue: true);

  /// development | beta | production. Tags error reports.
  static const environment = String.fromEnvironment('HALLY_ENV', defaultValue: 'development');

  /// Sentry DSN (not a secret, but environment-specific). Empty = error reporting off.
  static const sentryDsn = String.fromEnvironment('SENTRY_DSN');
}
