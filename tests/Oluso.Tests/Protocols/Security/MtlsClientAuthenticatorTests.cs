using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Protocols.Mtls;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// Tests for mTLS (RFC 8705) client authentication via ClientAuthenticator.TryMtlsClientAuthAsync.
/// Covers tls_client_auth (PKI-validated) and self_signed_tls_client_auth (thumbprint-matched).
/// </summary>
public class MtlsClientAuthenticatorTests : IDisposable
{
    private readonly Mock<IClientStore> _clientStore;
    private readonly Mock<IIssuerResolver> _issuerResolver;
    private readonly Mock<IDistributedCache> _cache;
    private readonly Mock<IClientCertificateExtractor> _certExtractor;
    private readonly MtlsOptions _mtlsOptions;
    private readonly ClientAuthenticator _authenticator;

    private readonly X509Certificate2 _clientCert;
    private readonly string _testClientId = "mtls-client";

    public MtlsClientAuthenticatorTests()
    {
        _clientStore = new Mock<IClientStore>();
        _issuerResolver = new Mock<IIssuerResolver>();
        _issuerResolver.Setup(x => x.GetIssuerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://auth.example.com");

        _cache = new Mock<IDistributedCache>();
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _certExtractor = new Mock<IClientCertificateExtractor>();

        _mtlsOptions = new MtlsOptions { Enabled = true };

        var endpointConfig = Options.Create(new OidcEndpointConfiguration
        {
            TokenEndpoint = "/connect/token"
        });

        _authenticator = new ClientAuthenticator(
            _clientStore.Object,
            _issuerResolver.Object,
            endpointConfig,
            _cache.Object,
            _certExtractor.Object,
            _mtlsOptions,
            NullLogger<ClientAuthenticator>.Instance);

        _clientCert = CreateSelfSignedCertificate("CN=mtls-client.example.com");
    }

    public void Dispose()
    {
        _clientCert.Dispose();
    }

    #region tls_client_auth - Subject DN Match

    [Fact]
    public async Task MtlsAuth_ValidCertMatchingSubjectDn_ReturnsSuccessWithTlsClientAuth()
    {
        SetupCertExtractor(_clientCert);
        var client = CreatePkiClient(tlsClientAuthSubjectDn: _clientCert.Subject);
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.TlsClientAuth);
    }

    #endregion

    #region tls_client_auth - SAN DNS Match

    [Fact]
    public async Task MtlsAuth_ValidCertMatchingSanDns_ReturnsSuccess()
    {
        const string sanDns = "client.example.com";
        using var certWithSan = CreateCertificateWithSanDns("CN=mtls-client", sanDns);
        SetupCertExtractor(certWithSan);
        var client = CreatePkiClient(tlsClientAuthSanDns: sanDns);
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Method.Should().Be(ClientAuthenticationMethod.TlsClientAuth);
    }

    #endregion

    #region tls_client_auth - Wrong Subject DN

    [Fact]
    public async Task MtlsAuth_WrongSubjectDn_ReturnsInvalidClient()
    {
        SetupCertExtractor(_clientCert);
        var client = CreatePkiClient(tlsClientAuthSubjectDn: "CN=completely-different.example.com");
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("does not match");
    }

    #endregion

    #region tls_client_auth - No Certificate Identity Registered

