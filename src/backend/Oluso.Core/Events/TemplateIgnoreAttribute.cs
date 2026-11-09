namespace Oluso.Core.Events;

/// <summary>
/// Marks an event property as internal/routing-only — excluded from template variable generation.
/// Properties without this attribute are included by default when generating EventPropertyDefinition[].
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TemplateIgnoreAttribute : Attribute;
