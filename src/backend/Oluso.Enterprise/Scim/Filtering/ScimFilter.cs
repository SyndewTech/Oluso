using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Oluso.Enterprise.Scim.Filtering;

/// <summary>
/// Thrown when a SCIM filter expression cannot be parsed or references an
/// attribute that is not filterable. Surfaced by callers as a SCIM
/// <c>invalidFilter</c> error (HTTP 400).
/// </summary>
public sealed class ScimFilterException : Exception
{
    public ScimFilterException(string message) : base(message) { }
}

/// <summary>
/// Parsed SCIM filter expression node (RFC 7644 §3.4.2.2).
/// </summary>
public abstract class ScimFilterNode { }

/// <summary>attrPath op [value] (e.g. <c>userName eq "x"</c>, <c>title pr</c>)</summary>
public sealed class ScimComparisonNode : ScimFilterNode
{
    public required string Attribute { get; init; }
    public required string Operator { get; init; }

    /// <summary>Literal value (already unquoted), or null for <c>pr</c> / the <c>null</c> literal.</summary>
    public string? Value { get; init; }

    /// <summary>True when the value was an explicit <c>null</c> literal (distinguishes from <c>pr</c>).</summary>
    public bool ValueIsNullLiteral { get; init; }
}

/// <summary>and / or</summary>
public sealed class ScimLogicalNode : ScimFilterNode
{
    public required string Operator { get; init; }
    public required ScimFilterNode Left { get; init; }
    public required ScimFilterNode Right { get; init; }
}

/// <summary>not ( ... )</summary>
public sealed class ScimNotNode : ScimFilterNode
{
    public required ScimFilterNode Operand { get; init; }
}

/// <summary>
/// Recursive-descent parser for the SCIM filter grammar. Produces a
/// <see cref="ScimFilterNode"/> tree that <see cref="ScimFilterTranslator{T}"/>
/// turns into an EF-translatable predicate.
/// </summary>
public static class ScimFilterParser
{
    public static ScimFilterNode Parse(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            throw new ScimFilterException("Filter is empty");

        var tokens = Tokenize(filter);
        var parser = new Parser(tokens);
        var node = parser.ParseFilter();
        parser.ExpectEnd();
        return node;
    }

