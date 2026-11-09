namespace Oluso.Core.Locale;

public record LocaleInfo(string Code, string Name, string DateFormat, string NumberFormat);

public static class SupportedLocales
{
    public static readonly IReadOnlyList<LocaleInfo> All = new[]
    {
        new LocaleInfo("en-US", "English (US)", "MM/DD/YYYY", "1,234.56"),
        new LocaleInfo("en-GB", "English (UK)", "DD/MM/YYYY", "1,234.56"),
        new LocaleInfo("en-NG", "English (Nigeria)", "DD/MM/YYYY", "1,234.56"),
        new LocaleInfo("en-IN", "English (India)", "DD/MM/YYYY", "1,23,456.78"),
        new LocaleInfo("en-AU", "English (Australia)", "DD/MM/YYYY", "1,234.56"),
        new LocaleInfo("en-CA", "English (Canada)", "YYYY-MM-DD", "1,234.56"),
        new LocaleInfo("en-SG", "English (Singapore)", "DD/MM/YYYY", "1,234.56"),
        new LocaleInfo("en-ZA", "English (South Africa)", "YYYY/MM/DD", "1 234,56"),
        new LocaleInfo("en-KE", "English (Kenya)", "DD/MM/YYYY", "1,234.56"),
        new LocaleInfo("fr-FR", "French (France)", "DD/MM/YYYY", "1 234,56"),
        new LocaleInfo("de-DE", "German (Germany)", "DD.MM.YYYY", "1.234,56"),
        new LocaleInfo("pt-BR", "Portuguese (Brazil)", "DD/MM/YYYY", "1.234,56"),
        new LocaleInfo("ar-AE", "Arabic (UAE)", "DD/MM/YYYY", "1,234.56"),
    };
}
