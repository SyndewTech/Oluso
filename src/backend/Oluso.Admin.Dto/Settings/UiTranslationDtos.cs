namespace Oluso.Admin.Dto.Settings;

public class LocaleStringsEditorResponse
{
    public required string Locale { get; init; }
    public Dictionary<string, string> Defaults { get; init; } = new();
    public Dictionary<string, string> Overrides { get; init; } = new();
}

public class SaveLocaleOverridesRequest
{
    public Dictionary<string, string> Overrides { get; init; } = new();
}
