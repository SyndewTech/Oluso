namespace Oluso.Core.Locale;

/// <summary>
/// One currency the platform can price, invoice, or pay in.
/// </summary>
/// <param name="Code">ISO 4217 alphabetic code.</param>
/// <param name="Name">English display name.</param>
/// <param name="Symbol">Symbol shown next to amounts; the code itself where no symbol is in common use.</param>
/// <param name="DecimalPlaces">ISO 4217 minor units (0 for JPY, 3 for KWD, and so on).</param>
/// <param name="Common">Listed first in pickers as a frequently used currency.</param>
public record CurrencyInfo(string Code, string Name, string Symbol, int DecimalPlaces = 2, bool Common = false);

/// <summary>
/// The platform's single currency list: every active ISO 4217 currency, with the
/// frequently used ones first. Served by <c>GET api/locale/currencies</c>; UIs read
/// it through the shared currency catalog instead of keeping their own copies.
/// </summary>
public static class SupportedCurrencies
{
    /// <summary>Frequently used currencies, in picker order.</summary>
    public static readonly IReadOnlyList<CurrencyInfo> Common = new CurrencyInfo[]
    {
        new("USD", "US Dollar", "$", 2, Common: true),
        new("EUR", "Euro", "€", 2, Common: true),
        new("GBP", "British Pound", "£", 2, Common: true),
        new("NGN", "Nigerian Naira", "₦", 2, Common: true),
        new("INR", "Indian Rupee", "₹", 2, Common: true),
        new("CAD", "Canadian Dollar", "CA$", 2, Common: true),
        new("AUD", "Australian Dollar", "A$", 2, Common: true),
        new("SGD", "Singapore Dollar", "S$", 2, Common: true),
        new("AED", "UAE Dirham", "د.إ", 2, Common: true),
        new("ZAR", "South African Rand", "R", 2, Common: true),
        new("KES", "Kenyan Shilling", "KSh", 2, Common: true),
        new("GHS", "Ghanaian Cedi", "₵", 2, Common: true),
        new("JPY", "Japanese Yen", "¥", 0, Common: true),
        new("CNY", "Chinese Yuan", "¥", 2, Common: true),
        new("BRL", "Brazilian Real", "R$", 2, Common: true),
        new("MXN", "Mexican Peso", "MX$", 2, Common: true),
    };

