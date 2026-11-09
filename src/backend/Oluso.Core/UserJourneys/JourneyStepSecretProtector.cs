using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Protects the secret-bearing configuration of journey steps — the outbound
/// credentials an <c>api_call</c> or <c>webhook</c> step uses.
///
/// Two jobs, both driven off <see cref="IJourneyStepSecretResolver"/> so the
/// step-type schema stays the single source of truth for what counts as secret:
/// <list type="bullet">
///   <item><b>At rest</b> — values are encrypted with <c>IDataProtection</c>
///     before the policy is persisted, matching how tenant configuration handles
///     SMTP and integration credentials. Previously they sat in the Steps JSON
///     column in the clear.</item>
///   <item><b>On read</b> — values are replaced with <see cref="RedactedValue"/>
///     in admin responses, so an API caller can't read back a credential the UI
///     only ever shows masked. Writing that same marker back keeps the stored
///     value, which is what a round-tripping editor does.</item>
/// </list>
/// </summary>
public interface IJourneyStepSecretProtector
{
    /// <summary>Marker returned in place of a stored secret.</summary>
    const string RedactedValue = "__redacted__";

    /// <summary>Encrypts secret values in place, ready to persist.</summary>
    void Protect(IEnumerable<JourneyPolicyStep> steps);

    /// <summary>Decrypts secret values in place, for step execution.</summary>
    void Unprotect(IEnumerable<JourneyPolicyStep> steps);

    /// <summary>
    /// Replaces stored secrets with <see cref="RedactedValue"/> for API responses.
    /// Operates on the outgoing copy — never on anything about to be persisted.
    /// </summary>
    void Redact(IEnumerable<JourneyPolicyStep> steps);

    /// <summary>
    /// Restores secrets the caller sent back as <see cref="RedactedValue"/> (or
    /// omitted) from the stored policy, so an edit that doesn't touch a secret
    /// doesn't erase it.
    /// </summary>
    void PreserveUnchanged(IEnumerable<JourneyPolicyStep> incoming, IEnumerable<JourneyPolicyStep> stored);
}

/// <inheritdoc />
public class JourneyStepSecretProtector : IJourneyStepSecretProtector
{
    private readonly IJourneyStepSecretResolver _resolver;
    private readonly IDataProtector _protector;
    private readonly ILogger<JourneyStepSecretProtector> _logger;

    public JourneyStepSecretProtector(
        IJourneyStepSecretResolver resolver,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<JourneyStepSecretProtector> logger)
    {
        _resolver = resolver;
        _protector = dataProtectionProvider.CreateProtector("Oluso.JourneyStepSecrets");
        _logger = logger;
    }

    public void Protect(IEnumerable<JourneyPolicyStep> steps) =>
        Transform(steps, value =>
        {
            // Already ciphertext (a re-save that didn't touch the value) — leave it.
            if (IsProtected(value))
            {
                return value;
            }
            return Envelope + _protector.Protect(value);
        });

    public void Unprotect(IEnumerable<JourneyPolicyStep> steps) =>
        Transform(steps, value =>
        {
            if (!IsProtected(value))
            {
                // Written before this protection existed. Leave it usable rather
                // than breaking a live journey; the next save encrypts it.
                return value;
            }

            try
            {
                return _protector.Unprotect(value[Envelope.Length..]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to decrypt a journey step secret — the step will run without it");
                return null;
            }
        });

    public void Redact(IEnumerable<JourneyPolicyStep> steps) =>
        Transform(steps, _ => IJourneyStepSecretProtector.RedactedValue);

    public void PreserveUnchanged(IEnumerable<JourneyPolicyStep> incoming, IEnumerable<JourneyPolicyStep> stored)
    {
        var storedById = stored
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var step in incoming)
        {
            if (step.Configuration == null || !storedById.TryGetValue(step.Id, out var previous))
            {
                continue;
            }

            foreach (var path in _resolver.GetSecretPaths(step.Type))
            {
                var submitted = _resolver.ReadValue(step.Configuration, path);
                var unchanged = string.IsNullOrEmpty(submitted)
                    || submitted == IJourneyStepSecretProtector.RedactedValue;

                if (!unchanged)
                {
                    continue;
                }

                var kept = _resolver.ReadValue(previous.Configuration, path);
                _resolver.WriteValue(step.Configuration, path, kept);
            }
        }
    }

    /// <summary>
    /// Prefix marking a value as produced by this protector, so plaintext left
    /// by an older write is recognisable and a double-encrypt is impossible.
    /// </summary>
    private const string Envelope = "enc:v1:";

    private static bool IsProtected(string value) =>
        value.StartsWith(Envelope, StringComparison.Ordinal);

    private void Transform(IEnumerable<JourneyPolicyStep> steps, Func<string, string?> transform)
    {
        foreach (var step in steps)
        {
            if (step.Configuration == null)
            {
                continue;
            }

            foreach (var path in _resolver.GetSecretPaths(step.Type))
            {
                var value = _resolver.ReadValue(step.Configuration, path);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                _resolver.WriteValue(step.Configuration, path, transform(value));
            }
        }
    }
}
