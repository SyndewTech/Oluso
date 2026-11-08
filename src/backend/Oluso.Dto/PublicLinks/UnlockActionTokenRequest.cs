namespace Oluso.Dto.PublicLinks;

public class UnlockActionTokenRequest
{
    public string? Token { get; set; }

    /// <summary>The address the link was sent to.</summary>
    public string? Email { get; set; }

    /// <summary>The one-time code, for links that require one; omit to have a code sent.</summary>
    public string? Code { get; set; }
}

public class UnlockActionTokenResponse
{
    /// <summary>Proof to send with later requests for this link; set once unlocked.</summary>
    public string? Unlock { get; set; }

    /// <summary>True when a code was sent (or would have been, had the address matched).</summary>
    public bool CodeSent { get; set; }

    public string? Message { get; set; }
}
