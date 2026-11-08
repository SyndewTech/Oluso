namespace Oluso.Admin.Dto.Files;

public class FileUploadResponse
{
    public string ImageUrl { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public long SizeBytes { get; set; }
}

public class SasUploadRequest
{
    public string FileName { get; set; } = null!;
    public string? ContentType { get; set; }
    public string? Folder { get; set; }
}

public class SasUploadResponse
{
    public string UploadUrl { get; set; } = null!;
    public string ImageUrl { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public int ExpiresInSeconds { get; set; }
}
