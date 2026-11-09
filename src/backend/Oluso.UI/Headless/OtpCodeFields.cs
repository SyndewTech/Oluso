using Oluso.Dto.Journeys;

namespace Oluso.UI.Headless;

/// <summary>Journey/_OtpCodeInput: one box per digit, posted as one value under the field name the view gives it.</summary>
internal static class OtpCodeFields
{
    public static JourneyFormField Describe(string name, int length, string label, string? hint = null)
    {
        var codeLength = length > 0 ? length : 6;
        return new JourneyFormField
        {
            Name = name,
            Type = "otp",
            Label = label,
            Hint = string.IsNullOrEmpty(hint) ? null : hint,
            Required = true,
            MinLength = codeLength,
            MaxLength = codeLength,
            Autocomplete = "one-time-code",
            Autofocus = true
        };
    }
}
