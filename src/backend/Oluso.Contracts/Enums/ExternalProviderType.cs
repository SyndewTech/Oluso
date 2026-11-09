using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalProviderType
{
    Google,
    Microsoft,
    Facebook,
    Apple,
    GitHub,
    LinkedIn,
    Twitter,
    Oidc,
    OAuth2,
    Saml2,
    Ldap
}
