using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Oluso.Core.Tests.Locale;

/// <summary>
/// Server-rendered pages resolve their copy through <c>UiStringDefaults.Resolve</c>;
/// a key missing from a locale file renders as the raw key on screen. Every key
/// the backend asks for must exist in every shipped language.
/// </summary>
public class UiStringKeysTests
{
    private static readonly Regex ResolveCall = new(@"UiStringDefaults\.Resolve\(\s*""([a-z0-9_]+)""", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Oluso.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Oluso.sln) not found");
    }

    [Fact]
    public void Every_resolved_key_exists_in_every_locale()
    {
        var root = RepoRoot();
        var backend = Path.Combine(root, "src", "backend");

        var keys = Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => ResolveCall.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        keys.Should().NotBeEmpty();

        var resources = Path.Combine(backend, "Oluso.Core", "Locale", "Resources");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(resources, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var present = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            missing.AddRange(keys.Where(k => !present.Contains(k)).Select(k => $"{Path.GetFileName(file)}: {k}"));
        }

        missing.Should().BeEmpty();
    }
}
