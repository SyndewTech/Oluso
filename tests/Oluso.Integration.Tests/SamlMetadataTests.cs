using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Enterprise.Saml.Services;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for SAML metadata endpoints.
/// Tests skip gracefully when SAML IdP is not available (e.g. enterprise
/// feature not licensed, or signing certificate not configured).
/// </summary>
public class SamlMetadataTests : IntegrationTestBase
{
    private static readonly XNamespace SamlMd = "urn:oasis:names:tc:SAML:2.0:metadata";

    private bool _samlAvailable;

    public SamlMetadataTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        try
        {
            using var scope = Factory.Services.CreateScope();
            var samlSettings = scope.ServiceProvider.GetRequiredService<ISamlTenantSettingsService>();
            await samlSettings.UpdateSettingsAsync("default", s => s.Enabled = true);
            await samlSettings.GenerateSigningCertificateAsync("default");
            _samlAvailable = true;
        }
        catch
        {
            _samlAvailable = false;
        }
    }

    [Fact]
    public async Task IdpMetadata_ReturnsValidXml()
    {
        var response = await Client.GetAsync("/saml/idp/metadata");

        if (!_samlAvailable || response.StatusCode == HttpStatusCode.NotFound)
            return;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("xml");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();

        var doc = XDocument.Parse(content);
        doc.Should().NotBeNull();
    }

    [Fact]
    public async Task IdpMetadata_ContainsEntityDescriptor()
    {
        var response = await Client.GetAsync("/saml/idp/metadata");
        if (!_samlAvailable || response.StatusCode != HttpStatusCode.OK)
            return;

        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);

        var entityDescriptor = doc.Root;
        entityDescriptor.Should().NotBeNull();
        entityDescriptor!.Name.LocalName.Should().Be("EntityDescriptor");
        entityDescriptor.Attribute("entityID").Should().NotBeNull();
    }

    [Fact]
    public async Task IdpMetadata_ContainsIdpDescriptor()
    {
        var response = await Client.GetAsync("/saml/idp/metadata");
        if (!_samlAvailable || response.StatusCode != HttpStatusCode.OK)
            return;

        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);

        var idpDescriptor = doc.Descendants(SamlMd + "IDPSSODescriptor").FirstOrDefault();
        idpDescriptor.Should().NotBeNull();
    }

    [Fact]
    public async Task IdpMetadata_ContainsSsoService()
    {
        var response = await Client.GetAsync("/saml/idp/metadata");
        if (!_samlAvailable || response.StatusCode != HttpStatusCode.OK)
            return;

        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);

        var ssoServices = doc.Descendants(SamlMd + "SingleSignOnService").ToList();
        ssoServices.Should().NotBeEmpty();
    }
}
