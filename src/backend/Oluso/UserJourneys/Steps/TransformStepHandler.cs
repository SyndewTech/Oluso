using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Derives journey values from other values: each mapping reads a source, applies one
/// transform from <see cref="Catalog"/> with its options, and writes the target. The
/// catalog is published in the step's schema, so the builder offers exactly what runs.
/// </summary>
public class TransformStepHandler : IStepHandler, IStepSettingsSchema
{
    public string StepType => "transform";

    public const string MappingsSetting = "mappings";

    /// <summary>The transforms this step runs, with the options each one reads from <c>transformConfig</c>.</summary>
    public static readonly IReadOnlyList<TransformDefinition> Catalog =
    [
        new("copy", "Copy as is", "The source value unchanged."),
        new("lowercase", "Lowercase", "The value in lower case."),
        new("uppercase", "Uppercase", "The value in upper case."),
        new("trim", "Trim whitespace", "The value without leading or trailing spaces."),
        new("split", "Split", "One part of the value split by a delimiter.",
            new TransformOption("delimiter", "Delimiter", ","), new TransformOption("index", "Part to take (0 = first, -1 = last)", "0")),
        new("join", "Join", "All of the source's values joined into one.",
            new TransformOption("separator", "Separator", ", ")),
        new("replace", "Replace text", "The value with every occurrence of some text replaced.",
            new TransformOption("find", "Find"), new TransformOption("replaceWith", "Replace with")),
        new("regex_replace", "Replace by pattern", "The value with every match of a regular expression replaced.",
            new TransformOption("pattern", "Pattern"), new TransformOption("replaceWith", "Replace with")),
        new("regex_match", "Extract by pattern", "The first match of a regular expression (or its first group).",
            new TransformOption("pattern", "Pattern")),
        new("substring", "Part of the text", "Characters from a position.",
            new TransformOption("start", "Start position", "0"), new TransformOption("length", "Length (blank = to the end)")),
        new("prefix", "Add prefix", "Text placed before the value.", new TransformOption("value", "Prefix")),
        new("suffix", "Add suffix", "Text placed after the value.", new TransformOption("value", "Suffix")),
        new("template", "Template", "Text with placeholders such as {data:given_name} or {input:code}; the source is not needed.",
            new TransformOption("template", "Template", Multiline: true)),
        new("map", "Map values", "A value looked up in a list of pairs, one per line: from=to.",
            new TransformOption("pairs", "Pairs (from=to, one per line)", Multiline: true), new TransformOption("otherwise", "When nothing matches (blank = keep the value)")),
        new("hash", "Hash", "A one-way hash of the value, base64 encoded.",
            new TransformOption("algorithm", "Algorithm (sha256, sha384, sha512)", "sha256")),
        new("base64encode", "Base64 encode", "The value base64 encoded."),
        new("base64decode", "Base64 decode", "The value base64 decoded."),
        new("urlencode", "URL encode", "The value escaped for use in a URL."),
        new("urldecode", "URL decode", "The value with URL escapes decoded."),
    ];

    /// <summary>Source types a mapping can read.</summary>
    public static readonly IReadOnlyList<string> SourceTypes = ["journeyData", "claim", "context", "user", "input", "literal"];

