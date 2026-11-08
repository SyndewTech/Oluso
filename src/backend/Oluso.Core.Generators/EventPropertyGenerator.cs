using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Oluso.Core.Generators
{
    [Generator]
    public class EventPropertyGenerator : IIncrementalGenerator
    {
        private const string TemplateIgnoreAttr = "Oluso.Core.Events.TemplateIgnoreAttribute";
        private const string TemplateExpandAttr = "Oluso.Core.Events.TemplateExpandAttribute";
        private const string TemplateDepthAttr = "Oluso.Core.Events.TemplateDepthAttribute";
        private const string WebhookEventAttr = "Oluso.Core.Events.WebhookEventAttribute";
        private const string WebhookProviderAttr = "Oluso.Core.Events.WebhookProviderAttribute";
        private const string DataClassificationAttr = "Oluso.Core.Events.DataClassificationAttribute";
        private const int SensitivityInternal = 1;
        private const int SensitivityRestricted = 2;
        private const string OlusoEventFull = "Oluso.Core.Events.OlusoEvent";
        private const int DefaultExpansionDepth = 2;

        private static readonly HashSet<string> BasePropertyNames = new HashSet<string>
        {
            "Id", "Timestamp", "TenantId", "ActivityId",
            "Category", "EventType", "IsFailure", "WebhookEventType"
        };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var eventClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsConcreteClassCandidate(node),
                    transform: static (ctx, ct) => GetEventClassInfo(ctx, ct))
                .Where(static info => info != null)
                .Select(static (info, _) => info!)
                .Collect();

            // Extract assembly name and [assembly: WebhookProvider] attribute
            var assemblyInfo = context.CompilationProvider.Select(static (c, _) =>
            {
                var asmName = c.AssemblyName ?? "Unknown";
                string? providerId = null;
                string? providerDisplayName = null;

                // Check for [assembly: WebhookProvider("id", "Display Name")]
                try
                {
                    foreach (var attr in c.Assembly.GetAttributes())
                    {
                        if (attr.AttributeClass != null &&
                            attr.AttributeClass.ToDisplayString() == WebhookProviderAttr &&
                            attr.ConstructorArguments.Length >= 2)
                        {
                            providerId = attr.ConstructorArguments[0].Value as string;
                            providerDisplayName = attr.ConstructorArguments[1].Value as string;
                        }
                    }
                }
                catch
                {
                    // Assembly attributes may not be available in all contexts
                }

                // Derive from assembly name if not specified
                if (string.IsNullOrEmpty(providerId))
                {
                    providerId = DeriveProviderId(asmName);
                    providerDisplayName = DeriveProviderDisplayName(asmName);
                }

                return new AssemblyInfo(asmName, providerId!, providerDisplayName!);
            });

            var combined = eventClasses.Combine(assemblyInfo);

            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var classes = data.Left;
                var asm = data.Right;
                var sourceName = GetSourceClassName(asm.Name);
                var code = GenerateSource(classes, asm, sourceName);
                spc.AddSource(sourceName + ".g.cs", SourceText.From(code, Encoding.UTF8));

                if (asm.Name == "Oluso.Core")
                {
                    var registryCode = GenerateStaticRegistry(classes);
                    spc.AddSource("EventPropertyRegistry.g.cs", SourceText.From(registryCode, Encoding.UTF8));
                }
            });
        }

        #region Syntax Analysis

        private static bool IsConcreteClassCandidate(SyntaxNode node)
        {
            if (!(node is ClassDeclarationSyntax classDecl))
                return false;
            if (classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
                return false;
            return classDecl.BaseList != null;
        }

        private static EventClassInfo? GetEventClassInfo(GeneratorSyntaxContext context, CancellationToken ct)
        {
            try
            {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
            if (symbol == null) return null;
            if (!DerivesFrom(symbol, OlusoEventFull)) return null;

            var className = symbol.Name;
            var fullName = symbol.ToDisplayString();

            // Extract WebhookEventType values (one per arm when it switches on the event's data)
            var webhookEventTypes = new List<string>();
            var webhookProp = FindPropertyInHierarchy(symbol, "WebhookEventType");
            if (webhookProp != null)
            {
                webhookEventTypes = GetConstantReturnValues(webhookProp, context.SemanticModel, ct);
            }

            // Extract Category value
            string? category = null;
            var categoryProp = FindPropertyInHierarchy(symbol, "Category");
            if (categoryProp != null)
            {
                category = GetConstantReturnValue(categoryProp, context.SemanticModel, ct);
            }

            // Extract Scope value (enum). The generator reads the constant
            // member name (Tenant / Platform / Both) so it doesn't need a
            // reference to the runtime enum at codegen time.
            string? scope = null;
            var scopeProp = FindPropertyInHierarchy(symbol, "Scope");
            if (scopeProp != null && scopeProp.Type.TypeKind == TypeKind.Enum)
            {
                scope = GetEnumMemberName(scopeProp, context.SemanticModel, ct);
            }

            // Extract [WebhookEvent] attribute metadata
            string? webhookDisplayName = null;
            string? webhookDescription = null;
            bool webhookEnabledByDefault = false;
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass != null && attr.AttributeClass.ToDisplayString() == WebhookEventAttr)
                {
                    if (attr.ConstructorArguments.Length > 0)
                        webhookDisplayName = attr.ConstructorArguments[0].Value as string;
                    foreach (var named in attr.NamedArguments)
                    {
                        if (named.Key == "Description") webhookDescription = named.Value.Value as string;
                        if (named.Key == "EnabledByDefault" && named.Value.Value is bool b) webhookEnabledByDefault = b;
                    }
                }
            }

            var classification = GetClassClassification(symbol);

            // Collect properties with expansion
            var properties = new List<FlatProperty>();
            var visitedTypes = new HashSet<string>();
            CollectProperties(symbol, properties, "", "", DefaultExpansionDepth, visitedTypes, ct,
                defaultSensitive: classification.Sensitivity == SensitivityRestricted);

            return new EventClassInfo(className, fullName, webhookEventTypes, category ?? "",
                webhookDisplayName ?? "", webhookDescription ?? "", webhookEnabledByDefault, scope ?? "Tenant", properties,
                classification);
            }
            catch
            {
                return null;
            }
        }

        private static void CollectProperties(
            INamedTypeSymbol typeSymbol,
            List<FlatProperty> results,
            string prefix,
            string displayPrefix,
            int remainingDepth,
            HashSet<string> visitedTypes,
            CancellationToken ct,
            string accessPrefix = "",
            bool defaultSensitive = false)
        {
            var seenNames = new HashSet<string>();
            var current = typeSymbol;

            while (current != null && current.ToDisplayString() != OlusoEventFull)
            {
                foreach (var member in current.GetMembers())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!(member is IPropertySymbol prop)) continue;
                    if (prop.IsStatic || prop.IsWriteOnly || prop.DeclaredAccessibility != Accessibility.Public) continue;

                    // Only filter base properties at root level
                    if (string.IsNullOrEmpty(prefix) && BasePropertyNames.Contains(prop.Name)) continue;
                    if (HasAttribute(prop, TemplateIgnoreAttr)) continue;
                    if (!seenNames.Add(prop.Name)) continue;

                    var schemaType = MapToSchemaType(prop.Type);
                    var isRequired = prop.IsRequired;
                    var snakeName = prefix + ToSnakeCase(prop.Name);
                    var dispName = displayPrefix + ToDisplayName(prop.Name);

                    // Check if this property should be expanded
                    var shouldExpand = ShouldExpand(prop, schemaType, remainingDepth);
                    var expandDepth = GetExpandDepth(prop, remainingDepth);

                    var accessPath = string.IsNullOrEmpty(accessPrefix)
                        ? prop.Name
                        : accessPrefix + prop.Name;
                    var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                        prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                    var declared = GetPropertySensitivity(prop);
                    var isSensitive = declared == SensitivityRestricted || (declared != SensitivityInternal && defaultSensitive);

                    if (shouldExpand && schemaType == "object")
                    {
                        var nestedType = UnwrapNullable(prop.Type) as INamedTypeSymbol;
                        if (nestedType != null && !nestedType.IsGenericType)
                        {
                            var typeKey = nestedType.ToDisplayString();
                            if (!visitedTypes.Contains(typeKey))
                            {
                                visitedTypes.Add(typeKey);
                                // Use null-conditional for nullable navigation props
                                var nestedAccessPrefix = accessPath + (isNullable ? "?." : ".");
                                CollectProperties(
                                    nestedType, results,
                                    snakeName + "_",
                                    dispName + " ",
                                    expandDepth - 1,
                                    visitedTypes, ct,
                                    nestedAccessPrefix,
                                    isSensitive);
                                visitedTypes.Remove(typeKey);
                                continue; // Don't add the parent object property
                            }
                            // Circular reference — fall through, add as opaque object
                        }
                    }

                    results.Add(new FlatProperty(
                        prop.Name, snakeName, dispName, schemaType, isRequired, isNullable, accessPath, isSensitive));
                }
                current = current.BaseType;
            }
        }

        /// <summary>
        /// The [DataClassification] on the event class or the nearest base class that carries one.
        /// </summary>
        private static ClassificationInfo GetClassClassification(INamedTypeSymbol symbol)
        {
            var current = symbol;
            while (current != null)
            {
                foreach (var attr in current.GetAttributes())
                {
                    if (attr.AttributeClass == null || attr.AttributeClass.ToDisplayString() != DataClassificationAttr) continue;
                    var sensitivity = attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int v ? v : 0;
                    string? permission = null;
                    string? purpose = null;
                    foreach (var named in attr.NamedArguments)
                    {
                        if (named.Key == "RequiredPermission") permission = named.Value.Value as string;
                        if (named.Key == "Purpose") purpose = named.Value.Value as string;
                    }
                    return new ClassificationInfo(sensitivity, permission, purpose);
                }
                current = current.BaseType;
            }
            return new ClassificationInfo(0, null, null);
        }

        /// <summary>The sensitivity declared on a property (or the property it overrides); 0 when none.</summary>
        private static int GetPropertySensitivity(IPropertySymbol prop)
        {
            foreach (var attr in prop.GetAttributes())
            {
                if (attr.AttributeClass != null && attr.AttributeClass.ToDisplayString() == DataClassificationAttr
                    && attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int v)
                    return v;
            }
            return prop.OverriddenProperty != null ? GetPropertySensitivity(prop.OverriddenProperty) : 0;
        }

        private static bool ShouldExpand(IPropertySymbol prop, string schemaType, int remainingDepth)
        {
            if (schemaType != "object") return false;
            if (remainingDepth <= 0) return false;
            if (HasAttribute(prop, TemplateIgnoreAttr)) return false;

            // Explicit [TemplateExpand] or [TemplateDepth(n)] always expands
            if (HasAttribute(prop, TemplateExpandAttr)) return true;
            if (HasAttribute(prop, TemplateDepthAttr)) return true;

            // Default: expand if we have remaining depth
            return remainingDepth > 0;
        }

        private static int GetExpandDepth(IPropertySymbol prop, int remainingDepth)
        {
            // Check for [TemplateDepth(n)]
            foreach (var attr in prop.GetAttributes())
            {
                if (attr.AttributeClass != null &&
                    attr.AttributeClass.ToDisplayString() == TemplateDepthAttr &&
                    attr.ConstructorArguments.Length > 0)
                {
                    var val = attr.ConstructorArguments[0].Value;
                    if (val is int depth) return depth;
                }
            }
            return remainingDepth;
        }

        private static IPropertySymbol? FindPropertyInHierarchy(INamedTypeSymbol symbol, string name)
        {
            var current = symbol;
            while (current != null)
            {
                foreach (var member in current.GetMembers(name))
                {
                    if (member is IPropertySymbol prop) return prop;
                }
                current = current.BaseType;
            }
            return null;
        }

        /// <summary>
        /// Walks property override chain and returns the enum member name
        /// of a constant return like `=> EventScope.Platform`. Returns null
        /// when no enum constant is found.
        /// </summary>
        private static string? GetEnumMemberName(IPropertySymbol prop, SemanticModel contextModel, CancellationToken ct)
        {
            var current = prop;
            while (current != null)
            {
                foreach (var syntaxRef in current.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxRef.GetSyntax(ct);
                    if (syntax is PropertyDeclarationSyntax propSyntax)
                    {
                        var model = ModelFor(syntax, contextModel);
                        ExpressionSyntax? expr = propSyntax.ExpressionBody?.Expression;
                        if (expr == null && propSyntax.AccessorList != null)
                        {
                            foreach (var accessor in propSyntax.AccessorList.Accessors)
                            {
                                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.ExpressionBody != null)
                                {
                                    expr = accessor.ExpressionBody.Expression;
                                    break;
                                }
                            }
                        }
                        if (expr is MemberAccessExpressionSyntax member)
                        {
                            var symbolInfo = model.GetSymbolInfo(member, ct);
                            if (symbolInfo.Symbol is IFieldSymbol field && field.ContainingType?.TypeKind == TypeKind.Enum)
                            {
                                return field.Name;
                            }
                        }
                    }
                }
                current = current.OverriddenProperty;
            }
            return null;
        }

        /// <summary>
        /// A semantic model for the tree a declaration lives in: an overridden
        /// property is often declared in another file than the event class.
        /// </summary>
        private static SemanticModel ModelFor(SyntaxNode syntax, SemanticModel contextModel) =>
            syntax.SyntaxTree == contextModel.SyntaxTree
                ? contextModel
                : contextModel.Compilation.GetSemanticModel(syntax.SyntaxTree);

        private static string? GetConstantReturnValue(IPropertySymbol prop, SemanticModel contextModel, CancellationToken ct)
        {
            var values = GetConstantReturnValues(prop, contextModel, ct);
            return values.Count > 0 ? values[0] : null;
        }

        /// <summary>
        /// The string constants a property can return, walking up its override chain to
        /// the first declaration that returns any: one for <c>=&gt; "a.b"</c> or a const
        /// field, every arm's for a switch or conditional expression.
        /// </summary>
        private static List<string> GetConstantReturnValues(IPropertySymbol prop, SemanticModel contextModel, CancellationToken ct)
        {
            var current = prop;
            while (current != null)
            {
                foreach (var syntaxRef in current.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxRef.GetSyntax(ct);
                    if (syntax is PropertyDeclarationSyntax propSyntax)
                    {
                        var model = ModelFor(syntax, contextModel);
                        var expr = propSyntax.ExpressionBody?.Expression;
                        if (expr == null && propSyntax.AccessorList != null)
                        {
                            foreach (var accessor in propSyntax.AccessorList.Accessors)
                            {
                                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.ExpressionBody != null)
                                {
                                    expr = accessor.ExpressionBody.Expression;
                                    break;
                                }
                            }
                        }

                        if (expr != null)
                        {
                            var values = new List<string>();
                            CollectStringConstants(expr, model, values, ct);
                            if (values.Count > 0) return values;
                        }
                    }
                }
                current = current.OverriddenProperty;
            }
            return new List<string>();
        }

        private static void CollectStringConstants(ExpressionSyntax expr, SemanticModel model, List<string> values, CancellationToken ct)
        {
            switch (expr)
            {
                case ParenthesizedExpressionSyntax paren:
                    CollectStringConstants(paren.Expression, model, values, ct);
                    return;
                case SwitchExpressionSyntax switchExpr:
                    foreach (var arm in switchExpr.Arms)
                        CollectStringConstants(arm.Expression, model, values, ct);
                    return;
                case ConditionalExpressionSyntax conditional:
                    CollectStringConstants(conditional.WhenTrue, model, values, ct);
                    CollectStringConstants(conditional.WhenFalse, model, values, ct);
                    return;
            }

            var constVal = model.GetConstantValue(expr, ct);
            if (constVal.HasValue && constVal.Value is string s)
            {
                if (!values.Contains(s)) values.Add(s);
                return;
            }

            var symbolInfo = model.GetSymbolInfo(expr, ct);
            if (symbolInfo.Symbol is IFieldSymbol field && field.HasConstantValue && field.ConstantValue is string cs && !values.Contains(cs))
                values.Add(cs);
        }

        #endregion

        #region Type Helpers

        private static bool DerivesFrom(INamedTypeSymbol symbol, string baseTypeName)
        {
            var current = symbol.BaseType;
            while (current != null)
            {
                if (current.ToDisplayString() == baseTypeName) return true;
                current = current.BaseType;
            }
            return false;
        }

        private static bool HasAttribute(IPropertySymbol prop, string attributeFullName)
        {
            foreach (var attr in prop.GetAttributes())
                if (attr.AttributeClass != null && attr.AttributeClass.ToDisplayString() == attributeFullName)
                    return true;
            if (prop.OverriddenProperty != null)
                return HasAttribute(prop.OverriddenProperty, attributeFullName);
            return false;
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol nullable &&
                nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                nullable.TypeArguments.Length > 0)
                return nullable.TypeArguments[0];
            return type;
        }

        private static string MapToSchemaType(ITypeSymbol type)
        {
            type = UnwrapNullable(type);
            switch (type.SpecialType)
            {
                case SpecialType.System_String: return "string";
                case SpecialType.System_Boolean: return "boolean";
                case SpecialType.System_Int16: case SpecialType.System_Int32: case SpecialType.System_Int64:
                case SpecialType.System_UInt16: case SpecialType.System_UInt32: case SpecialType.System_UInt64:
                case SpecialType.System_Single: case SpecialType.System_Double: case SpecialType.System_Decimal:
                case SpecialType.System_Byte: case SpecialType.System_SByte: return "number";
                case SpecialType.System_DateTime: return "datetime";
                default: return MapToSchemaTypeByName(type);
            }
        }

        private static string MapToSchemaTypeByName(ITypeSymbol type)
        {
            var name = type.ToDisplayString();
            if (name == "System.DateTime" || name == "System.DateTimeOffset" || name == "System.DateOnly") return "datetime";
            if (name == "System.TimeSpan" || name == "System.TimeOnly") return "string";
            if (name == "System.Guid" || name == "System.Uri") return "string";
            if (type is IArrayTypeSymbol) return "array";
            if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                var def = named.OriginalDefinition.ToDisplayString();
                if (def.StartsWith("System.Collections.Generic.IReadOnlyList") ||
                    def.StartsWith("System.Collections.Generic.IList") ||
                    def.StartsWith("System.Collections.Generic.List") ||
                    def.StartsWith("System.Collections.Generic.IEnumerable") ||
                    def.StartsWith("System.Collections.Generic.ICollection") ||
                    def.StartsWith("System.Collections.Generic.HashSet") ||
                    def.StartsWith("System.Collections.Immutable.ImmutableList") ||
                    def.StartsWith("System.Collections.Immutable.ImmutableArray"))
                    return "array";
                if (def.StartsWith("System.Collections.Generic.IDictionary") ||
                    def.StartsWith("System.Collections.Generic.Dictionary") ||
                    def.StartsWith("System.Collections.Generic.IReadOnlyDictionary"))
                    return "object";
            }
            if (type.TypeKind == TypeKind.Enum) return "string";
            return "object";
        }

        #endregion

        #region Code Generation

        private static string GetSourceClassName(string assemblyName)
        {
            var parts = assemblyName.Split('.');
            var sb = new StringBuilder();
            var startIndex = (parts.Length > 0 && parts[0] == "Oluso") ? 1 : 0;
            if (startIndex >= parts.Length) sb.Append("Core");
            else for (int i = startIndex; i < parts.Length; i++) sb.Append(parts[i]);
            sb.Append("EventPropertySource");
            return sb.ToString();
        }

        private static string GenerateSource(ImmutableArray<EventClassInfo> classes, AssemblyInfo asm, string className)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace Oluso.Core.Events");
            sb.AppendLine("{");

            // Class header — implements both interfaces
            sb.Append("    /// <summary>Auto-generated event property source and webhook provider for ").Append(asm.Name).AppendLine(".</summary>");
            sb.Append("    public sealed class ").Append(className).AppendLine(" : IEventPropertySource, IWebhookEventProvider");
            sb.AppendLine("    {");

            // --- IWebhookEventProvider ---
            sb.Append("        public string ProviderId => \"").Append(EscapeString(asm.ProviderId)).AppendLine("\";");
            sb.Append("        string IWebhookEventProvider.DisplayName => \"").Append(EscapeString(asm.ProviderDisplayName)).AppendLine("\";");
            sb.AppendLine();
            GenerateWebhookDefinitions(sb, classes, asm);
            sb.AppendLine();

            // --- GetEventProperties ---
            sb.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, EventPropertyDefinition[]> _byClass = new System.Collections.Generic.Dictionary<string, EventPropertyDefinition[]>(System.StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("        {");
            foreach (var cls in classes)
            {
                if (cls == null) continue;
                AppendPropertyEntry(sb, cls.ClassName, cls.Properties);
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // --- GetPropertiesByWebhookType ---
            sb.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, EventPropertyDefinition[]> _byWebhook = new System.Collections.Generic.Dictionary<string, EventPropertyDefinition[]>(System.StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("        {");
            foreach (var cls in classes)
            {
                if (cls == null) continue;
                foreach (var webhookEventType in cls.WebhookEventTypes)
                    AppendPropertyEntry(sb, webhookEventType, cls.Properties);
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // --- ResolveEventType ---
            sb.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Type> _types = new System.Collections.Generic.Dictionary<string, System.Type>(System.StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("        {");
            foreach (var cls in classes)
            {
                if (cls == null) continue;
                sb.Append("            [\"").Append(cls.ClassName).Append("\"] = typeof(").Append(cls.FullName).AppendLine("),");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // --- Interface methods ---
            sb.AppendLine("        public System.Collections.Generic.IReadOnlyDictionary<string, EventPropertyDefinition[]> GetEventProperties() => _byClass;");
            sb.AppendLine("        public System.Collections.Generic.IReadOnlyDictionary<string, EventPropertyDefinition[]> GetPropertiesByWebhookType() => _byWebhook;");
            sb.AppendLine("        public System.Type? ResolveEventType(string eventClassName) => _types.TryGetValue(eventClassName, out var t) ? t : null;");
            sb.AppendLine();

            // --- ExtractData ---
            GenerateExtractData(sb, classes);

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void GenerateExtractData(StringBuilder sb, ImmutableArray<EventClassInfo> classes)
        {
            sb.AppendLine("        public System.Collections.Generic.Dictionary<string, object?>? ExtractData(OlusoEvent evt)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (evt)");
            sb.AppendLine("            {");

            foreach (var cls in classes)
            {
                if (cls == null) continue;
                sb.Append("                case ").Append(cls.FullName).Append(" e: return new System.Collections.Generic.Dictionary<string, object?>(").Append(cls.Properties.Count).AppendLine(")");
                sb.AppendLine("                {");
                foreach (var prop in cls.Properties)
                {
                    sb.Append("                    [\"").Append(prop.SnakeName).Append("\"] = e.").Append(prop.AccessPath).AppendLine(",");
                }
                sb.AppendLine("                };");
            }

            sb.AppendLine("                default: return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
        }

        private static void GenerateWebhookDefinitions(StringBuilder sb, ImmutableArray<EventClassInfo> classes, AssemblyInfo asm)
        {
            // Filter to events with WebhookEventType
            var webhookEvents = classes.Where(c => c != null && c.WebhookEventTypes.Count > 0).ToList();
            if (webhookEvents.Count == 0)
            {
                sb.AppendLine("        public System.Collections.Generic.IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => System.Array.Empty<WebhookEventDefinition>();");
                return;
            }

            sb.AppendLine("        private static readonly WebhookEventDefinition[] _webhookDefs = new WebhookEventDefinition[]");
            sb.AppendLine("        {");
            foreach (var cls in webhookEvents)
            foreach (var webhookEventType in cls.WebhookEventTypes)
            {
                var displayName = cls.WebhookEventTypes.Count > 1
                    ? DeriveDisplayNameFromEventType(webhookEventType)
                    : string.IsNullOrEmpty(cls.WebhookDisplayName) ? DeriveEventDisplayName(cls.ClassName) : cls.WebhookDisplayName;
                var category = string.IsNullOrEmpty(cls.Category) ? "Other" : cls.Category;

                sb.AppendLine("            new WebhookEventDefinition");
                sb.AppendLine("            {");
                sb.Append("                EventType = \"").Append(EscapeString(webhookEventType)).AppendLine("\",");
                sb.Append("                Category = \"").Append(EscapeString(category)).AppendLine("\",");
                sb.Append("                DisplayName = \"").Append(EscapeString(displayName)).AppendLine("\",");
                if (!string.IsNullOrEmpty(cls.WebhookDescription))
                    sb.Append("                Description = \"").Append(EscapeString(cls.WebhookDescription)).AppendLine("\",");
                if (cls.WebhookEnabledByDefault)
                    sb.AppendLine("                EnabledByDefault = true,");
                sb.Append("                ProviderId = \"").Append(EscapeString(asm.ProviderId)).AppendLine("\",");
                if (!string.IsNullOrEmpty(cls.Scope) && cls.Scope != "Tenant")
                    sb.Append("                Scope = EventScope.").Append(cls.Scope).AppendLine(",");
                if (cls.Classification.Sensitivity != 0)
                    sb.Append("                Sensitivity = (DataSensitivity)").Append(cls.Classification.Sensitivity).AppendLine(",");
                if (!string.IsNullOrEmpty(cls.Classification.RequiredPermission))
                    sb.Append("                RequiredPermission = \"").Append(EscapeString(cls.Classification.RequiredPermission!)).AppendLine("\",");
                if (!string.IsNullOrEmpty(cls.Classification.Purpose))
                    sb.Append("                ClassificationPurpose = \"").Append(EscapeString(cls.Classification.Purpose!)).AppendLine("\",");

                // Inline the properties
                sb.AppendLine("                Properties = new EventPropertyDefinition[]");
                sb.AppendLine("                {");
                foreach (var prop in cls.Properties)
                {
                    sb.Append("                    new EventPropertyDefinition { Name = \"").Append(prop.SnakeName);
                    sb.Append("\", DisplayName = \"").Append(prop.DisplayName);
                    sb.Append("\", Type = \"").Append(prop.SchemaType).Append("\"");
                    if (prop.IsRequired) sb.Append(", Required = true");
                    if (prop.IsSensitive) sb.Append(", Sensitive = true");
                    sb.AppendLine(" },");
                }
                sb.AppendLine("                },");
                sb.AppendLine("            },");
            }
            sb.AppendLine("        };");
            sb.AppendLine("        public System.Collections.Generic.IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => _webhookDefs;");
        }

        /// <summary>
        /// "hr.offboarding.process_initiated" -> "Offboarding Process Initiated", for an
        /// event class that fires several types, where the class's own name fits only one.
        /// </summary>
        private static string DeriveDisplayNameFromEventType(string eventType)
        {
            var segments = eventType.Split('.');
            var words = segments.Skip(segments.Length > 1 ? 1 : 0)
                .SelectMany(segment => segment.Split('_'))
                .Where(word => word.Length > 0)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1));
            return string.Join(" ", words);
        }

        private static string DeriveEventDisplayName(string className)
        {
            // Strip "Event" suffix
            var name = className;
            if (name.EndsWith("Event")) name = name.Substring(0, name.Length - 5);
            return ToDisplayName(name);
        }

        private static string DeriveProviderId(string assemblyName)
        {
            var parts = assemblyName.Split('.');
            if (parts.Length <= 1) return assemblyName.ToLowerInvariant();
            // Second segment of the assembly name, e.g. Oluso.&lt;Module&gt; -> "&lt;module&gt;", Oluso.Core -> "core".
            var segment = parts.Length > 1 ? parts[1] : parts[0];
            return segment.ToLowerInvariant();
        }

        private static string DeriveProviderDisplayName(string assemblyName)
        {
            var id = DeriveProviderId(assemblyName);
            // Module display names come from each module's [WebhookEvent] provider
            // metadata; this is only a fallback for unannotated assemblies, so it
            // stays generic and names no specific module.
            return id switch
            {
                "core" => "Oluso Core",
                "enterprise" => "Enterprise",
                _ => ToDisplayName(id.Substring(0, 1).ToUpperInvariant() + id.Substring(1))
            };
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void AppendPropertyEntry(StringBuilder sb, string key, List<FlatProperty> properties)
        {
            sb.Append("            [\"").Append(key).AppendLine("\"] = new EventPropertyDefinition[]");
            sb.AppendLine("            {");
            foreach (var prop in properties)
            {
                sb.Append("                new EventPropertyDefinition { Name = \"").Append(prop.SnakeName);
                sb.Append("\", DisplayName = \"").Append(prop.DisplayName);
                sb.Append("\", Type = \"").Append(prop.SchemaType).Append("\"");
                if (prop.IsRequired) sb.Append(", Required = true");
                if (prop.IsSensitive) sb.Append(", Sensitive = true");
                sb.AppendLine(" },");
            }
            sb.AppendLine("            },");
        }

        private static string GenerateStaticRegistry(ImmutableArray<EventClassInfo> classes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("namespace Oluso.Core.Events");
            sb.AppendLine("{");
            sb.AppendLine("    public static partial class EventPropertyRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, EventPropertyDefinition[]> _coreProperties = new System.Collections.Generic.Dictionary<string, EventPropertyDefinition[]>(System.StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("        {");
            foreach (var cls in classes)
            {
                if (cls == null) continue;
                AppendPropertyEntry(sb, cls.ClassName, cls.Properties);
            }
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        public static EventPropertyDefinition[] GetProperties(string eventClassName) => _coreProperties.TryGetValue(eventClassName, out var p) ? p : System.Array.Empty<EventPropertyDefinition>();");
            sb.AppendLine("        public static EventPropertyDefinition[] GetProperties<T>() where T : OlusoEvent => GetProperties(typeof(T).Name);");
            sb.AppendLine("        public static EventPropertyDefinition[] GetProperties(System.Type eventType) => GetProperties(eventType.Name);");
            sb.AppendLine("        public static System.Collections.Generic.IReadOnlyCollection<string> GetRegisteredEventTypes() => _coreProperties.Keys;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        #endregion

        #region String Helpers

        private static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        var prev = name[i - 1];
                        if (!char.IsUpper(prev)) sb.Append('_');
                        else if (i + 1 < name.Length && char.IsLower(name[i + 1])) sb.Append('_');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ToDisplayName(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c) && i > 0)
                {
                    var prev = name[i - 1];
                    if (!char.IsUpper(prev)) sb.Append(' ');
                    else if (i + 1 < name.Length && char.IsLower(name[i + 1])) sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        #endregion

        #region Models

        private sealed class AssemblyInfo
        {
            public string Name { get; }
            public string ProviderId { get; }
            public string ProviderDisplayName { get; }

            public AssemblyInfo(string name, string providerId, string providerDisplayName)
            {
                Name = name;
                ProviderId = providerId;
                ProviderDisplayName = providerDisplayName;
            }
        }

        private sealed class EventClassInfo
        {
            public string ClassName { get; }
            public string FullName { get; }
            public List<string> WebhookEventTypes { get; }
            public string Category { get; }
            public string WebhookDisplayName { get; }
            public string WebhookDescription { get; }
            public bool WebhookEnabledByDefault { get; }
            public string Scope { get; }
            public List<FlatProperty> Properties { get; }
            public ClassificationInfo Classification { get; }

            public EventClassInfo(string className, string fullName, List<string> webhookEventTypes, string category,
                string webhookDisplayName, string webhookDescription, bool webhookEnabledByDefault,
                string scope, List<FlatProperty> properties, ClassificationInfo classification)
            {
                Classification = classification;
                ClassName = className;
                FullName = fullName;
                WebhookEventTypes = webhookEventTypes;
                Category = category;
                WebhookDisplayName = webhookDisplayName;
                WebhookDescription = webhookDescription;
                WebhookEnabledByDefault = webhookEnabledByDefault;
                Scope = scope;
                Properties = properties;
            }
        }

        private sealed class FlatProperty
        {
            public string OriginalName { get; }
            public string SnakeName { get; }
            public string DisplayName { get; }
            public string SchemaType { get; }
            public bool IsRequired { get; }
            public bool IsNullable { get; }

            /// <summary>
            /// C# accessor path from the event variable (e.g., "Email" or "ShippingAddress?.Street")
            /// </summary>
            public string AccessPath { get; }

            public bool IsSensitive { get; }

            public FlatProperty(string originalName, string snakeName, string displayName, string schemaType, bool isRequired, bool isNullable, string accessPath, bool isSensitive)
            {
                IsSensitive = isSensitive;
                OriginalName = originalName;
                SnakeName = snakeName;
                DisplayName = displayName;
                SchemaType = schemaType;
                IsRequired = isRequired;
                IsNullable = isNullable;
                AccessPath = accessPath;
            }
        }

        private sealed class ClassificationInfo
        {
            public int Sensitivity { get; }
            public string? RequiredPermission { get; }
            public string? Purpose { get; }

            public ClassificationInfo(int sensitivity, string? requiredPermission, string? purpose)
            {
                Sensitivity = sensitivity;
                RequiredPermission = requiredPermission;
                Purpose = purpose;
            }
        }

        #endregion
    }
}
