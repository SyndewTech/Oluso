namespace Oluso.Dto.Files;

public class WorkspaceFileUploadResponse
{
    public string ImageUrl { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = null!;
}
