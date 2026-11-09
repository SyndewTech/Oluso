using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Oluso.Core.Generators
{
    /// <summary>
    /// Generates one <c>IApprovalEntityPropertySource</c> implementation per
    /// assembly, populated from every class carrying
    /// <c>[Oluso.Approval.Domain.ApprovalEntity("EntityType")]</c>. Each
    /// public, readable, non-ignored property of those classes becomes an
    /// <c>ApprovalConditionProperty</c> in the generated source — surfaced
    /// to the workflow-builder UI as a usable condition field and validated
    /// against at save time.
    ///
    /// <para>Property metadata layers (in order of precedence): explicit
    /// <c>[ApprovalConditionProperty(Description=..., Example=...)]</c>;
    /// otherwise auto-derived (display name from PascalCase, type from C#
    /// type, no description, no example). The runtime aggregator unions
    /// multiple sources per entity type so an entity defined in one assembly
    /// can be extended (extra fields) by another.</para>
    /// </summary>
    [Generator]
    public class ApprovalConditionPropertyGenerator : IIncrementalGenerator
    {
        private const string EntityAttrFull = "Oluso.Approval.Domain.ApprovalEntityAttribute";
        private const string PropertyAttrFull = "Oluso.Approval.Domain.ApprovalConditionPropertyAttribute";
        private const string IgnoreAttrFull = "Oluso.Approval.Domain.ApprovalConditionIgnoreAttribute";
        private const string ConditionPropertyTypeFull = "Oluso.Approval.Domain.ApprovalConditionProperty";
        private const string SourceInterfaceFull = "Oluso.Approval.Domain.IApprovalEntityPropertySource";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var snapshotClasses = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    EntityAttrFull,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, ct) => GetSnapshotInfo(ctx, ct))
                .Where(static info => info != null)
                .Select(static (info, _) => info!)
                .Collect();

            var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Unknown");

            var combined = snapshotClasses.Combine(assemblyName);

            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var classes = data.Left;
                if (classes.IsDefaultOrEmpty) return;

                var asmName = data.Right;
                var sourceName = GetSourceClassName(asmName);
                var code = GenerateSource(classes, sourceName);
                spc.AddSource(sourceName + ".g.cs", SourceText.From(code, Encoding.UTF8));
            });
        }

        #region Discovery

        private static SnapshotClassInfo? GetSnapshotInfo(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        {
            try
            {
                if (context.TargetSymbol is not INamedTypeSymbol symbol) return null;

                // Pull the entity-type string from the [ApprovalEntity("...")] ctor arg.
                string? entityType = null;
                foreach (var attr in context.Attributes)
                {
                    if (attr.AttributeClass?.ToDisplayString() != EntityAttrFull) continue;
                    if (attr.ConstructorArguments.Length > 0 &&
                        attr.ConstructorArguments[0].Value is string s)
                    {
                        entityType = s;
                    }
                }
                if (string.IsNullOrEmpty(entityType)) return null;

                var properties = new List<SnapshotProperty>();
                foreach (var member in symbol.GetMembers())
                {
                    ct.ThrowIfCancellationRequested();
                    if (member is not IPropertySymbol prop) continue;
                    if (prop.IsStatic || prop.IsWriteOnly) continue;
                    if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                    if (HasAttribute(prop, IgnoreAttrFull)) continue;

                    string? description = null;
                    string? example = null;
                    foreach (var attr in prop.GetAttributes())
                    {
                        if (attr.AttributeClass?.ToDisplayString() != PropertyAttrFull) continue;
                        foreach (var named in attr.NamedArguments)
                        {
                            if (named.Key == "Description") description = named.Value.Value as string;
                            else if (named.Key == "Example") example = named.Value.Value as string;
                        }
                    }

                    properties.Add(new SnapshotProperty(
                        prop.Name,
                        MapToSchemaType(prop.Type),
                        description,
                        example));
                }

                return new SnapshotClassInfo(entityType!, properties);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasAttribute(IPropertySymbol prop, string fullName)
        {
            foreach (var attr in prop.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == fullName) return true;
            }
            return false;
        }

        #endregion

        #region Type Mapping

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol nullable &&
                nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                nullable.TypeArguments.Length > 0)
                return nullable.TypeArguments[0];
            return type;
        }

        /// <summary>
        /// C# → condition-schema mapping. Mirrors <c>EventPropertyGenerator.MapToSchemaType</c>
        /// where it overlaps, but with the slimmer schema the runtime
        /// condition evaluator understands ("string"/"number"/"boolean").
        /// Enums map to "string"; collections map to "array" (no operator
        /// supports arrays today — surfacing them anyway helps the UI hint
        /// that <c>contains</c> is not the right tool yet).
        /// </summary>
        private static string MapToSchemaType(ITypeSymbol type)
        {
            type = UnwrapNullable(type);
            switch (type.SpecialType)
            {
                case SpecialType.System_String: return "string";
                case SpecialType.System_Boolean: return "boolean";
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                    return "number";
                case SpecialType.System_DateTime: return "datetime";
            }

            var displayName = type.ToDisplayString();
            if (displayName == "System.DateTimeOffset" || displayName == "System.DateOnly") return "datetime";
            if (displayName == "System.TimeSpan" || displayName == "System.TimeOnly") return "string";
            if (displayName == "System.Guid" || displayName == "System.Uri") return "string";

            if (type is IArrayTypeSymbol) return "array";
            if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                var def = named.OriginalDefinition.ToDisplayString();
                if (def.StartsWith("System.Collections.Generic.IReadOnlyList") ||
                    def.StartsWith("System.Collections.Generic.IList") ||
                    def.StartsWith("System.Collections.Generic.List") ||
                    def.StartsWith("System.Collections.Generic.IEnumerable") ||
                    def.StartsWith("System.Collections.Generic.ICollection") ||
                    def.StartsWith("System.Collections.Generic.HashSet"))
                    return "array";
            }
            if (type.TypeKind == TypeKind.Enum) return "string";
            return "string";
        }

        #endregion

        #region Codegen

        private static string GetSourceClassName(string assemblyName)
        {
            var parts = assemblyName.Split('.');
            var sb = new StringBuilder();
            var startIndex = (parts.Length > 0 && parts[0] == "Oluso") ? 1 : 0;
            if (startIndex >= parts.Length) sb.Append("Core");
            else for (int i = startIndex; i < parts.Length; i++) sb.Append(parts[i]);
            sb.Append("EntityConditionSource");
            return sb.ToString();
        }

        private static string GenerateSource(ImmutableArray<SnapshotClassInfo> classes, string className)
        {
            // Group by entity type — multiple snapshot classes can describe
            // the same EntityType (e.g. one in a base assembly for core fields,
            // another in a module assembly adding extras). The generated source still
            // produces a single dictionary entry per entity type, unioning props.
            var byEntity = classes
                .GroupBy(c => c.EntityType, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace Oluso.Approval.Domain");
            sb.AppendLine("{");
            sb.Append("    /// <summary>Auto-generated approval-condition property source. Register via <c>services.AddApprovalEntityPropertySource&lt;").Append(className).AppendLine("&gt;()</c>.</summary>");
            sb.Append("    public sealed class ").Append(className).Append(" : ").Append(SourceInterfaceFull).AppendLine();
            sb.AppendLine("    {");
            sb.Append("        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<").Append(ConditionPropertyTypeFull).AppendLine(">> _byEntity =");
            sb.Append("            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<").Append(ConditionPropertyTypeFull).AppendLine(">>(System.StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("        {");

            foreach (var group in byEntity)
            {
                sb.Append("            [\"").Append(EscapeString(group.Key)).AppendLine("\"] = new[]");
                sb.AppendLine("            {");

                // Dedupe properties by name when multiple snapshot classes
                // contribute to the same entity type — first occurrence wins.
                // Each property is tagged with Category = entity type so the
                // admin UI can filter the flat list by the workflow's
                // entity type (existing frontend contract).
                var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var cls in group)
                {
                    foreach (var prop in cls.Properties)
                    {
                        if (!seen.Add(prop.Name)) continue;
                        sb.Append("                new ").Append(ConditionPropertyTypeFull).Append(" { Name = \"")
                          .Append(prop.Name).Append("\", Type = \"").Append(prop.Type).Append("\"");
                        if (!string.IsNullOrEmpty(prop.Description))
                        {
                            sb.Append(", Description = \"").Append(EscapeString(prop.Description!)).Append("\"");
                        }
                        if (!string.IsNullOrEmpty(prop.Example))
                        {
                            sb.Append(", Example = \"").Append(EscapeString(prop.Example!)).Append("\"");
                        }
                        sb.Append(", Category = \"").Append(EscapeString(group.Key)).Append("\"");
                        sb.AppendLine(" },");
                    }
                }

                sb.AppendLine("            },");
            }

            sb.AppendLine("        };");
            sb.AppendLine();
            sb.Append("        public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<").Append(ConditionPropertyTypeFull).AppendLine(">> GetConditionPropertiesByEntityType() => _byEntity;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        #endregion

        #region Models

        private sealed class SnapshotClassInfo
        {
            public string EntityType { get; }
            public List<SnapshotProperty> Properties { get; }

            public SnapshotClassInfo(string entityType, List<SnapshotProperty> properties)
            {
                EntityType = entityType;
                Properties = properties;
            }
        }

        private sealed class SnapshotProperty
        {
            public string Name { get; }
            public string Type { get; }
            public string? Description { get; }
            public string? Example { get; }

            public SnapshotProperty(string name, string type, string? description, string? example)
            {
                Name = name;
                Type = type;
                Description = description;
                Example = example;
            }
        }

        #endregion
    }
}
