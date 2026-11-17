using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Fido2.Configuration;
using Oluso.Enterprise.Fido2.Entities;
using Oluso.Enterprise.Fido2.Services;
using Oluso.Enterprise.Fido2.Stores;
using Xunit;

namespace Oluso.Enterprise.Fido2.Tests;

/// <summary>
/// Whether a passkey verified its user (the WebAuthn UV flag) reaches the sign-in: a passkey that verified the user
/// with its PIN or biometric is two factors and records mfa (NIST SP 800-63B), one that only tested presence records
/// hwk alone; and a relying party that requires user verification refuses an assertion without it (WebAuthn 7.2).
/// </summary>
public class PasskeyUserVerificationTests
{
    private const string RpId = "id.acme.test";
    private const string Origin = "https://id.acme.test";
    private const string Challenge = "challenge-1";
    private static readonly byte[] CredentialId = { 1, 2, 3, 4 };

    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private Fido2AuthenticationService Service(string userVerification)
    {
        var parameters = _key.ExportParameters(false);
        var publicKey = new byte[] { 0x04 }.Concat(parameters.Q.X!).Concat(parameters.Q.Y!).ToArray();
        var store = new Mock<IFido2CredentialStore>();
        store.Setup(s => s.GetByCredentialIdAsync(Base64Url(CredentialId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fido2CredentialEntity
            {
                Id = "cred-1",
                UserId = "u1",
                CredentialId = Base64Url(CredentialId),
                PublicKey = Base64Url(publicKey),
                UserHandle = Base64Url(Encoding.UTF8.GetBytes("u1")),
            });
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Origin);

        return new Fido2AuthenticationService(
            Mock.Of<IOlusoUserService>(),
            store.Object,
            Mock.Of<ITenantContext>(),
            new HttpContextAccessor(),
            issuer.Object,
            Options.Create(new Fido2Options
            {
                RelyingPartyId = RpId,
                Origins = { Origin },
                UserVerificationRequirement = userVerification
            }),
            NullLogger<Fido2AuthenticationService>.Instance);
    }

    private AuthenticatorAssertionResponse Assertion(byte flags)
    {
        var authenticatorData = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(RpId)).CopyTo(authenticatorData, 0);
        authenticatorData[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(authenticatorData.AsSpan(33), 1);
        var clientDataJson = Encoding.UTF8.GetBytes(
            $"{{\"type\":\"webauthn.get\",\"challenge\":\"{Challenge}\",\"origin\":\"{Origin}\"}}");
        var signature = _key.SignData(
            authenticatorData.Concat(SHA256.HashData(clientDataJson)).ToArray(),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new AuthenticatorAssertionResponse
        {
            Id = Base64Url(CredentialId),
            RawId = Base64Url(CredentialId),
            Response = new AssertionResponseData
            {
                AuthenticatorData = Base64Url(authenticatorData),
                ClientDataJson = Base64Url(clientDataJson),
                Signature = Base64Url(signature)
            }
        };
    }

    [Theory]
    [InlineData(UserPresent | UserVerified, true)]
    [InlineData(UserPresent, false)]
    public async Task The_assertion_reports_whether_the_passkey_verified_its_user(byte flags, bool verified)
    {
        var result = await Service("preferred").VerifyAssertionAsync(Assertion(flags), Challenge);

        result.Success.Should().BeTrue(result.Error);
        result.UserVerified.Should().Be(verified);
    }

    [Fact]
    public async Task A_relying_party_that_requires_user_verification_refuses_a_passkey_that_only_tested_presence()
    {
        var refused = await Service("required").VerifyAssertionAsync(Assertion(UserPresent), Challenge);
        var accepted = await Service("required").VerifyAssertionAsync(Assertion(UserPresent | UserVerified), Challenge);

        refused.Success.Should().BeFalse();
        refused.ErrorCode.Should().Be("user_verification_required");
        accepted.Success.Should().BeTrue(accepted.Error);
    }

    [Theory]
    [InlineData(true, new[] { "hwk", "mfa" })]
    [InlineData(false, new[] { "hwk" })]
    public void A_passkey_records_mfa_only_when_it_verified_the_user(bool userVerified, string[] amr)
    {
        PasskeyAuthenticationMethods.Amr(userVerified).Should().Equal(amr);
        SecondFactorRequirement.IsMet(PasskeyAuthenticationMethods.Amr(userVerified)).Should().Be(userVerified);
    }

    [Fact]
    public void Changing_the_methods_one_sign_in_got_does_not_change_the_next_ones()
    {
        var first = PasskeyAuthenticationMethods.Amr(userVerified: false);
        first[0] = "mfa";

        PasskeyAuthenticationMethods.Amr(userVerified: false).Should().Equal("hwk");
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
