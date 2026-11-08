using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Oluso.Core.Licensing;

namespace Oluso.Controllers;

/// <summary>
/// Returns platform branding and module display name overrides.
/// Unauthenticated — needed before login for frontend initialization.
/// </summary>
[ApiController]
[Route("api/branding")]
[AllowAnonymous]
public class BrandingController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public BrandingController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Get platform branding and module display names.
    /// Returns configured overrides (e.g., a module shown under its own product name).
    /// </summary>
    [HttpGet]
    public IActionResult GetBranding()
    {
        var platformSection = _configuration.GetSection("Oluso:Platform");

        var modules = ModuleRegistry.Instance.GetRegisteredModules()
            .Select(m => new
            {
                m.FeatureKey,
                DisplayName = ModuleBrandingRegistry.GetDisplayName(m.FeatureKey, m.DisplayName),
                m.Description,
                m.IconName,
            })
            .ToList();

        return Ok(new
        {
            Platform = new
            {
                CompanyName = platformSection["CompanyName"] ?? "Oluso",
                ProductName = platformSection["ProductName"] ?? "Oluso",
                LogoUrl = platformSection["LogoUrl"],
                SupportUrl = platformSection["SupportUrl"],
            },
            Modules = modules,
        });
    }
}
