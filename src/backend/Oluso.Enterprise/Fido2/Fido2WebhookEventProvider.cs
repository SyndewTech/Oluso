using Oluso.Core.Events;

namespace Oluso.Enterprise.Fido2;

/// <summary>
/// Provides FIDO2/WebAuthn-specific webhook events.
/// Registered automatically when AddFido2() is called.
/// </summary>
public class Fido2WebhookEventProvider : IWebhookEventProvider
{
    public string ProviderId => "fido2";
    public string DisplayName => "FIDO2/Passkeys";

    public IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => new[]
    {
        new WebhookEventDefinition
        {
            EventType = Fido2WebhookEvents.PasskeyRegistered,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Passkey Registered",
            Description = "Triggered when a user registers a new passkey or security key",
            EnabledByDefault = true
        },
        new WebhookEventDefinition
        {
            EventType = Fido2WebhookEvents.PasskeyDeleted,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Passkey Deleted",
            Description = "Triggered when a user removes a passkey or security key"
        },
        new WebhookEventDefinition
        {
            EventType = Fido2WebhookEvents.PasskeyAuthenticated,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Passkey Authentication",
            Description = "Triggered when a user authenticates using a passkey"
        },
        new WebhookEventDefinition
        {
            EventType = Fido2WebhookEvents.PasskeyAuthenticationFailed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Passkey Authentication Failed",
            Description = "Triggered when passkey authentication fails"
        },
        new WebhookEventDefinition
        {
            EventType = Fido2WebhookEvents.PasskeyRenamed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Passkey Renamed",
            Description = "Triggered when a user renames their passkey"
        }
    };
}

/// <summary>
/// FIDO2/Passkey webhook event type constants
/// </summary>
public static class Fido2WebhookEvents
{
    public const string PasskeyRegistered = "passkey.registered";
    public const string PasskeyDeleted = "passkey.deleted";
    public const string PasskeyAuthenticated = "passkey.authenticated";
    public const string PasskeyAuthenticationFailed = "passkey.authentication_failed";
    public const string PasskeyRenamed = "passkey.renamed";
}
