namespace Hally.Api.Features.Demo;

/// <summary>
/// The bundled onboarding sample: a labelled, made-up school announcement (not user data). The same text
/// is shown in the app (mobile/lib/features/demo/demo_samples.dart). Dates are relative on purpose so the
/// demo always lands in the coming days.
/// </summary>
public static class DemoSamples
{
    public const string Turkish = """
        Sevgili veliler,
        Cuma günü sınıfımız kırmızı giyiyor, lütfen çocuklarınıza kırmızı tişört giydirin.
        Haftaya salı fen dersi için herkes 10 kuru yaprak getirsin.
        Ayın 25'inde saat 18:30'da veli toplantımız var, katılımınızı rica ederim.
        Öğretmen Ayşe Hanım
        """;

    public const string English = """
        Dear parents,
        On Friday our class is wearing red, so please send your child in a red T-shirt.
        Next Tuesday everyone should bring 10 dry leaves for science.
        Parent-teacher meeting on the 25th at 6:30pm. Hope to see you there!
        Ms. Johnson
        """;

    public static string? Get(string? id) => id switch
    {
        "school-tr" => Turkish,
        "school-en" => English,
        _ => null,
    };
}
