namespace Oluso.Dto.Files;

public class WorkspaceSasUploadRequest
{
    public string FileName { get; set; } = null!;
    public string? ContentType { get; set; }
    public string? Folder { get; set; }
}
