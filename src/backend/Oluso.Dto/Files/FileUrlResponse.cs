namespace Oluso.Dto.Files;

public class FileUrlResponse
{
    public string Url { get; set; } = null!;
    /// <summary>True if the URL can be used directly (SAS/public), false if it needs auth headers.</summary>
    public bool IsDirect { get; set; }
}
