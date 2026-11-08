namespace Oluso.Admin.Dto.Grants;

public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class PersistedGrantDto
{
    public string Key { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string ClientId { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? Expiration { get; set; }
    public DateTime? ConsumedTime { get; set; }
    /// <summary>Expiration has passed; hidden from the default list, shown with the "include expired" filter.</summary>
    public bool IsExpired { get; set; }
}

public class ServerSideSessionDto
{
    public int Id { get; set; }
    public string Key { get; set; } = null!;
    public string Scheme { get; set; } = null!;
    public string SubjectId { get; set; } = null!;
    public string? SessionId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
    public bool IsExpired { get; set; }
}