    private static readonly IReadOnlyList<CurrencyInfo> Others = new CurrencyInfo[]
    {
        new("AFN", "Afghan Afghani", "؋"),
        new("ALL", "Albanian Lek", "L"),
        new("AMD", "Armenian Dram", "֏"),
        new("ANG", "Netherlands Antillean Guilder", "ƒ"),
        new("AOA", "Angolan Kwanza", "Kz"),
        new("ARS", "Argentine Peso", "$"),
        new("AWG", "Aruban Florin", "ƒ"),
        new("AZN", "Azerbaijani Manat", "₼"),
        new("BAM", "Bosnia and Herzegovina Convertible Mark", "KM"),
        new("BBD", "Barbadian Dollar", "$"),
        new("BDT", "Bangladeshi Taka", "৳"),
        new("BGN", "Bulgarian Lev", "лв"),
        new("BHD", "Bahraini Dinar", ".د.ب", 3),
        new("BIF", "Burundian Franc", "FBu", 0),
        new("BMD", "Bermudian Dollar", "$"),
        new("BND", "Brunei Dollar", "$"),
        new("BOB", "Bolivian Boliviano", "Bs."),
        new("BSD", "Bahamian Dollar", "$"),
        new("BTN", "Bhutanese Ngultrum", "Nu."),
        new("BWP", "Botswana Pula", "P"),
        new("BYN", "Belarusian Ruble", "Br"),
        new("BZD", "Belize Dollar", "BZ$"),
        new("CDF", "Congolese Franc", "FC"),
        new("CHF", "Swiss Franc", "CHF"),
        new("CLP", "Chilean Peso", "$", 0),
        new("COP", "Colombian Peso", "$"),
        new("CRC", "Costa Rican Colón", "₡"),
        new("CUP", "Cuban Peso", "$"),
        new("CVE", "Cape Verdean Escudo", "$"),
        new("CZK", "Czech Koruna", "Kč"),
        new("DJF", "Djiboutian Franc", "Fdj", 0),
        new("DKK", "Danish Krone", "kr"),
        new("DOP", "Dominican Peso", "RD$"),
        new("DZD", "Algerian Dinar", "د.ج"),
        new("EGP", "Egyptian Pound", "E£"),
        new("ERN", "Eritrean Nakfa", "Nfk"),
        new("ETB", "Ethiopian Birr", "Br"),
        new("FJD", "Fijian Dollar", "$"),
        new("FKP", "Falkland Islands Pound", "£"),
        new("GEL", "Georgian Lari", "₾"),
        new("GIP", "Gibraltar Pound", "£"),
        new("GMD", "Gambian Dalasi", "D"),
        new("GNF", "Guinean Franc", "FG", 0),
        new("GTQ", "Guatemalan Quetzal", "Q"),
        new("GYD", "Guyanese Dollar", "$"),
        new("HKD", "Hong Kong Dollar", "HK$"),
        new("HNL", "Honduran Lempira", "L"),
        new("HTG", "Haitian Gourde", "G"),
        new("HUF", "Hungarian Forint", "Ft"),
        new("IDR", "Indonesian Rupiah", "Rp"),
        new("ILS", "Israeli New Shekel", "₪"),
        new("IQD", "Iraqi Dinar", "ع.د", 3),
        new("IRR", "Iranian Rial", "﷼"),
        new("ISK", "Icelandic Króna", "kr", 0),
        new("JMD", "Jamaican Dollar", "J$"),
        new("JOD", "Jordanian Dinar", "د.ا", 3),
        new("KGS", "Kyrgyzstani Som", "с"),
        new("KHR", "Cambodian Riel", "៛"),
        new("KMF", "Comorian Franc", "CF", 0),
        new("KPW", "North Korean Won", "₩"),
        new("KRW", "South Korean Won", "₩", 0),
        new("KWD", "Kuwaiti Dinar", "د.ك", 3),
        new("KYD", "Cayman Islands Dollar", "$"),
        new("KZT", "Kazakhstani Tenge", "₸"),
        new("LAK", "Lao Kip", "₭"),
        new("LBP", "Lebanese Pound", "ل.ل"),
        new("LKR", "Sri Lankan Rupee", "Rs"),
        new("LRD", "Liberian Dollar", "$"),
        new("LSL", "Lesotho Loti", "L"),
        new("LYD", "Libyan Dinar", "ل.د", 3),
        new("MAD", "Moroccan Dirham", "د.م."),
        new("MDL", "Moldovan Leu", "L"),
        new("MGA", "Malagasy Ariary", "Ar"),
        new("MKD", "Macedonian Denar", "ден"),
        new("MMK", "Myanmar Kyat", "K"),
        new("MNT", "Mongolian Tögrög", "₮"),
        new("MOP", "Macanese Pataca", "MOP$"),
        new("MRU", "Mauritanian Ouguiya", "UM"),
        new("MUR", "Mauritian Rupee", "₨"),
        new("MVR", "Maldivian Rufiyaa", "Rf"),
        new("MWK", "Malawian Kwacha", "MK"),
        new("MYR", "Malaysian Ringgit", "RM"),
        new("MZN", "Mozambican Metical", "MT"),
        new("NAD", "Namibian Dollar", "$"),
        new("NIO", "Nicaraguan Córdoba", "C$"),
        new("NOK", "Norwegian Krone", "kr"),
        new("NPR", "Nepalese Rupee", "₨"),
        new("NZD", "New Zealand Dollar", "NZ$"),
        new("OMR", "Omani Rial", "ر.ع.", 3),
        new("PAB", "Panamanian Balboa", "B/."),
        new("PEN", "Peruvian Sol", "S/"),
        new("PGK", "Papua New Guinean Kina", "K"),
        new("PHP", "Philippine Peso", "₱"),
        new("PKR", "Pakistani Rupee", "₨"),
        new("PLN", "Polish Złoty", "zł"),
        new("PYG", "Paraguayan Guaraní", "₲", 0),
        new("QAR", "Qatari Riyal", "ر.ق"),
        new("RON", "Romanian Leu", "lei"),
        new("RSD", "Serbian Dinar", "дин"),
        new("RUB", "Russian Ruble", "₽"),
        new("RWF", "Rwandan Franc", "FRw", 0),
        new("SAR", "Saudi Riyal", "﷼"),
        new("SBD", "Solomon Islands Dollar", "$"),
        new("SCR", "Seychellois Rupee", "₨"),
        new("SDG", "Sudanese Pound", "ج.س."),
        new("SEK", "Swedish Krona", "kr"),
        new("SHP", "Saint Helena Pound", "£"),
        new("SLE", "Sierra Leonean Leone", "Le"),
        new("SOS", "Somali Shilling", "S"),
        new("SRD", "Surinamese Dollar", "$"),
        new("SSP", "South Sudanese Pound", "£"),
        new("STN", "São Tomé and Príncipe Dobra", "Db"),
        new("SVC", "Salvadoran Colón", "$"),
        new("SYP", "Syrian Pound", "£"),
        new("SZL", "Swazi Lilangeni", "E"),
        new("THB", "Thai Baht", "฿"),
        new("TJS", "Tajikistani Somoni", "SM"),
        new("TMT", "Turkmenistani Manat", "m"),
        new("TND", "Tunisian Dinar", "د.ت", 3),
        new("TOP", "Tongan Paʻanga", "T$"),
        new("TRY", "Turkish Lira", "₺"),
        new("TTD", "Trinidad and Tobago Dollar", "TT$"),
        new("TWD", "New Taiwan Dollar", "NT$"),
        new("TZS", "Tanzanian Shilling", "TSh"),
        new("UAH", "Ukrainian Hryvnia", "₴"),
        new("UGX", "Ugandan Shilling", "USh", 0),
        new("UYU", "Uruguayan Peso", "$U"),
        new("UZS", "Uzbekistani Som", "so'm"),
        new("VES", "Venezuelan Bolívar", "Bs."),
        new("VND", "Vietnamese Đồng", "₫", 0),
        new("VUV", "Vanuatu Vatu", "VT", 0),
        new("WST", "Samoan Tālā", "WS$"),
        new("XAF", "Central African CFA Franc", "FCFA", 0),
        new("XCD", "East Caribbean Dollar", "EC$"),
        new("XOF", "West African CFA Franc", "CFA", 0),
        new("XPF", "CFP Franc", "₣", 0),
        new("YER", "Yemeni Rial", "﷼"),
        new("ZMW", "Zambian Kwacha", "ZK"),
        new("ZWG", "Zimbabwe Gold", "ZiG"),
    };

    /// <summary>All currencies: the common ones first, then the rest by code.</summary>
    public static readonly IReadOnlyList<CurrencyInfo> All = Common.Concat(Others).ToList();

    private static readonly Dictionary<string, CurrencyInfo> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public static CurrencyInfo? GetByCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code.Trim(), out var currency) ? currency : null;

    public static bool IsSupported(string code) => GetByCode(code) != null;
}
