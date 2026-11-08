namespace Oluso.Admin.Dto.Search;

/// <summary>
/// Grouped results of an admin search. Each group comes from one provider,
/// which the caller was allowed to search: the tenant has the module, the
/// caller holds the provider's read permission.
/// </summary>
public sealed class AdminSearchResponse
{
    public string Query { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public List<AdminSearchGroupDto> Groups { get; init; } = new();
}

public sealed class AdminSearchGroupDto
{
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Module feature key the results belong to; <c>platform</c> for core records.</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>Kind of record within the module, e.g. <c>employee</c>, <c>invoice</c>.</summary>
    public string Category { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;

    /// <summary>Matches in total, which may exceed the items returned.</summary>
    public int TotalCount { get; init; }

    /// <summary>Admin route to the module's list page, pre-filtered by the query.</summary>
    public string? SeeAllRoute { get; init; }

    public List<AdminSearchResultItem> Items { get; init; } = new();
}

public sealed class AdminSearchResultItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }

    /// <summary>Admin route that opens the record; empty when the record is managed in the workspace app.</summary>
    public string Route { get; init; } = string.Empty;

    /// <summary>Workspace route for records that have no admin page; the client resolves the workspace host.</summary>
    public string? WorkspaceRoute { get; init; }

    public string? Icon { get; init; }
    public string? ImageUrl { get; init; }
    public List<AdminSearchBadge> Badges { get; init; } = new();

    /// <summary>Relative relevance within the group; higher sorts first.</summary>
    public double Score { get; init; }
}

public sealed class AdminSearchBadge
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

    /// <summary>Visual hint: <c>neutral</c>, <c>info</c>, <c>success</c>, <c>warning</c>, <c>danger</c>.</summary>
    public string Tone { get; init; } = AdminSearchBadgeTones.Neutral;
}

public static class AdminSearchBadgeTones
{
    public const string Neutral = "neutral";
    public const string Info = "info";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Danger = "danger";
}

/// <summary>
/// A provider the caller may search, with the filters it accepts.
/// </summary>
public sealed class AdminSearchProviderDto
{
    public string ProviderId { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public List<AdminSearchFilterDefinition> Filters { get; init; } = new();
}

public sealed class AdminSearchFilterDefinition
{
    /// <summary>Key sent back as <c>filters[key]=value</c>.</summary>
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    /// <summary>One of <see cref="AdminSearchFilterTypes"/>.</summary>
    public string Type { get; init; } = AdminSearchFilterTypes.Text;

    public string? Placeholder { get; init; }

    /// <summary>Choices for <c>select</c> and <c>multiselect</c> filters.</summary>
    public List<AdminSearchFilterOption>? Options { get; init; }
}

public sealed class AdminSearchFilterOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public static class AdminSearchFilterTypes
{
    public const string Text = "text";
    public const string Select = "select";
    /// <summary>Comma-separated values.</summary>
    public const string MultiSelect = "multiselect";
    public const string Boolean = "boolean";
    /// <summary>ISO date.</summary>
    public const string Date = "date";
    /// <summary>Two ISO dates separated by <c>..</c>; either side may be empty.</summary>
    public const string DateRange = "daterange";
    public const string Number = "number";
    /// <summary>Two numbers separated by <c>..</c>; either side may be empty.</summary>
    public const string NumberRange = "numberrange";
}
