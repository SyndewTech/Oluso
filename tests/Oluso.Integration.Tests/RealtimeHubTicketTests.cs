using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// The realtime hub is opened with a short-lived ticket obtained over an authenticated call, so a
/// sender-constrained access token never has to travel in a URL.
/// </summary>
public class RealtimeHubTicketTests : IntegrationTestBase, IDisposable
{
    private const string Negotiate = "/hubs/realtime/negotiate?negotiateVersion=1";
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public RealtimeHubTicketTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task A_ticket_opens_the_hub_in_the_query_string_or_the_authorization_header()
    {
        var ticket = await TicketAsync(await TokenAsync(proof: null), "Bearer", proof: null);

        var inQuery = await Client.PostAsync($"{Negotiate}&access_token={Uri.EscapeDataString(ticket)}", content: null);
        using var inHeader = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        inHeader.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket);

        inQuery.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.SendAsync(inHeader)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_ticket_or_no_credential_is_refused()
    {
        var unknown = await Client.PostAsync($"{Negotiate}&access_token=rt_not-a-real-ticket", content: null);
        var none = await Client.PostAsync(Negotiate, content: null);

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ticket_cannot_be_traded_for_another_ticket()
    {
        var ticket = await TicketAsync(await TokenAsync(proof: null), "Bearer", proof: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/realtime/ticket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_dpop_bound_token_gets_a_ticket_with_a_proof_and_cannot_open_the_hub_itself()
    {
        var bound = await TokenAsync(Proof("POST", "/connect/token", accessToken: null));

        var ticket = await TicketAsync(bound, "DPoP", Proof("POST", "/api/realtime/ticket", bound));
        var withTicket = await Client.PostAsync($"{Negotiate}&access_token={Uri.EscapeDataString(ticket)}", content: null);
        var withToken = await Client.PostAsync($"{Negotiate}&access_token={Uri.EscapeDataString(bound)}", content: null);

        withTicket.StatusCode.Should().Be(HttpStatusCode.OK);
        withToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> TicketAsync(string accessToken, string scheme, string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/realtime/ticket");
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, accessToken);
        if (proof != null) request.Headers.Add("DPoP", proof);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("expiresIn").GetInt32().Should().BeGreaterThan(0);
        var ticket = body.GetProperty("ticket").GetString()!;
        ticket.Should().StartWith("rt_");
        return ticket;
    }

    private async Task<string> TokenAsync(string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = "testuser@example.com",
                ["password"] = "Password123!",
                ["client_id"] = "ropc-client",
                ["client_secret"] = "test-secret",
                ["scope"] = "openid profile email",
            }),
        };
        if (proof != null) request.Headers.Add("DPoP", proof);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    private string Proof(string method, string path, string? accessToken)
    {
        var parameters = _key.ExportParameters(false);
        var header = new JwtHeader(new SigningCredentials(new ECDsaSecurityKey(_key), SecurityAlgorithms.EcdsaSha256))
        {
            ["typ"] = "dpop+jwt",
            ["jwk"] = new Dictionary<string, object>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64UrlEncoder.Encode(parameters.Q.X!),
                ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y!),
            },
        };
        var payload = new JwtPayload
        {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = method,
            ["htu"] = $"http://localhost{path}",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        if (accessToken != null)
            payload["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}
