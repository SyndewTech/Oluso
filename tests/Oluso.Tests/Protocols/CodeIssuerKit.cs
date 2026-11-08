using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Protocols;
using Oluso.Protocols.Services;
using Oluso.Protocols.Validation;

namespace Oluso.Tests.Protocols;

/// <summary>
/// A real <see cref="AuthorizationCodeIssuer"/> (real scope and PKCE validators) over in-memory stores, for
/// tests of the endpoints that return a code to a page.
/// </summary>
internal sealed class CodeIssuerKit
{
    public const string AppOrigin = "https://app.acme.test";
    public const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    public const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    public List<AuthorizationCode> Codes { get; } = new();
    public List<SignInAudit> SignIns { get; } = new();
    public Mock<IClientStore> Clients { get; } = new();
    public Mock<IOlusoUserService> Users { get; } = new();
    public Mock<IConsentStore> Consents { get; } = new();
    public Mock<ITenantContext> Tenant { get; } = new();
    public Mock<IAuthorizationCodeStore> CodeStore { get; } = new();
    public TenantProtocolSettings Protocol { get; } = new();

    public CodeIssuerKit()
    {
        Clients.Setup(c => c.FindClientByIdAsync("spa", It.IsAny<CancellationToken>())).ReturnsAsync(() => Spa);
        Users.Setup(u => u.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new OlusoUserInfo { Id = id, Username = id, Email = $"{id}@acme.test", TenantId = "acme" });
        Tenant.Setup(t => t.TenantId).Returns("acme");
        Tenant.Setup(t => t.HasTenant).Returns(true);
        CodeStore.Setup(c => c.StoreAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationCode, CancellationToken>((code, _) => Codes.Add(code))
            .Returns(Task.CompletedTask);
        CodeStore.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => Codes.FirstOrDefault(c => c.Code == code));
    }

    /// <summary>The SPA client: authorization code + PKCE, openid/profile/email, registered on <see cref="AppOrigin"/>.</summary>
    public Client Spa { get; set; } = NewSpa();

    public static Client NewSpa() => new()
    {
        ClientId = "spa",
        Enabled = true,
        RequireClientSecret = false,
        AllowedGrantTypes = new List<ClientGrantType> { new() { GrantType = "authorization_code" } },
        AllowedScopes = new List<ClientScope> { new() { Scope = "openid" }, new() { Scope = "profile" }, new() { Scope = "email" } },
        AllowedCorsOrigins = new List<ClientCorsOrigin> { new() { Origin = AppOrigin } }
    };

    public AuthorizationCodeIssuer Issuer
    {
        get
        {
            var resources = new Mock<IResourceStore>();
            resources.Setup(r => r.GetAllIdentityResourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdentityResource>());
            resources.Setup(r => r.GetAllApiScopesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ApiScope>());
            var settings = new Mock<ITenantSettingsProvider>();
            settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Protocol);
            var auditor = new Mock<ISignInAuditor>();
            auditor.Setup(a => a.RecordSuccessAsync(It.IsAny<SignInAudit>(), It.IsAny<CancellationToken>()))
                .Callback<SignInAudit, CancellationToken>((audit, _) => SignIns.Add(audit))
                .Returns(Task.CompletedTask);

            return new AuthorizationCodeIssuer(
                Clients.Object,
                new ScopeValidator(resources.Object),
                new PkceValidator(),
                settings.Object,
                Consents.Object,
                CodeStore.Object,
                Users.Object,
                Tenant.Object,
                Mock.Of<IClientUsageRecorder>(),
                ClientUserAccessKit.For(Mock.Of<IProfileService>(), Users.Object),
                NullLogger<AuthorizationCodeIssuer>.Instance,
                auditor.Object);
        }
    }
}
