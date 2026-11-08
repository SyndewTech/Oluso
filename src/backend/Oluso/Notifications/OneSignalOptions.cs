namespace Oluso.Notifications;

/// <summary>
/// Platform-level OneSignal configuration. The mobile app is a single platform
/// app → one OneSignal app, so these are platform secrets (config section
/// "OneSignal"), not per-tenant. Push self-disables when AppId/RestApiKey are
/// unset, so the app runs fine before they're provided.
/// </summary>
public sealed class OneSignalOptions
{
    public string AppId { get; set; } = "";
    public string RestApiKey { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://api.onesignal.com";
    public int TimeoutSeconds { get; set; } = 15;

    public bool Enabled => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(RestApiKey);
}
