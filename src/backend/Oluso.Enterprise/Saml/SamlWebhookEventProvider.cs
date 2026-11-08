using Oluso.Core.Events;

namespace Oluso.Enterprise.Saml;

public class SamlWebhookEventProvider : IWebhookEventProvider
{
    public string ProviderId => "saml";
    public string DisplayName => "SAML";

    public IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => new[]
    {
        new WebhookEventDefinition
        {
            EventType = "admin.saml_sp_created",
            Category = "Admin",
            DisplayName = "Admin SAML SP Created",
            Description = "Triggered when an admin creates a SAML Service Provider",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "admin.saml_sp_updated",
            Category = "Admin",
            DisplayName = "Admin SAML SP Updated",
            Description = "Triggered when an admin updates a SAML Service Provider",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "admin.saml_sp_deleted",
            Category = "Admin",
            DisplayName = "Admin SAML SP Deleted",
            Description = "Triggered when an admin deletes a SAML Service Provider",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "saml.sso_request_received",
            Category = "SAML",
            DisplayName = "SAML SSO Request Received",
            Description = "Triggered when a SAML SSO request is received",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "saml.assertion_issued",
            Category = "SAML",
            DisplayName = "SAML Assertion Issued",
            Description = "Triggered when a SAML assertion is issued",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "saml.sso_failed",
            Category = "SAML",
            DisplayName = "SAML SSO Failed",
            Description = "Triggered when SAML SSO fails",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "saml.logout_request_received",
            Category = "SAML",
            DisplayName = "SAML Logout Request Received",
            Description = "Triggered when a SAML logout request is received",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "saml.logout_completed",
            Category = "SAML",
            DisplayName = "SAML Logout Completed",
            Description = "Triggered when SAML logout is completed",
            ProviderId = ProviderId
        },
        new WebhookEventDefinition
        {
            EventType = "saml.sp_login",
            Category = "SAML",
            DisplayName = "SAML SP Login",
            Description = "Triggered when a user authenticates via SAML SP",
            ProviderId = ProviderId
        }
    };
}
