namespace Oluso.UI.ViewModels;

/// <summary>
/// Input for the shared Journey/_OtpCodeInput partial: how many digits, the form field the code
/// posts as, and whether a complete code submits the form on its own. Turn auto-submit off when the
/// form has other choices to make first (e.g. "remember this device").
/// </summary>
public sealed record OtpCodeInputViewModel(int Length = 6, string FieldName = "code", bool AutoSubmit = true);
