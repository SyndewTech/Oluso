using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authentication;
using Oluso.Core.Services;

namespace Oluso.PublicLinks;

public enum UnlockOutcome { Unlocked, CodeSent, Mismatch, WrongCode, LockedOut, CodeCooldown, NoEmailOnFile, SendFailed }

public sealed record UnlockResult(UnlockOutcome Outcome, string? Proof = null);

/// <summary>
/// Proves a visitor may open a locked link: they give the address it was sent
/// to and, when required, a one-time code mailed there. Success yields a proof
/// bound to that one link that the page keeps and sends with later requests.
/// Failures count per link; too many lock it for a while.
/// </summary>
public interface IPublicLinkUnlockService
{
    bool IsUnlocked(string rawToken, string? proof);

    Task<UnlockResult> UnlockAsync(string rawToken, string tenantId, ActionTokenAccessRequirement requirement,
        string email, string? code, CancellationToken cancellationToken = default);
}

public sealed class PublicLinkUnlockService : IPublicLinkUnlockService
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ProofLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CodeCooldown = TimeSpan.FromSeconds(60);

    private readonly IDataProtector _protector;
    private readonly IDistributedCache _cache;
    private readonly IEmailSender _email;
    private readonly ILogger<PublicLinkUnlockService> _logger;

    public PublicLinkUnlockService(IDataProtectionProvider protection, IDistributedCache cache, IEmailSender email, ILogger<PublicLinkUnlockService> logger)
    {
        _protector = protection.CreateProtector("Oluso.PublicLinks.Unlock.v1");
        _cache = cache;
        _email = email;
        _logger = logger;
    }

    public bool IsUnlocked(string rawToken, string? proof)
    {
        if (string.IsNullOrWhiteSpace(proof)) return false;
        try
        {
            var parts = _protector.Unprotect(proof).Split('|');
            return parts.Length == 2
                && parts[0] == LinkKey(rawToken)
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
                && expires > DateTime.UtcNow.Ticks;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public async Task<UnlockResult> UnlockAsync(string rawToken, string tenantId, ActionTokenAccessRequirement requirement,
        string email, string? code, CancellationToken cancellationToken = default)
    {
        var link = LinkKey(rawToken);
        if (await IsLockedOutAsync(link, cancellationToken))
            return new UnlockResult(UnlockOutcome.LockedOut);
        if (string.IsNullOrWhiteSpace(requirement.ExpectedEmail))
            return new UnlockResult(UnlockOutcome.NoEmailOnFile);

        var matches = string.Equals(email.Trim(), requirement.ExpectedEmail.Trim(), StringComparison.OrdinalIgnoreCase);

        if (requirement.Mode == ActionTokenLockMode.Email)
        {
            if (matches) return Unlocked(rawToken);
            await RecordFailureAsync(link, cancellationToken);
            return new UnlockResult(UnlockOutcome.Mismatch);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            if (!matches)
            {
                // Same answer as a match, so the form cannot be used to test which address a link went to.
                await RecordFailureAsync(link, cancellationToken);
                return new UnlockResult(UnlockOutcome.CodeSent);
            }
            return await SendCodeAsync(link, tenantId, requirement, cancellationToken);
        }

        var stored = await _cache.GetStringAsync(CodeKey(link), cancellationToken);
        if (matches && stored != null && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(Hash(code.Trim()))))
        {
            await _cache.RemoveAsync(CodeKey(link), cancellationToken);
            await _cache.RemoveAsync(FailureKey(link), cancellationToken);
            return Unlocked(rawToken);
        }

        await RecordFailureAsync(link, cancellationToken);
        return new UnlockResult(UnlockOutcome.WrongCode);
    }

    private async Task<UnlockResult> SendCodeAsync(string link, string tenantId, ActionTokenAccessRequirement requirement, CancellationToken cancellationToken)
    {
        if (await _cache.GetStringAsync(CooldownKey(link), cancellationToken) != null)
            return new UnlockResult(UnlockOutcome.CodeCooldown);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var title = string.IsNullOrWhiteSpace(requirement.Title) ? "your document" : requirement.Title;
        var result = await _email.SendAsync(tenantId, new EmailMessage
        {
            To = new[] { requirement.ExpectedEmail!.Trim() },
            Subject = $"Your code to open {title}",
            HtmlBody = $"<p>Your code to open <strong>{WebUtility.HtmlEncode(title)}</strong> is:</p><p style=\"font-size:24px;letter-spacing:4px\"><strong>{code}</strong></p><p>It expires in 10 minutes. If you did not ask for it, ignore this email.</p>",
            TextBody = $"Your code to open {title} is {code}. It expires in 10 minutes. If you did not ask for it, ignore this email.",
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Could not send a link unlock code for tenant {TenantId}: {Error}", tenantId, result.Error);
            return new UnlockResult(UnlockOutcome.SendFailed);
        }

        await _cache.SetStringAsync(CodeKey(link), Hash(code), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CodeLifetime }, cancellationToken);
        await _cache.SetStringAsync(CooldownKey(link), "1", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CodeCooldown }, cancellationToken);
        return new UnlockResult(UnlockOutcome.CodeSent);
    }

    private UnlockResult Unlocked(string rawToken)
    {
        var expires = DateTime.UtcNow.Add(ProofLifetime).Ticks.ToString(CultureInfo.InvariantCulture);
        return new UnlockResult(UnlockOutcome.Unlocked, _protector.Protect($"{LinkKey(rawToken)}|{expires}"));
    }

    private async Task<bool> IsLockedOutAsync(string link, CancellationToken cancellationToken)
    {
        var value = await _cache.GetStringAsync(FailureKey(link), cancellationToken);
        return int.TryParse(value, out var failures) && failures >= MaxFailures;
    }

    private async Task RecordFailureAsync(string link, CancellationToken cancellationToken)
    {
        var value = await _cache.GetStringAsync(FailureKey(link), cancellationToken);
        var failures = int.TryParse(value, out var n) ? n + 1 : 1;
        await _cache.SetStringAsync(FailureKey(link), failures.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LockoutFor }, cancellationToken);
    }

    private static string LinkKey(string rawToken) => Hash(rawToken)[..32];
    private static string FailureKey(string link) => $"oluso:link-unlock:fail:{link}";
    private static string CodeKey(string link) => $"oluso:link-unlock:code:{link}";
    private static string CooldownKey(string link) => $"oluso:link-unlock:cooldown:{link}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
