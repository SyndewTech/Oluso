using FluentAssertions;
using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Ldap.Headless;
using Oluso.Enterprise.Ldap.Integration;
using Xunit;

namespace Oluso.Enterprise.Ldap.Tests;

/// <summary>Journey/_LdapLogin described as a form: sign-in, and the change-password form with its own back button.</summary>
public class LdapLoginDescriberTests
{
    private static JourneyStepForm Describe(LdapLoginViewModel? model) =>
        new LdapLoginDescriber(new KeyLocalizerFactory())
            .Describe(new JourneyStepDescription("step", "ldap", "Journey/_LdapLogin", model, null));

    [Fact]
    public void Sign_in_posts_username_password_and_remember_me_without_a_button_name()
    {
        var form = Describe(new LdapLoginViewModel
        {
            ProviderDisplayName = "Acme AD", TenantName = "Acme", Username = "jo",
            ErrorMessage = "Invalid username or password", InfoMessage = "Password changed"
        });

        form.Title.Should().Be("Acme AD");
        form.Description.Should().Be("Sign in to Acme");
        form.Error.Should().Be("Invalid username or password");
        form.Notice.Should().Be("Password changed");
        form.Fields.Where(f => f.Type != "content").Select(f => (f.Name, f.Type, f.Value)).Should().Equal(
            ("username", "text", "jo"), ("password", "password", null), ("rememberMe", "checkbox", null));
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = (string?)null, Value = (string?)null, PostsFields = true });
    }

    [Fact]
    public void Remember_me_and_change_password_follow_the_model()
    {
        var form = Describe(new LdapLoginViewModel { ShowRememberMe = false, AllowPasswordChange = true });

        form.Fields.Should().NotContain(f => f.Name == "rememberMe");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind)).Should().Equal((null, null, "primary"), ("action", "show_change_password", "link"));
    }

    [Fact]
    public void Change_password_posts_the_new_password_and_back_posts_only_the_username()
    {
        var form = Describe(new LdapLoginViewModel { Username = "jo", AllowPasswordChange = true, ChangePasswordMode = true, ErrorMessage = "The new passwords don't match" });

        form.Error.Should().Be("The new passwords don't match");
        form.Fields.Where(f => f.Type != "content").Select(f => f.Name).Should().Equal("username", "password", "newPassword", "confirmPassword");
        form.Fields.First().Value.Should().Be("jo");
        form.Actions.Select(a => (a.Name, a.Value, a.PostsFields)).Should().Equal(("action", "change_password", true), ("username", "jo", false));
    }

    private sealed class KeyLocalizerFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource) => new KeyLocalizer();
        public IStringLocalizer Create(string baseName, string location) => new KeyLocalizer();
    }

    private sealed class KeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}:{string.Join(",", arguments)}");
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
