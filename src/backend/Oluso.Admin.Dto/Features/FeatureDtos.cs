namespace Oluso.Admin.Dto.Features;

public class TenantFeaturesResponse
{
    public string TenantId { get; set; } = null!;
    public bool BillingEnabled { get; set; }
    public Dictionary<string, FeatureStatusDto> Features { get; set; } = new();
    public Dictionary<string, LimitStatusDto> Limits { get; set; } = new();
    public PlanSummaryDto? Plan { get; set; }
    public string? SubscriptionStatus { get; set; }
    public string? CurrentPeriodEnd { get; set; }
    public bool IsTrialing { get; set; }
    public string? TrialEnd { get; set; }
}

public class PlanSummaryDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? BillingInterval { get; set; }
}

public class FeatureStatusDto
{
    public bool Enabled { get; set; }
    public string? Value { get; set; }
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
}

public class LimitStatusDto
{
    public int Limit { get; set; }
    public int Current { get; set; }
    public int Remaining { get; set; }
    public bool IsUnlimited { get; set; }
    public string DisplayName { get; set; } = null!;
    public double UsagePercentage { get; set; }
}

public class FeatureCheckResponse
{
    public string Feature { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
    public string? Value { get; set; }
    public string? UpgradeUrl { get; set; }
}

public class LimitCheckResponse
{
    public string LimitType { get; set; } = null!;
    public bool IsAllowed { get; set; }
    public bool IsUnlimited { get; set; }
    public int Limit { get; set; }
    public int Current { get; set; }
    public int Remaining { get; set; }
    public string? Reason { get; set; }
    public bool HasOverageCharge { get; set; }
    public decimal? OveragePricePerUnit { get; set; }
    public string? UpgradeUrl { get; set; }
}

public class FeatureDefinitionsResponse
{
    public List<FeatureDefinition> Features { get; set; } = new();
    public List<LimitDefinition> Limits { get; set; } = new();
}

public class FeatureDefinition
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
}

public class LimitDefinition
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Category { get; set; }
}

public class ModulesResponse
{
    public List<ModuleInfoDto> Modules { get; set; } = new();
}

public class ModuleInfoDto
{
    public string FeatureKey { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? IconName { get; set; }
    public string? Color { get; set; }
    public ModuleAuthorDto Author { get; set; } = null!;
    public List<string> Dependencies { get; set; } = new();
}

public class ModuleAuthorDto
{
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string? Url { get; set; }
}
