namespace Oluso.Core.Events;

/// <summary>
/// Marks a navigation property for expansion in generated event property definitions and data extraction.
/// When applied, the generator will flatten the nested object's properties using underscore-joined snake_case
/// (e.g., ShippingAddress.Street becomes shipping_address_street).
/// Uses the default expansion depth (2) unless overridden with <see cref="TemplateDepthAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TemplateExpandAttribute : Attribute;

/// <summary>
/// Controls the maximum expansion depth for a navigation property in generated event property definitions.
/// Depth 0 = no expansion (treat as opaque object). Depth 1 = expand immediate properties only.
/// Depth 2 = expand nested properties one additional level. Implies <see cref="TemplateExpandAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TemplateDepthAttribute : Attribute
{
    public int Depth { get; }
    public TemplateDepthAttribute(int depth) => Depth = depth;
}
