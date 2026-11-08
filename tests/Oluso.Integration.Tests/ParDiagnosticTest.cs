using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oluso.EntityFramework;
using Oluso.Integration.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Oluso.Integration.Tests;

public class ParDiagnosticTest : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    
    public ParDiagnosticTest(OlusoWebApplicationFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Fact]
    public async Task Diagnostic_CheckClientExistsAndParResponse()
    {
        // Check if test-client exists
        await WithDbContextAsync(async db =>
        {
            var clientCount = db.Clients.Count();
            _output.WriteLine($"Total clients: {clientCount}");
            foreach (var c in db.Clients.Take(10))
            {
                _output.WriteLine($"  Client: {c.ClientId}, RequireSecret: {c.RequireClientSecret}");
            }
            var testClient = db.Clients.FirstOrDefault(c => c.ClientId == "test-client");
            _output.WriteLine($"test-client found: {testClient != null}");
            if (testClient != null)
            {
                var secrets = db.Set<Oluso.Core.Domain.Entities.ClientSecret>()
                    .Where(s => s.ClientId == testClient.Id).ToList();
                _output.WriteLine($"  Secrets count: {secrets.Count}");
                foreach (var s in secrets)
                {
                    _output.WriteLine($"  Secret type: {s.Type}, value length: {s.Value?.Length}");
                }
            }
        });

        // Try PAR
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["redirect_uri"] = "http://localhost:5100/signin-oidc",
            ["scope"] = "openid profile",
        });

        var response = await Client.PostAsync("/connect/par", request);
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"PAR Status: {response.StatusCode}");
        _output.WriteLine($"PAR Body: {body}");
    }
}
