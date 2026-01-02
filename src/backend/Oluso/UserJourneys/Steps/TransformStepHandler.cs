using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Transforms data during the journey. Useful for data mapping, formatting,
/// and deriving new values from existing data.
/// </summary>
/// <remarks>
/// Configuration options:
/// - transforms: array of transformation rules
///
/// Transform types: copy, constant, uppercase, lowercase, trim, hash, prefix, suffix,
/// replace, regex_replace, regex_match, substring, split, combine, template, map, conditional
/// </remarks>
public class TransformStepHandler : IStepHandler
{
    public string StepType => "transform";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<TransformStepHandler>>();
        var transforms = context.GetConfig<List<TransformRule>>("transforms", new());

        if (transforms.Count == 0)
        {
            logger.LogWarning("Transform step has no transforms defined, skipping");
            return Task.FromResult(StepHandlerResult.Skip());
        }

        var outputData = new Dictionary<string, object>();

        foreach (var transform in transforms)
        {
            try
            {
                var result = ApplyTransform(transform, context);
                if (result != null)
                {
                    outputData[transform.OutputKey] = result;
                    context.SetData(transform.OutputKey, result);
                    logger.LogDebug("Transform {Type}: {Input} -> {Output} = '{Value}'",
                        transform.Type, transform.InputKey, transform.OutputKey, result);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Transform failed for {OutputKey}", transform.OutputKey);
                if (transform.Required)
                {
                    return Task.FromResult(StepHandlerResult.Fail("transform_failed",
                        $"Required transform for {transform.OutputKey} failed: {ex.Message}"));
                }
            }
        }

        return Task.FromResult(StepHandlerResult.Success(outputData));
    }

    private string? ApplyTransform(TransformRule rule, StepExecutionContext context)
    {
        // Get input value
        var inputValue = rule.InputSource?.ToLower() switch
        {
            "constant" => rule.ConstantValue,
            "input" => context.GetInput(rule.InputKey ?? ""),
            "config" => context.GetConfig<string>(rule.InputKey ?? "", null),
            _ => context.GetData<string>(rule.InputKey ?? "")
        };

        if (inputValue == null && rule.Type != "constant" && rule.Type != "combine" && rule.Type != "template")
        {
            return rule.DefaultValue;
        }

        return rule.Type?.ToLower() switch
        {
            "copy" => inputValue,
            "constant" => rule.ConstantValue,
            "uppercase" => inputValue?.ToUpperInvariant(),
            "lowercase" => inputValue?.ToLowerInvariant(),
            "trim" => inputValue?.Trim(),
            "hash" => HashValue(inputValue, rule.HashAlgorithm ?? "sha256"),
            "prefix" => $"{rule.Prefix}{inputValue}",
            "suffix" => $"{inputValue}{rule.Suffix}",
            "replace" => inputValue?.Replace(rule.Find ?? "", rule.ReplaceWith ?? ""),
            "regex_replace" => !string.IsNullOrEmpty(inputValue) && !string.IsNullOrEmpty(rule.Pattern)
                ? Regex.Replace(inputValue, rule.Pattern, rule.ReplaceWith ?? "")
                : inputValue,
            "regex_match" => !string.IsNullOrEmpty(inputValue) && !string.IsNullOrEmpty(rule.Pattern)
                ? Regex.Match(inputValue, rule.Pattern).Value
                : null,
            "substring" => ApplySubstring(inputValue, rule.StartIndex, rule.Length),
            "split" => inputValue?.Split(rule.Delimiter ?? ",").ElementAtOrDefault(rule.Index ?? 0),
            "combine" => CombineValues(rule, context),
            "template" => ApplyTemplate(rule.Template ?? "", context),
            "map" => rule.Mapping?.GetValueOrDefault(inputValue ?? "") ?? rule.DefaultValue,
            "conditional" => EvaluateConditional(rule, inputValue),
            "base64encode" => inputValue != null ? Convert.ToBase64String(Encoding.UTF8.GetBytes(inputValue)) : null,
            "base64decode" => inputValue != null ? Encoding.UTF8.GetString(Convert.FromBase64String(inputValue)) : null,
            "urlencode" => inputValue != null ? Uri.EscapeDataString(inputValue) : null,
            "urldecode" => inputValue != null ? Uri.UnescapeDataString(inputValue) : null,
            _ => inputValue
        };
    }

