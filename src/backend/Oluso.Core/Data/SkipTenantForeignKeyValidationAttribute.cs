namespace Oluso.Core.Data;

/// <summary>
/// Marks a foreign key property to skip automatic tenant-scoped FK validation on save.
/// Use this for FK properties that intentionally reference shared/global entities
/// (not scoped to a tenant) or cross-tenant references.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SkipTenantForeignKeyValidationAttribute : Attribute
{
}
