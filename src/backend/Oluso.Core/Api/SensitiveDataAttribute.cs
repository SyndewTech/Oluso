namespace Oluso.Core.Api;

/// <summary>
/// Marks a property as containing sensitive data that requires a specific permission to view.
/// When applied to a DTO property, the SensitiveDataFilter will null out the property value
/// in API responses if the requesting user does not have any of the required permissions.
/// This provides field-level access control at the serialization boundary.
/// </summary>
/// <example>
/// public class SampleDto
/// {
///     public string Name { get; set; }
///
///     [SensitiveData("<module>.records.view_sensitive")]
///     public decimal? Amount { get; set; }
///
///     // Visible if user has EITHER read OR write permission
///     [SensitiveData("<module>.records.read", "<module>.records.write")]
///     public string? SecretField { get; set; }
/// }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SensitiveDataAttribute : Attribute
{
    /// <summary>
    /// The primary permission required to view this property's value.
    /// If the user lacks all specified permissions, the property will be set to its default value.
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// Additional permissions that also grant access to this property (OR logic).
    /// The user needs ANY ONE of these permissions to view the value.
    /// </summary>
    public string[] AlternatePermissions { get; }

    public SensitiveDataAttribute(string permission, params string[] alternatePermissions)
    {
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        AlternatePermissions = alternatePermissions ?? Array.Empty<string>();
    }
}
