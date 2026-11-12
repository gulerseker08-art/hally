/// The bundled onboarding sample (a labelled, made-up school announcement). The backend keeps the same
/// text in backend/src/Hally.Api/Features/Demo/DemoSamples.cs and only receives the sample id.
abstract final class DemoSamples {
  static const turkishId = 'school-tr';
  static const englishId = 'school-en';

  static const turkish = 'Sevgili veliler,\n'
      'Cuma günü sınıfımız kırmızı giyiyor, lütfen çocuklarınıza kırmızı tişört giydirin.\n'
      'Haftaya salı fen dersi için herkes 10 kuru yaprak getirsin.\n'
      "Ayın 25'inde saat 18:30'da veli toplantımız var, katılımınızı rica ederim.\n"
      'Öğretmen Ayşe Hanım';

  static const english = 'Dear parents,\n'
      'On Friday our class is wearing red, so please send your child in a red T-shirt.\n'
      'Next Tuesday everyone should bring 10 dry leaves for science.\n'
      'Parent-teacher meeting on the 25th at 6:30pm. Hope to see you there!\n'
      'Ms. Johnson';

  static String idFor(String languageCode) => languageCode == 'en' ? englishId : turkishId;

  static String textFor(String languageCode) => languageCode == 'en' ? english : turkish;
}
