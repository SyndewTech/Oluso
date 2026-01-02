using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for SAML metadata endpoints.
/// </summary>
public class SamlMetadataTests : IntegrationTestBase
{
    private static readonly XNamespace SamlMd = "urn:oasis:names:tc:SAML:2.0:metadata";
    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    public SamlMetadataTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task IdpMetadata_ReturnsValidXml()
    {
        // Act
        var response = await Client.GetAsync("/saml/idp/metadata");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("xml");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();

        // Parse as XML
        var doc = XDocument.Parse(content);
        doc.Should().NotBeNull();
    }

    [Fact]
    public async Task IdpMetadata_ContainsEntityDescriptor()
    {
        // Act
        var response = await Client.GetAsync("/saml/idp/metadata");
        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);

        // Assert
        var entityDescriptor = doc.Root;
        entityDescriptor.Should().NotBeNull();
        entityDescriptor!.Name.LocalName.Should().Be("EntityDescriptor");
        entityDescriptor.Attribute("entityID").Should().NotBeNull();
    }

    [Fact]
    public async Task IdpMetadata_ContainsIdpDescriptor()
    {
        // Act
        var response = await Client.GetAsync("/saml/idp/metadata");
        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);

        // Assert
        var idpDescriptor = doc.Descendants(SamlMd + "IDPSSODescriptor").FirstOrDefault();
        idpDescriptor.Should().NotBeNull();
    }

    [Fact]
    public async Task IdpMetadata_ContainsSsoService()
    {
        // Act
        var response = await Client.GetAsync("/saml/idp/metadata");
        var content = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(content);

        // Assert
        var ssoServices = doc.Descendants(SamlMd + "SingleSignOnService").ToList();
        ssoServices.Should().NotBeEmpty();
    }
}
