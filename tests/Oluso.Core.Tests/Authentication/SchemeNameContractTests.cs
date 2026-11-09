using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Oluso.Core.Tests.Authentication;

/// <summary>
/// Pins the hard-coded scheme-name literal embedded in
/// <c>Oluso.Core.Authentication.TenantCookieAuthenticationOptions.PostConfigure</c>
/// against the source-of-truth in
/// <see cref="IdentityConstants.ApplicationScheme"/>.
///
/// Oluso.Core deliberately doesn't reference Microsoft.AspNetCore.Identity
/// (it's a one-constant dependency that would pull the whole Identity
/// stack into Core for callers that don't need it), so the production
/// code uses the literal string "Identity.Application". If a future
/// Identity release renames the scheme, the literal silently rots; this
/// test is the regression net.
/// </summary>
public class SchemeNameContractTests
{
    [Fact]
    public void TenantCookieAuthentication_application_scheme_literal_matches_identity_constant()
    {
        // Same literal as in TenantCookieAuthentication.PostConfigure.
        const string ApplicationSchemeLiteral = "Identity.Application";

        ApplicationSchemeLiteral.Should().Be(IdentityConstants.ApplicationScheme,
            "TenantCookieAuthentication.PostConfigure scopes the tenant principal " +
            "validator using a hard-coded \"Identity.Application\" literal — if " +
            "IdentityConstants.ApplicationScheme is ever renamed by Microsoft.AspNetCore.Identity, " +
            "the validator silently stops applying to the Oluso session cookie. " +
            "Update both places together.");
    }
}