    public Dictionary<string, object> GetConfigurationSchema() => new()
    {
        [MappingsSetting] = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["required"] = true,
            ["description"] = "Values to derive, applied in order. Each reads a source, applies a transform and writes the target.",
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["source"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Key to read (the text itself for literal)" },
                    ["sourceType"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = SourceTypes, ["default"] = "journeyData", ["description"] = "Where the source is read from" },
                    ["target"] = new Dictionary<string, object> { ["type"] = "string", ["required"] = true, ["description"] = "Journey key to write" },
                    ["transform"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["enum"] = Catalog.Select(t => t.Name).ToArray(),
                        ["default"] = "copy",
                        ["description"] = "What to do to the value",
                        // The builder shows each transform's options from here.
                        ["x-transforms"] = Catalog.Select(t => new Dictionary<string, object?>
                        {
                            ["name"] = t.Name,
                            ["label"] = t.Label,
                            ["description"] = t.Description,
                            ["options"] = t.Options.Select(o => new Dictionary<string, object?> { ["name"] = o.Name, ["label"] = o.Label, ["default"] = o.Default, ["multiline"] = o.Multiline }).ToArray(),
                        }).ToArray(),
                    },
                    ["transformConfig"] = new Dictionary<string, object> { ["type"] = "object", ["additionalProperties"] = new Dictionary<string, object> { ["type"] = "string" }, ["description"] = "The transform's options" },
                    ["defaultValue"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Used when no value is produced" },
                    ["required"] = new Dictionary<string, object> { ["type"] = "boolean", ["default"] = false, ["description"] = "Fail the journey when no value is produced" },
                },
            },
        },
    };

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var errors = new List<string>();
        var mappings = StepValueReader.Read<List<TransformMapping>>(
            configuration?.TryGetValue(MappingsSetting, out var raw) == true ? raw : null, null, errors.Add) ?? [];

        if (mappings.Count == 0)
        {
            errors.Add("At least one mapping is required");
        }

        foreach (var (mapping, index) in mappings.Select((m, i) => (m, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(mapping.Target))
                errors.Add($"Mapping {index} has no target");
            var transform = Find(mapping.Transform);
            if (transform == null)
                errors.Add($"Mapping {index} uses unknown transform '{mapping.Transform}'");
            else if (transform.Name != "template" && string.IsNullOrWhiteSpace(mapping.Source) && mapping.SourceType != "literal")
                errors.Add($"Mapping {index} has no source");
            if (transform?.Name is "regex_replace" or "regex_match" && Option(mapping, "pattern") is { } pattern)
            {
                try { _ = new Regex(pattern); }
                catch (ArgumentException ex) { errors.Add($"Mapping {index} pattern is not a valid regular expression: {ex.Message}"); }
            }
        }

        return Task.FromResult(errors.Count == 0
            ? StepConfigurationValidationResult.Valid()
            : StepConfigurationValidationResult.Build(errors, []));
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<TransformStepHandler>>();
        var mappings = context.GetConfig<List<TransformMapping>>(MappingsSetting, []) ?? [];

        if (mappings.Count == 0)
        {
            logger.LogWarning("Transform step {StepId} has no mappings", context.StepId);
            return StepHandlerResult.Skip();
        }

        var values = await JourneyConditionValues.ForStepAsync(context,
            mappings.Select(m => m.SourceType == "literal" ? null : SourceFor(m.SourceType)), cancellationToken);

        var output = new Dictionary<string, object>();
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Target)) continue;

            string? result;
            try
            {
                var inputs = mapping.SourceType == "literal"
                    ? (string.IsNullOrEmpty(mapping.Source) ? [] : [mapping.Source])
                    : values.Get(SourceFor(mapping.SourceType), mapping.Source);
                result = Apply(mapping, inputs, context);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or RegexMatchTimeoutException)
            {
                logger.LogWarning(ex, "Transform {Transform} for {Target} failed", mapping.Transform, mapping.Target);
                result = null;
            }

            if (string.IsNullOrEmpty(result))
            {
                result = mapping.DefaultValue;
            }

            if (string.IsNullOrEmpty(result))
            {
                if (mapping.Required)
                {
                    return StepHandlerResult.Fail("transform_failed", $"No value could be produced for '{mapping.Target}'.");
                }
                continue;
            }

            output[mapping.Target] = result;
            context.SetData(mapping.Target, result);
        }

        return StepHandlerResult.Success(output);
    }

    private static string SourceFor(string? sourceType) => sourceType switch
    {
        null or "" or "journeyData" => "data",
        _ => sourceType,
    };

    private static TransformDefinition? Find(string? name) =>
        Catalog.FirstOrDefault(t => string.Equals(t.Name, string.IsNullOrEmpty(name) ? "copy" : name, StringComparison.OrdinalIgnoreCase));

    private static string? Option(TransformMapping mapping, string name)
    {
        if (mapping.TransformConfig != null)
        {
            foreach (var (key, value) in mapping.TransformConfig)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
            }
        }
        return Find(mapping.Transform)?.Options.FirstOrDefault(o => o.Name == name)?.Default;
    }

    private static string? Apply(TransformMapping mapping, IReadOnlyList<string> inputs, StepExecutionContext context)
    {
        var transform = Find(mapping.Transform) ?? throw new ArgumentException($"Unknown transform '{mapping.Transform}'");
        var value = inputs.Count > 0 ? inputs[0] : null;

        switch (transform.Name)
        {
            case "template":
                return Fill(Option(mapping, "template") ?? "", context);
            case "join":
                return inputs.Count == 0 ? null : string.Join(Option(mapping, "separator") ?? "", inputs);
        }

        if (value == null) return null;

        return transform.Name switch
        {
            "copy" => value,
            "lowercase" => value.ToLowerInvariant(),
            "uppercase" => value.ToUpperInvariant(),
            "trim" => value.Trim(),
            "split" => Part(value.Split(Option(mapping, "delimiter") ?? ","), Number(Option(mapping, "index")) ?? 0),
            "replace" => string.IsNullOrEmpty(Option(mapping, "find")) ? value : value.Replace(Option(mapping, "find")!, Option(mapping, "replaceWith") ?? ""),
            "regex_replace" => Regex.Replace(value, Required(mapping, "pattern"), Option(mapping, "replaceWith") ?? "", RegexOptions.None, RegexTimeout),
            "regex_match" => Matched(Regex.Match(value, Required(mapping, "pattern"), RegexOptions.None, RegexTimeout)),
            "substring" => Substring(value, Number(Option(mapping, "start")) ?? 0, Number(Option(mapping, "length"))),
            "prefix" => (Option(mapping, "value") ?? "") + value,
            "suffix" => value + (Option(mapping, "value") ?? ""),
            "map" => Map(value, Option(mapping, "pairs"), Option(mapping, "otherwise")),
            "hash" => Hash(value, Option(mapping, "algorithm") ?? "sha256"),
            "base64encode" => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)),
            "base64decode" => Encoding.UTF8.GetString(Convert.FromBase64String(value)),
            "urlencode" => Uri.EscapeDataString(value),
            "urldecode" => Uri.UnescapeDataString(value),
            _ => value,
        };
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static string Required(TransformMapping mapping, string option) =>
        Option(mapping, option) is { Length: > 0 } value ? value : throw new ArgumentException($"The '{option}' option is required");

    private static int? Number(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string? Part(string[] parts, int index) =>
        index < 0 ? parts.ElementAtOrDefault(parts.Length + index) : parts.ElementAtOrDefault(index);

    private static string? Matched(Match match) =>
        !match.Success ? null : match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

    private static string Substring(string value, int start, int? length)
    {
        start = Math.Clamp(start, 0, value.Length);
        var available = value.Length - start;
        return value.Substring(start, length is { } l ? Math.Clamp(l, 0, available) : available);
    }

    private static string Map(string value, string? pairs, string? otherwise)
    {
        foreach (var line in (pairs ?? "").Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator > 0 && string.Equals(line[..separator].Trim(), value, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }
        return string.IsNullOrEmpty(otherwise) ? value : otherwise;
    }

    private static string Hash(string value, string algorithm)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = algorithm.ToLowerInvariant() switch
        {
            "sha256" => SHA256.HashData(bytes),
            "sha384" => SHA384.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => throw new ArgumentException($"Unsupported hash algorithm '{algorithm}'"),
        };
        return Convert.ToBase64String(hash);
    }

    private static string Fill(string template, StepExecutionContext context)
    {
        return Regex.Replace(template, @"\{(data|input|context):([^}]+)\}", match =>
        {
            var key = match.Groups[2].Value;
            return match.Groups[1].Value switch
            {
                "data" => context.GetData<string>(key) ?? "",
                "input" => context.GetInput(key) ?? "",
                _ => key switch
                {
                    "userId" => context.UserId ?? "",
                    "tenantId" => context.TenantId ?? "",
                    "clientId" => context.ClientId ?? "",
                    _ => "",
                },
            };
        }, RegexOptions.None, RegexTimeout);
    }
}

/// <summary>One mapping of a transform step, as the builder writes it.</summary>
public class TransformMapping
{
    /// <summary>Key (or literal text when <see cref="SourceType"/> is <c>literal</c>).</summary>
    public string? Source { get; set; }

    /// <summary>journeyData (default), claim, context, user, input or literal.</summary>
    public string? SourceType { get; set; }

    public string Target { get; set; } = "";

    /// <summary>A name from <see cref="TransformStepHandler.Catalog"/>; copy when empty.</summary>
    public string? Transform { get; set; }

    public Dictionary<string, string>? TransformConfig { get; set; }

    public string? DefaultValue { get; set; }

    /// <summary>Fail the journey when no value can be produced.</summary>
    public bool Required { get; set; }
}

public sealed record TransformOption(string Name, string Label, string? Default = null, bool Multiline = false);

public sealed record TransformDefinition(string Name, string Label, string Description, params TransformOption[] Options);