    private enum TokenKind { Word, String, LParen, RParen, LBracket, RBracket, End }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '(': tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue;
                case '[': tokens.Add(new Token(TokenKind.LBracket, "[")); i++; continue;
                case ']': tokens.Add(new Token(TokenKind.RBracket, "]")); i++; continue;
            }

            if (c == '"')
            {
                var sb = new StringBuilder();
                i++; // opening quote
                var closed = false;
                while (i < input.Length)
                {
                    var ch = input[i];
                    if (ch == '\\' && i + 1 < input.Length)
                    {
                        var next = input[i + 1];
                        sb.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => next });
                        i += 2;
                        continue;
                    }
                    if (ch == '"') { closed = true; i++; break; }
                    sb.Append(ch);
                    i++;
                }
                if (!closed)
                    throw new ScimFilterException("Unterminated string literal in filter");
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            // Word: attribute path / operator / keyword / unquoted literal.
            var start = i;
            while (i < input.Length)
            {
                var ch = input[i];
                if (char.IsWhiteSpace(ch) || ch is '(' or ')' or '[' or ']' or '"')
                    break;
                i++;
            }
            tokens.Add(new Token(TokenKind.Word, input[start..i]));
        }

        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private static readonly HashSet<string> ComparisonOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "ne", "co", "sw", "ew", "gt", "ge", "lt", "le"
    };

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        public Parser(List<Token> tokens) => _tokens = tokens;

        private Token Peek => _tokens[_pos];
        private Token Next() => _tokens[_pos++];

        public void ExpectEnd()
        {
            if (Peek.Kind != TokenKind.End)
                throw new ScimFilterException($"Unexpected token '{Peek.Text}' in filter");
        }

        public ScimFilterNode ParseFilter() => ParseOr();

        private ScimFilterNode ParseOr()
        {
            var left = ParseAnd();
            while (IsKeyword("or"))
            {
                Next();
                var right = ParseAnd();
                left = new ScimLogicalNode { Operator = "or", Left = left, Right = right };
            }
            return left;
        }

        private ScimFilterNode ParseAnd()
        {
            var left = ParseNot();
            while (IsKeyword("and"))
            {
                Next();
                var right = ParseNot();
                left = new ScimLogicalNode { Operator = "and", Left = left, Right = right };
            }
            return left;
        }

        private ScimFilterNode ParseNot()
        {
            if (IsKeyword("not"))
            {
                Next();
                if (Peek.Kind != TokenKind.LParen)
                    throw new ScimFilterException("Expected '(' after 'not'");
                Next();
                var operand = ParseFilter();
                Expect(TokenKind.RParen, ")");
                return new ScimNotNode { Operand = operand };
            }
            return ParsePrimary();
        }

        private ScimFilterNode ParsePrimary()
        {
            if (Peek.Kind == TokenKind.LParen)
            {
                Next();
                var node = ParseFilter();
                Expect(TokenKind.RParen, ")");
                return node;
            }

            if (Peek.Kind != TokenKind.Word)
                throw new ScimFilterException($"Expected attribute, found '{Peek.Text}'");

            var attribute = Next().Text;

            // Value-path: attr[ subFilter ]  (e.g. emails[type eq "work"])
            if (Peek.Kind == TokenKind.LBracket)
            {
                Next();
                var sub = ParseFilter();
                Expect(TokenKind.RBracket, "]");
                return Rescope(attribute, sub);
            }

            if (Peek.Kind != TokenKind.Word)
                throw new ScimFilterException($"Expected operator after '{attribute}'");

            var op = Next().Text.ToLowerInvariant();

            if (op == "pr")
                return new ScimComparisonNode { Attribute = attribute, Operator = "pr" };

            if (!ComparisonOps.Contains(op))
                throw new ScimFilterException($"Unsupported filter operator '{op}'");

            var valueToken = Next();
            if (valueToken.Kind == TokenKind.String)
                return new ScimComparisonNode { Attribute = attribute, Operator = op, Value = valueToken.Text };

            if (valueToken.Kind == TokenKind.Word)
            {
                if (string.Equals(valueToken.Text, "null", StringComparison.OrdinalIgnoreCase))
                    return new ScimComparisonNode { Attribute = attribute, Operator = op, ValueIsNullLiteral = true };
                return new ScimComparisonNode { Attribute = attribute, Operator = op, Value = valueToken.Text };
            }

            throw new ScimFilterException($"Expected value after operator '{op}'");
        }

        private bool IsKeyword(string kw) =>
            Peek.Kind == TokenKind.Word && string.Equals(Peek.Text, kw, StringComparison.OrdinalIgnoreCase);

        private void Expect(TokenKind kind, string text)
        {
            if (Peek.Kind != kind)
                throw new ScimFilterException($"Expected '{text}' in filter");
            Next();
        }

        // Prefixes every attribute inside a value-path sub-filter with the
        // outer attribute so emails[type eq "work"] becomes emails.type eq "work".
        private static ScimFilterNode Rescope(string prefix, ScimFilterNode node) => node switch
        {
            ScimComparisonNode c => new ScimComparisonNode
            {
                Attribute = $"{prefix}.{c.Attribute}",
                Operator = c.Operator,
                Value = c.Value,
                ValueIsNullLiteral = c.ValueIsNullLiteral
            },
            ScimLogicalNode l => new ScimLogicalNode
            {
                Operator = l.Operator,
                Left = Rescope(prefix, l.Left),
                Right = Rescope(prefix, l.Right)
            },
            ScimNotNode n => new ScimNotNode { Operand = Rescope(prefix, n.Operand) },
            _ => node
        };
    }
}

/// <summary>
/// Translates a parsed SCIM filter into an <see cref="Expression{TDelegate}"/>
/// predicate over an entity type, using a caller-supplied map from SCIM
/// attribute paths to entity members.
/// </summary>
public sealed class ScimFilterTranslator<T>
{
    private readonly Dictionary<string, MappedAttribute> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ParameterExpression _param = Expression.Parameter(typeof(T), "x");

    private sealed record MappedAttribute(Expression Body, Type ClrType);

    /// <summary>Map a SCIM string attribute (and any aliases) to an entity selector.</summary>
    public ScimFilterTranslator<T> MapString(Expression<Func<T, string?>> selector, params string[] scimAttributes)
    {
        var body = Rebind(selector);
        foreach (var attr in scimAttributes)
            _attributes[attr] = new MappedAttribute(body, typeof(string));
        return this;
    }

    /// <summary>Map a SCIM boolean attribute (and any aliases) to an entity selector.</summary>
    public ScimFilterTranslator<T> MapBool(Expression<Func<T, bool>> selector, params string[] scimAttributes)
    {
        var body = Rebind(selector);
        foreach (var attr in scimAttributes)
            _attributes[attr] = new MappedAttribute(body, typeof(bool));
        return this;
    }

