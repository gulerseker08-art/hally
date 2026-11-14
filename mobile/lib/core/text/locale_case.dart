import 'dart:ui';

/// Locale-aware upper case. Dart's [String.toUpperCase] ignores the locale, so in Turkish and Azerbaijani
/// "i" would become "I" instead of "İ" (e.g. "PAZARTESI" instead of "PAZARTESİ").
String upperCaseFor(String text, Locale locale) {
  if (locale.languageCode == 'tr' || locale.languageCode == 'az') {
    return text.replaceAll('i', 'İ').replaceAll('ı', 'I').toUpperCase();
  }
  return text.toUpperCase();
}
