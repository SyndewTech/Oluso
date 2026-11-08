namespace Oluso.Dto.PublicLinks;

public class RedeemActionTokenRequest
{
    public string? Token { get; set; }

    /// <summary>Proof from the unlock endpoint that the visitor confirmed their email.</summary>
    public string? Unlock { get; set; }

    /// <summary>The <c>ActionTokenOption.Key</c> the user picked.</summary>
    public string? Option { get; set; }

    /// <summary>Optional comment associated with the action.</summary>
    public string? Comments { get; set; }

    /// <summary>Values of the option's fields, keyed by field key.</summary>
    public Dictionary<string, string>? Fields { get; set; }
}
