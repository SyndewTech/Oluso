using FluentAssertions;
using Oluso.Admin.Dto.Journeys;
using Oluso.Admin.Journeys;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The .journey file is opaque to a casual reader and tamper-evident on import,
/// while plain JSON exports still import. These pin that contract down.
/// </summary>
public class JourneyExportEnvelopeTests
{
    private static JourneyExportDocument Sample() => new()
    {
        SourceName = "Sign in with MFA",
        Policy = new CreatePolicyRequest
        {
            Name = "Sign in with MFA",
            Type = "SignIn",
            Enabled = true,
            Steps =
            [
                new JourneyPolicyStepDto
                {
                    Id = "login",
                    Type = "LocalLogin",
                    Order = 0,
                    Configuration = new Dictionary<string, object> { ["allowSelfRegistration"] = true }
                }
            ]
        }
    };

    [Fact]
    public void Wrap_produces_header_checksum_and_opaque_body()
    {
        var text = JourneyExportEnvelope.Wrap(Sample());
        var lines = text.TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(3);
        lines[0].Should().Be(JourneyExportEnvelope.Header);
        lines[1].Should().StartWith("sha256:");
        text.Should().NotContain("Sign in with MFA");
        text.Should().NotContain("LocalLogin");
    }

    [Fact]
    public void Round_trips_the_document()
    {
        var text = JourneyExportEnvelope.Wrap(Sample());

        JourneyExportEnvelope.TryUnwrap(text, out var document, out var error).Should().BeTrue(error);
        document!.Format.Should().Be(JourneyExportDocument.CurrentFormat);
        document.Version.Should().Be(JourneyExportDocument.CurrentVersion);
        document.Policy.Name.Should().Be("Sign in with MFA");
        document.Policy.Type.Should().Be("SignIn");
        document.Policy.Steps.Should().ContainSingle().Which.Type.Should().Be("LocalLogin");
    }

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var lines = JourneyExportEnvelope.Wrap(Sample()).TrimEnd('\n').Split('\n');
        var other = JourneyExportEnvelope.Wrap(new JourneyExportDocument
        {
            Policy = new CreatePolicyRequest { Name = "Other", Type = "SignUp" }
        }).TrimEnd('\n').Split('\n');
        var tampered = string.Join('\n', lines[0], lines[1], other[2]);

        JourneyExportEnvelope.TryUnwrap(tampered, out var document, out var error).Should().BeFalse();
        document.Should().BeNull();
        error.Should().Contain("modified");
    }

    [Fact]
    public void Plain_json_document_is_accepted()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Sample(), new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        JourneyExportEnvelope.TryUnwrap(json, out var document, out var error).Should().BeTrue(error);
        document!.Policy.Name.Should().Be("Sign in with MFA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("OLUSO-JOURNEY/1\nsha256:abc\n%%%notbase64%%%")]
    [InlineData("{\"format\":\"something.else\",\"version\":1,\"policy\":{\"name\":\"x\",\"type\":\"SignIn\"}}")]
    public void Garbage_and_foreign_files_are_rejected(string content)
    {
        JourneyExportEnvelope.TryUnwrap(content, out var document, out var error).Should().BeFalse();
        document.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Newer_format_version_is_rejected_with_upgrade_hint()
    {
        var doc = Sample();
        doc.Version = JourneyExportDocument.CurrentVersion + 1;

        JourneyExportEnvelope.TryUnwrap(JourneyExportEnvelope.Wrap(doc), out _, out var error).Should().BeFalse();
        error.Should().Contain("newer");
    }

    [Theory]
    [InlineData("Sign in with MFA", "sign-in-with-mfa.journey")]
    [InlineData("  Contact / Form (v2)  ", "contact-form-v2.journey")]
    [InlineData("###", "journey.journey")]
    public void File_name_is_a_slug(string name, string expected)
    {
        JourneyExportEnvelope.FileNameFor(name).Should().Be(expected);
    }
}
