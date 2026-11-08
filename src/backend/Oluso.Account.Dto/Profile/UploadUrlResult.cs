namespace Oluso.Account.Dto.Profile;

public class UploadUrlResult
{
    public string UploadUrl { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public int ExpiresIn { get; set; }
}
