namespace Oluso.Admin.Dto.IdentityProviders;

public class ProviderIconCandidateDto
{
    /// <summary>Where the icon came from: SAML metadata logo, icon link, apple-touch-icon, favicon.ico.</summary>
    public string Source { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    /// <summary>A PNG data URL encoded by the server from the decoded image; safe to store as IconUrl.</summary>
    public string DataUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string SourceFormat { get; set; } = string.Empty;
}

public class ProviderIconDiscoveryResponse
{
    /// <summary>The site the icons were looked for on.</summary>
    public string? Origin { get; set; }
    public List<ProviderIconCandidateDto> Candidates { get; set; } = new();
    /// <summary>What was skipped or rejected and why.</summary>
    public List<string> Notes { get; set; } = new();
}

public class SetProviderIconRequest
{
    /// <summary>A PNG data URL, normally one returned by discovery. Re-validated and re-encoded before storing.</summary>
    public string DataUrl { get; set; } = string.Empty;
}
