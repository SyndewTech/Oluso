using Microsoft.AspNetCore.DataProtection;
using Oluso.Core.Services;

namespace Oluso.Keys;

/// <summary>
/// Key encryption service using ASP.NET Core Data Protection
/// </summary>
public class DataProtectionKeyEncryptionService : IKeyEncryptionService
{
    private readonly IDataProtector _protector;

    public DataProtectionKeyEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("Oluso.SigningKeys");
    }

    public Task<string> EncryptAsync(string data, CancellationToken cancellationToken = default)
    {
        var encrypted = _protector.Protect(data);
        return Task.FromResult(encrypted);
    }

    public Task<string> DecryptAsync(string encryptedData, CancellationToken cancellationToken = default)
    {
        var decrypted = _protector.Unprotect(encryptedData);
        return Task.FromResult(decrypted);
    }

    /// <summary>
    /// Encrypt bytes and return as base64 string.
    /// </summary>
    public string Encrypt(byte[] data)
    {
        var encrypted = _protector.Protect(data);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypt base64-encoded encrypted data to bytes.
    /// </summary>
    public byte[] Decrypt(string encryptedData)
    {
        var encrypted = Convert.FromBase64String(encryptedData);
        return _protector.Unprotect(encrypted);
    }
}