    public Expression<Func<T, bool>> Translate(ScimFilterNode node)
    {
        var body = Build(node);
        return Expression.Lambda<Func<T, bool>>(body, _param);
    }

    private Expression Build(ScimFilterNode node) => node switch
    {
        ScimLogicalNode l when l.Operator == "and" => Expression.AndAlso(Build(l.Left), Build(l.Right)),
        ScimLogicalNode l when l.Operator == "or" => Expression.OrElse(Build(l.Left), Build(l.Right)),
        ScimNotNode n => Expression.Not(Build(n.Operand)),
        ScimComparisonNode c => BuildComparison(c),
        _ => throw new ScimFilterException("Malformed filter expression")
    };

    private Expression BuildComparison(ScimComparisonNode c)
    {
        if (!_attributes.TryGetValue(c.Attribute, out var mapped))
            throw new ScimFilterException($"Attribute '{c.Attribute}' is not filterable");

        return mapped.ClrType == typeof(bool)
            ? BuildBoolComparison(mapped.Body, c)
            : BuildStringComparison(mapped.Body, c);
    }

    private static Expression BuildBoolComparison(Expression left, ScimComparisonNode c)
    {
        if (c.Operator == "pr")
            return Expression.Constant(true);

        if (c.Operator is not ("eq" or "ne"))
            throw new ScimFilterException($"Operator '{c.Operator}' is not valid for a boolean attribute");

        if (!bool.TryParse(c.Value, out var b))
            throw new ScimFilterException($"Invalid boolean value '{c.Value}'");

        var constant = Expression.Constant(b);
        return c.Operator == "eq" ? Expression.Equal(left, constant) : Expression.NotEqual(left, constant);
    }

    private static readonly MethodInfo ToLower =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo Contains =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
    private static readonly MethodInfo StartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;
    private static readonly MethodInfo EndsWith =
        typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;
    private static readonly MethodInfo Compare =
        typeof(string).GetMethod(nameof(string.Compare), new[] { typeof(string), typeof(string) })!;

    private static Expression BuildStringComparison(Expression left, ScimComparisonNode c)
    {
        if (c.Operator == "pr")
            return Expression.AndAlso(
                Expression.NotEqual(left, Expression.Constant(null, typeof(string))),
                Expression.NotEqual(left, Expression.Constant(string.Empty)));

        // Comparison against the null literal: userName eq null / ne null.
        if (c.ValueIsNullLiteral)
        {
            var nullConst = Expression.Constant(null, typeof(string));
            return c.Operator switch
            {
                "eq" => Expression.Equal(left, nullConst),
                "ne" => Expression.NotEqual(left, nullConst),
                _ => throw new ScimFilterException($"Operator '{c.Operator}' cannot be used with null")
            };
        }

        var value = c.Value ?? string.Empty;
        var lowerValue = Expression.Constant(value.ToLowerInvariant());
        var leftLower = Expression.Call(left, ToLower);
        var notNull = Expression.NotEqual(left, Expression.Constant(null, typeof(string)));

        switch (c.Operator)
        {
            case "eq":
                return Expression.AndAlso(notNull, Expression.Equal(leftLower, lowerValue));
            case "ne":
                return Expression.OrElse(
                    Expression.Equal(left, Expression.Constant(null, typeof(string))),
                    Expression.NotEqual(leftLower, lowerValue));
            case "co":
                return Expression.AndAlso(notNull, Expression.Call(leftLower, Contains, lowerValue));
            case "sw":
                return Expression.AndAlso(notNull, Expression.Call(leftLower, StartsWith, lowerValue));
            case "ew":
                return Expression.AndAlso(notNull, Expression.Call(leftLower, EndsWith, lowerValue));
            case "gt":
            case "ge":
            case "lt":
            case "le":
                var cmp = Expression.Call(Compare, left, Expression.Constant(value));
                var zero = Expression.Constant(0);
                return c.Operator switch
                {
                    "gt" => Expression.GreaterThan(cmp, zero),
                    "ge" => Expression.GreaterThanOrEqual(cmp, zero),
                    "lt" => Expression.LessThan(cmp, zero),
                    _ => Expression.LessThanOrEqual(cmp, zero)
                };
            default:
                throw new ScimFilterException($"Unsupported filter operator '{c.Operator}'");
        }
    }

    private Expression Rebind(LambdaExpression selector) =>
        new ParameterReplacer(selector.Parameters[0], _param).Visit(selector.Body)!;

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }
}
