namespace Oluso.Dto.Files;

public class MyFileDto
{
    public string Id { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string? Folder { get; set; }
    public DateTime CreatedAt { get; set; }
}
