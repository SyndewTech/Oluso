namespace Oluso.Dto.PublicLinks;

public class FileActionTokenRequest
{
    public string? Token { get; set; }

    /// <summary>Proof from the unlock endpoint that the visitor confirmed their email.</summary>
    public string? Unlock { get; set; }
}
