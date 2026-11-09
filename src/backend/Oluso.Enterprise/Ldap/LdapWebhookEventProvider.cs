using Oluso.Core.Events;

namespace Oluso.Enterprise.Ldap;

public class LdapWebhookEventProvider : IWebhookEventProvider
{
    public string ProviderId => "ldap";
    public string DisplayName => "LDAP";

    public IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => new[]
    {
        new WebhookEventDefinition
        {
            EventType = "ldap.login_success",
            Category = "LDAP",
            DisplayName = "LDAP Authentication Success",
            Description = "Triggered when LDAP authentication succeeds",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "ldap.login_failed",
            Category = "LDAP",
            DisplayName = "LDAP Authentication Failed",
            Description = "Triggered when LDAP authentication fails",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "ldap.password_changed",
            Category = "LDAP",
            DisplayName = "LDAP Password Changed",
            Description = "Triggered when a user changes their directory password at sign-in",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "ldap.user_provisioned",
            Category = "LDAP",
            DisplayName = "LDAP User Provisioned",
            Description = "Triggered when a new user is provisioned from LDAP",
            ProviderId = ProviderId
        }
    };
}