    [Fact]
    public async Task MtlsAuth_NoCertificateIdentityRegistered_ReturnsInvalidClient()
    {
        SetupCertExtractor(_clientCert);
        // Client with tls_client_auth but no DN/SAN properties set
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            TokenEndpointAuthMethod = "tls_client_auth"
        };
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("no certificate identity registered");
    }

    #endregion

    #region Client Not Configured for mTLS

    [Fact]
    public async Task MtlsAuth_ClientNotConfiguredForMtls_ReturnsNull_FallsThrough()
    {
        SetupCertExtractor(_clientCert);
        // TokenEndpointAuthMethod is null (legacy client)
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = false,
            TokenEndpointAuthMethod = null
        };
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        // mTLS handler returns null, so the authenticator falls through to public client
        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        // Should succeed as public client (fall-through), NOT as mTLS
        result.IsValid.Should().BeTrue();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    #endregion

    #region mTLS Disabled in Options

    [Fact]
    public async Task MtlsAuth_MtlsDisabledInOptions_ReturnsNull_FallsThrough()
    {
        // Create a separate authenticator with mTLS disabled
        var disabledOptions = new MtlsOptions { Enabled = false };
        var authenticator = new ClientAuthenticator(
            _clientStore.Object,
            _issuerResolver.Object,
            Options.Create(new OidcEndpointConfiguration { TokenEndpoint = "/connect/token" }),
            _cache.Object,
            _certExtractor.Object,
            disabledOptions,
            NullLogger<ClientAuthenticator>.Instance);

        SetupCertExtractor(_clientCert);
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = false,
            TokenEndpointAuthMethod = "tls_client_auth",
            TlsClientAuthSubjectDn = _clientCert.Subject
        };
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await authenticator.AuthenticateAsync(request, CancellationToken.None);

        // mTLS path is skipped, falls through to public client
        result.IsValid.Should().BeTrue();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    #endregion

    #region self_signed_tls_client_auth - Thumbprint Match

    [Fact]
    public async Task MtlsSelfSigned_ValidCertMatchingStoredThumbprint_ReturnsSuccess()
    {
        SetupCertExtractor(_clientCert);
        var client = CreateSelfSignedClient(_clientCert);
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.SelfSignedTlsClientAuth);
    }

    #endregion

    #region self_signed_tls_client_auth - Wrong Cert

    [Fact]
    public async Task MtlsSelfSigned_WrongCert_ReturnsInvalidClient()
    {
        using var wrongCert = CreateSelfSignedCertificate("CN=wrong-client.example.com");
        SetupCertExtractor(wrongCert);
        // Store the original test cert, but present a different one
        var client = CreateSelfSignedClient(_clientCert);
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("does not match");
    }

    #endregion

    #region self_signed_tls_client_auth - Expired Secret

    [Fact]
    public async Task MtlsSelfSigned_ExpiredSecret_SkipsExpiredAndFails()
    {
        SetupCertExtractor(_clientCert);
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            TokenEndpointAuthMethod = "self_signed_tls_client_auth",
            ClientSecrets = new List<ClientSecret>
            {
                new()
                {
                    Type = "X509CertificateBase64",
                    Value = Convert.ToBase64String(_clientCert.RawData),
                    Expiration = DateTime.UtcNow.AddDays(-1) // Expired
                }
            }
        };
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task MtlsAuth_NoCertificatePresented_ReturnsNull_FallsThrough()
    {
        // No certificate from the extractor
        _certExtractor.Setup(x => x.Extract(It.IsAny<HttpContext>()))
            .Returns((X509Certificate2?)null);

        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = false,
            TokenEndpointAuthMethod = "tls_client_auth",
            TlsClientAuthSubjectDn = "CN=test"
        };
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        // Falls through to public client
        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    [Fact]
    public async Task MtlsAuth_DisabledClient_ReturnsInvalidClient()
    {
        SetupCertExtractor(_clientCert);
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = false,
            TokenEndpointAuthMethod = "tls_client_auth",
            TlsClientAuthSubjectDn = _clientCert.Subject
        };
        SetupClientStore(client);

        var request = CreateMtlsRequest(_testClientId);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("disabled");
    }

    [Fact]
    public async Task MtlsAuth_UnknownClient_FallsThrough()
    {
        SetupCertExtractor(_clientCert);
        _clientStore.Setup(x => x.FindClientByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var request = CreateMtlsRequest("unknown-client");

        // mTLS returns null for unknown client; fall-through reaches TryPublicClient which returns "Unknown client"
        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    #endregion

    #region Helpers

    private void SetupCertExtractor(X509Certificate2 cert)
    {
        _certExtractor.Setup(x => x.Extract(It.IsAny<HttpContext>()))
            .Returns(cert);
    }

    private void SetupClientStore(Client client)
    {
        _clientStore.Setup(x => x.FindClientByIdAsync(client.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private Client CreatePkiClient(
        string? tlsClientAuthSubjectDn = null,
        string? tlsClientAuthSanDns = null,
        string? tlsClientAuthSanUri = null,
        string? tlsClientAuthSanEmail = null,
        string? tlsClientAuthSanIp = null)
    {
        return new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            TokenEndpointAuthMethod = "tls_client_auth",
            TlsClientAuthSubjectDn = tlsClientAuthSubjectDn,
            TlsClientAuthSanDns = tlsClientAuthSanDns,
            TlsClientAuthSanUri = tlsClientAuthSanUri,
            TlsClientAuthSanEmail = tlsClientAuthSanEmail,
            TlsClientAuthSanIp = tlsClientAuthSanIp
        };
    }

    private Client CreateSelfSignedClient(X509Certificate2 cert)
    {
        return new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            TokenEndpointAuthMethod = "self_signed_tls_client_auth",
            ClientSecrets = new List<ClientSecret>
            {
                new()
                {
                    Type = "X509CertificateBase64",
                    Value = Convert.ToBase64String(cert.RawData)
                }
            }
        };
    }

    private HttpRequest CreateMtlsRequest(string clientId)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("auth.example.com");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var formData = Encoding.UTF8.GetBytes($"client_id={clientId}");
        context.Request.Body = new MemoryStream(formData);
        return context.Request;
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static X509Certificate2 CreateCertificateWithSanDns(string subjectName, string dnsName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(dnsName);
        request.CertificateExtensions.Add(sanBuilder.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }

    #endregion
}
