namespace Oluso.Core.Conditions;

/// <summary>
/// Tiny boolean expression engine shared across modules. Today: approval
/// workflow conditions. Designed so feature-flag segment rules and
/// automation triggers can plug into the same grammar later — keeps a
/// single mental model for "conditions" across the platform.
///
/// <para>Grammar (case-insensitive keywords):</para>
/// <code>
///   expr        = or
///   or          = and ( "OR" and )*
///   and         = unary ( "AND" unary )*
///   unary       = "NOT" unary | primary
///   primary     = "(" expr ")" | comparison
///   comparison  = identifier op value | identifier ( "in" | "!in" ) list
///   op          = "==" | "=" | "!=" | "&lt;" | "&lt;=" | "&gt;" | "&gt;=" | "contains" | "!contains"
///   list        = "(" value ( "," value )* ")" | "[" value ( "," value )* "]"
///   value       = "'..."'" | '"..."' | number | bool | identifier
/// </code>
///
/// <para>Identifiers are property names looked up in the supplied data
/// dictionary. Missing properties evaluate to <c>false</c> for the leaf
/// comparison (the caller can opt into a warning hook to surface typos).</para>
///
/// <para><c>in</c> tests set membership. A property holding several values
/// (a comma-separated string such as <c>RequesterRoles</c>, or any
/// enumerable) matches when any of its values is in the list:
/// <c>RequesterRoles in ('HRAdmin', 'ITAdmin')</c>.</para>
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>
    /// True when the expression evaluates true for the supplied data.
    /// Errors (malformed, missing property) return <c>false</c> and call
    /// <paramref name="onMissing"/> when set — caller decides whether to
    /// log/warn. Treating errors as <c>false</c> matches the existing
    /// approval semantics ("if the condition can't be evaluated, the rule
    /// doesn't match").
    /// </summary>
    bool Evaluate(
        string expression,
        IReadOnlyDictionary<string, object?> data,
        Action<string>? onMissing = null);

    /// <summary>
    /// Returns every property name referenced as a comparison's left-hand
    /// identifier. Used by save-time validators to check "are all referenced
    /// properties declared for this entity type?"
    /// </summary>
    IReadOnlyList<string> ExtractReferencedProperties(string expression);
}
