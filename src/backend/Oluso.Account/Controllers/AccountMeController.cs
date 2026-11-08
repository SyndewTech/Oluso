using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Account.Dto.Workspace;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account /me endpoint - returns the current user's identity info including features.
/// Used by the Account SPA to determine which module plugins to show.
/// </summary>
[Route("api/account/me")]
public class AccountMeController : AccountBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly IFeatureGate _featureGate;
    private readonly IEnumerable<IProfileEnricher> _enrichers;
    private readonly ILogger<AccountMeController> _logger;

    public AccountMeController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IFeatureGate featureGate,
        IEnumerable<IProfileEnricher> enrichers,
        ILogger<AccountMeController> logger) : base(tenantContext)
    {
        _userService = userService;
        _featureGate = featureGate;
        _enrichers = enrichers;
        _logger = logger;
    }

    /// <summary>
    /// Get current user's account profile with features
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AccountMeDto>> GetMe(CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound(new { message = "User not found" });

        // Resolve enabled features by checking the feature gate for installed modules
        var features = new List<string>();
        foreach (var module in ModuleRegistry.Instance.GetRegisteredModules())
        {
            var result = await _featureGate.CheckFeatureAsync(module.FeatureKey, UserId, cancellationToken);
            if (result.IsAllowed)
                features.Add(module.FeatureKey);
        }

        var dto = new AccountMeDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = ResolveDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email),
            ProfilePictureUrl = user.Picture,
            TenantId = TenantId,
            Features = features,
        };

        // Module facts about this user (is a patient, has a linked relative, ...),
        // from the same enrichers the workspace profile uses. An enricher only runs
        // when the tenant has its module, so the portal never advertises a module
        // the tenant does not own. One failing module must not take the profile down.
        var context = new ProfileEnricherContext
        {
            UserId = UserId,
            TenantId = TenantId,
            Email = user.Email,
            Extensions = dto.Extensions,
        };
        foreach (var enricher in _enrichers.OrderBy(e => e.Priority))
        {
            if (enricher.FeatureKey != null && !features.Contains(enricher.FeatureKey))
                continue;

            try
            {
                await enricher.EnrichAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Account profile enricher {EnricherId} failed for user {UserId}", enricher.EnricherId, UserId);
            }
        }

        return Ok(dto);
    }

    // Never an empty string: clients render this verbatim in greetings and headers.
    private static string ResolveDisplayName(string? displayName, string? firstName, string? lastName, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        var full = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        if (!string.IsNullOrWhiteSpace(userName)) return userName;
        return email ?? string.Empty;
    }
}
