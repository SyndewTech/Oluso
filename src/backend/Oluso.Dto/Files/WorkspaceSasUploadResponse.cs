namespace Oluso.Dto.Files;

public class WorkspaceSasUploadResponse
{
    public string UploadUrl { get; set; } = null!;
    public string ImageUrl { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public int ExpiresInSeconds { get; set; }
}