    private static string? ApplySubstring(string? value, int? startIndex, int? length)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var start = Math.Min(startIndex ?? 0, value.Length);
        var len = Math.Min(length ?? value.Length - start, value.Length - start);
        return value.Substring(start, len);
    }

    private static string? HashValue(string? value, string algorithm)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var bytes = Encoding.UTF8.GetBytes(value);
        byte[] hash;

        using (var hasher = algorithm.ToLower() switch
        {
            "md5" => MD5.Create() as HashAlgorithm,
            "sha1" => SHA1.Create(),
            "sha384" => SHA384.Create(),
            "sha512" => SHA512.Create(),
            _ => SHA256.Create()
        })
        {
            hash = hasher.ComputeHash(bytes);
        }

        return Convert.ToBase64String(hash);
    }

    private string CombineValues(TransformRule rule, StepExecutionContext context)
    {
        if (rule.InputKeys == null || rule.InputKeys.Count == 0)
            return "";

        var values = rule.InputKeys
            .Select(k => context.GetData<string>(k) ?? "")
            .ToList();

        return string.Join(rule.Delimiter ?? " ", values);
    }

    private string ApplyTemplate(string template, StepExecutionContext context)
    {
        var result = template;

        // Replace {data:key} placeholders
        foreach (var kvp in context.JourneyData)
        {
            result = result.Replace($"{{data:{kvp.Key}}}", kvp.Value?.ToString() ?? "");
        }

        // Replace {input:key} placeholders
        foreach (var kvp in context.UserInput)
        {
            result = result.Replace($"{{input:{kvp.Key}}}", kvp.Value?.ToString() ?? "");
        }

        // Replace {user:property} placeholders
        result = result.Replace("{user:id}", context.UserId ?? "");

        return result;
    }

    private string? EvaluateConditional(TransformRule rule, string? inputValue)
    {
        if (rule.Conditions == null || rule.Conditions.Count == 0)
            return rule.DefaultValue;

        foreach (var condition in rule.Conditions)
        {
            var matches = condition.Operator?.ToLower() switch
            {
                "equals" or "eq" => string.Equals(inputValue, condition.Value, StringComparison.OrdinalIgnoreCase),
                "notequals" or "neq" => !string.Equals(inputValue, condition.Value, StringComparison.OrdinalIgnoreCase),
                "contains" => inputValue?.Contains(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
                "startswith" => inputValue?.StartsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
                "exists" or "notnull" => !string.IsNullOrEmpty(inputValue),
                "notexists" or "null" => string.IsNullOrEmpty(inputValue),
                _ => string.Equals(inputValue, condition.Value, StringComparison.OrdinalIgnoreCase)
            };

            if (matches)
            {
                return condition.ThenValue;
            }
        }

        return rule.DefaultValue;
    }
}

#region Configuration Models

/// <summary>
/// A transformation rule
/// </summary>
public class TransformRule
{
    /// <summary>
    /// Type of transformation to apply
    /// </summary>
    public string Type { get; set; } = "copy";

    /// <summary>
    /// Input key to read the value from
    /// </summary>
    public string? InputKey { get; set; }

    /// <summary>
    /// Source of the input: data (default), input, config, constant
    /// </summary>
    public string? InputSource { get; set; }

    /// <summary>
    /// Output key to store the result
    /// </summary>
    public string OutputKey { get; set; } = null!;

    /// <summary>
    /// Default value if input is null
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Whether this transform is required (fails journey if transform fails)
    /// </summary>
    public bool Required { get; set; }

    // Type-specific options
    public string? ConstantValue { get; set; }
    public string? Prefix { get; set; }
    public string? Suffix { get; set; }
    public string? Find { get; set; }
    public string? ReplaceWith { get; set; }
    public string? Pattern { get; set; }
    public string? HashAlgorithm { get; set; }
    public int? StartIndex { get; set; }
    public int? Length { get; set; }
    public string? Delimiter { get; set; }
    public int? Index { get; set; }
    public List<string>? InputKeys { get; set; }
    public string? Template { get; set; }
    public Dictionary<string, string>? Mapping { get; set; }
    public List<ConditionalTransformRule>? Conditions { get; set; }
}

/// <summary>
/// A conditional rule for conditional transforms
/// </summary>
public class ConditionalTransformRule
{
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? ThenValue { get; set; }
}

#endregion
