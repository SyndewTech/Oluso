namespace Oluso.Core.Storage;

/// <summary>
/// Where <see cref="IEntityReferenceProtector"/> keeps its keys. They sit in the signing-key store under their own
/// client ids; key maintenance must never expire or rotate them, since every value they protected needs them to be read.
/// </summary>
public static class DataProtectionKeys
{
    public const string ClientIdPrefix = "oluso.data-protection.";

    public static string ClientIdFor(string purpose) => ClientIdPrefix + purpose;

    public static bool IsDataProtectionKey(string? clientId) =>
        clientId != null && clientId.StartsWith(ClientIdPrefix, StringComparison.Ordinal);
}
