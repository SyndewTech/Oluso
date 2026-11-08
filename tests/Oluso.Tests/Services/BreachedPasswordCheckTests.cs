using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// The breached-password check sends only a 5-character hash prefix, asks for padded responses,
/// ignores padding entries, and gives up quickly. When the service can't answer, the tenant
/// policy decides: by default the password goes through (an outage must not block sign-ups);
/// with <see cref="TenantPasswordSettings.RejectWhenBreachCheckUnavailable"/> it is refused.
/// </summary>
public class BreachedPasswordCheckTests
{
    private const string Password = "correct horse battery staple";
    private static readonly string Hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Password)));

    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        public static FakeApi Unreachable() => new(() => throw new HttpRequestException("connection refused"));
        public HttpRequestMessage? Request { get; private set; }

        public FakeApi(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_respond());
        }
    }

    private static (TenantPasswordValidator Validator, IHttpClientFactory Clients) Validator(FakeApi api, bool rejectWhenUnavailable = false)
    {
        var services = new ServiceCollection();
        TenantPasswordValidator.AddPwnedPasswordsClient(services);
        services.AddHttpClient(TenantPasswordValidator.PwnedPasswordsClient).ConfigurePrimaryHttpMessageHandler(() => api);
        var clients = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TenantPasswordSettings
        {
            MinimumLength = 1,
            RequireDigit = false,
            RequireLowercase = false,
            RequireUppercase = false,
            RequireNonAlphanumeric = false,
            RequiredUniqueChars = 0,
            BlockCommonPasswords = false,
            CheckBreachedPasswords = true,
            RejectWhenBreachCheckUnavailable = rejectWhenUnavailable
        });

        return (new TenantPasswordValidator(settings.Object, NullLogger<TenantPasswordValidator>.Instance, clients, new Mock<IPasswordHistoryStore>().Object), clients);
    }

    private static HttpResponseMessage Range(params string[] lines) =>
        new(HttpStatusCode.OK) { Content = new StringContent(string.Join("\r\n", lines)) };

    [Fact]
    public async Task A_breached_password_is_rejected_using_only_the_hash_prefix_and_padding()
    {
        var api = new FakeApi(() => Range("0018A45C4D1DEF81644B54AB7F969B88D65:1", $"{Hash[5..]}:42"));
        var (validator, _) = Validator(api);

        var result = await validator.ValidateAsync(null!, new OlusoUser(), Password);

        result.Errors.Should().ContainSingle(e => e.Code == "PasswordBreached");
        api.Request!.RequestUri!.AbsoluteUri.Should().Be($"https://api.pwnedpasswords.com/range/{Hash[..5]}");
        api.Request.Headers.GetValues("Add-Padding").Should().ContainSingle("true");
    }

    [Fact]
    public async Task Padding_entries_are_not_breaches()
    {
        var (validator, _) = Validator(new FakeApi(() => Range($"{Hash[5..]}:0")));

        (await validator.ValidateAsync(null!, new OlusoUser(), Password)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task An_unavailable_service_lets_the_password_through()
    {
        var (validator, _) = Validator(new FakeApi(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        (await validator.ValidateAsync(null!, new OlusoUser(), Password)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task An_unreachable_service_lets_the_password_through_by_default()
    {
        var (validator, _) = Validator(FakeApi.Unreachable());

        (await validator.ValidateAsync(null!, new OlusoUser(), Password)).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_policy_that_rejects_when_unavailable_refuses_the_password(bool unreachable)
    {
        var api = unreachable ? FakeApi.Unreachable() : new FakeApi(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var (validator, _) = Validator(api, rejectWhenUnavailable: true);

        var result = await validator.ValidateAsync(null!, new OlusoUser(), Password);

        result.Errors.Should().ContainSingle(e => e.Code == "PasswordBreachCheckUnavailable")
            .Which.Description.Should().Contain("try again");
    }

    [Fact]
    public async Task A_policy_that_rejects_when_unavailable_still_accepts_a_clean_password()
    {
        var (validator, _) = Validator(new FakeApi(() => Range("0018A45C4D1DEF81644B54AB7F969B88D65:3")), rejectWhenUnavailable: true);

        (await validator.ValidateAsync(null!, new OlusoUser(), Password)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_check_gives_up_after_five_seconds()
    {
        var (_, clients) = Validator(new FakeApi(() => Range()));

        clients.CreateClient(TenantPasswordValidator.PwnedPasswordsClient).Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }
}
