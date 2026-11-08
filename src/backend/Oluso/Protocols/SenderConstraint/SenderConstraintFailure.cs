namespace Oluso.Protocols.SenderConstraint;

/// <summary>A sender-constraint check that failed, carried to the 401 challenge.</summary>
/// <param name="Scheme">The authentication scheme to challenge with: DPoP or Bearer.</param>
/// <param name="Nonce">A DPoP nonce the client must use on its retry.</param>
public sealed record SenderConstraintFailure(string Scheme, string Error, string Description, string? Nonce = null);
