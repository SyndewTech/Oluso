using System.Text;

namespace Oluso.Core.Conditions;

/// <summary>
/// Recursive-descent implementation of <see cref="IExpressionEvaluator"/>.
/// Hand-rolled rather than using a library because (a) the grammar is tiny
/// and (b) we want zero new dependencies in <c>Oluso.Core</c>. Tokeniser
/// preserves quoted strings (single or double), treats <c>AND</c>/<c>OR</c>/<c>NOT</c>
/// as keywords case-insensitively, and accepts the same comparison
/// operators the existing approval condition parser handled (==, !=, &lt;,
/// &lt;=, &gt;, &gt;=, contains, !contains) plus <c>in</c> / <c>!in</c>
/// against a parenthesised or bracketed list, parens and boolean combinators.
///
/// <para>Backwards-compatible with the prior <c>"prop op value"</c>
/// single-expression shape — those parse as a single comparison with no
/// AND/OR wrappers and produce identical results.</para>
///
/// <para>Set membership: <c>Role in ('HRAdmin', 'ITAdmin')</c>. When the
/// property holds several values (a comma-separated string such as
/// <c>RequesterRoles</c>, or any enumerable) the test passes if any of them
/// is in the list. <c>!in</c> is the negation.</para>
/// </summary>
public class DefaultExpressionEvaluator : IExpressionEvaluator
{
    public bool Evaluate(
        string expression,
        IReadOnlyDictionary<string, object?> data,
        Action<string>? onMissing = null)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        try
        {
            var tokens = Tokenise(expression);
            var parser = new Parser(tokens);
            var ast = parser.ParseExpr();
            if (!parser.IsAtEnd)
            {
                // Trailing tokens after a valid expression — likely a
                // user typo (e.g. unbalanced parens). Treat as malformed.
                return false;
            }
            return ast.Evaluate(data, onMissing);
        }
        catch
        {
            // Malformed expression — match existing approval semantics
            // (fail closed rather than surface a 500 to the runtime).
            return false;
        }
    }

    public IReadOnlyList<string> ExtractReferencedProperties(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return Array.Empty<string>();

        try
        {
            var tokens = Tokenise(expression);
            var parser = new Parser(tokens);
            var ast = parser.ParseExpr();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ast.CollectIdentifiers(seen);
            return seen.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ── Tokeniser ───────────────────────────────────────────────────────

    private enum TokenKind { Identifier, Operator, Value, LParen, RParen, LBracket, RBracket, Comma, And, Or, Not }

    private record Token(TokenKind Kind, string Lexeme);

    private static readonly string[] MultiCharOps = ["==", "!=", "<=", ">=", "!contains", "!in"];

    private static IReadOnlyList<Token> Tokenise(string input)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < input.Length)
        {
            var c = input[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue; }
            if (c == '[') { tokens.Add(new Token(TokenKind.LBracket, "[")); i++; continue; }
            if (c == ']') { tokens.Add(new Token(TokenKind.RBracket, "]")); i++; continue; }
            if (c == ',') { tokens.Add(new Token(TokenKind.Comma, ",")); i++; continue; }

            // Quoted string value — keep verbatim, drop quotes at evaluation.
            if (c == '\'' || c == '"')
            {
                var quote = c;
                var end = input.IndexOf(quote, i + 1);
                if (end < 0) throw new FormatException("Unterminated string literal");
                tokens.Add(new Token(TokenKind.Value, input.Substring(i + 1, end - i - 1)));
                i = end + 1;
                continue;
            }

            // Multi-char operators take precedence (==, !=, <=, >=, !contains, !in).
            var matched = false;
            foreach (var op in MultiCharOps)
            {
                if (i + op.Length <= input.Length &&
                    string.CompareOrdinal(input, i, op, 0, op.Length) == 0 &&
                    (op is not "!in" || i + op.Length >= input.Length || !char.IsLetterOrDigit(input[i + op.Length])))
                {
                    tokens.Add(new Token(TokenKind.Operator, op));
                    i += op.Length;
                    matched = true;
                    break;
                }
            }
            if (matched) continue;

            // Single-char operators.
            if (c == '=' || c == '<' || c == '>')
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString()));
                i++;
                continue;
            }

            // Identifier-or-keyword run. Property names may contain dots
            // (e.g. Requester.DepartmentId) so the rune set is alphanumeric
            // + dot + underscore — keep this in sync with what the data
            // dictionary uses for keys.
            if (char.IsLetter(c) || c == '_')
            {
                var sb = new StringBuilder();
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_' || input[i] == '.'))
                {
                    sb.Append(input[i]);
                    i++;
                }
                var word = sb.ToString();
                // Keywords are case-insensitive so "and" / "AND" / "And"
                // are all valid. Anything else is an identifier or a bare
                // value literal (true/false/null handled at eval time).
                tokens.Add(word.ToUpperInvariant() switch
                {
                    "AND" => new Token(TokenKind.And, word),
                    "OR" => new Token(TokenKind.Or, word),
                    "NOT" => new Token(TokenKind.Not, word),
                    "CONTAINS" => new Token(TokenKind.Operator, "contains"),
                    "IN" => new Token(TokenKind.Operator, "in"),
                    _ => new Token(TokenKind.Identifier, word),
                });
                continue;
            }

            // Numeric literal — leading digit or minus-with-digit. We don't
            // need a sophisticated parser; the comparison evaluator does
            // decimal.TryParse on string values anyway.
            if (char.IsDigit(c) || (c == '-' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                var sb = new StringBuilder();
                if (c == '-') { sb.Append('-'); i++; }
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
                {
                    sb.Append(input[i]);
                    i++;
                }
                tokens.Add(new Token(TokenKind.Value, sb.ToString()));
                continue;
            }

            throw new FormatException($"Unexpected character '{c}' at position {i}");
        }
        return tokens;
    }

    // ── AST + Parser ────────────────────────────────────────────────────

    private abstract record Node
    {
        public abstract bool Evaluate(IReadOnlyDictionary<string, object?> data, Action<string>? onMissing);
        public abstract void CollectIdentifiers(HashSet<string> sink);
    }

    private record AndNode(Node Left, Node Right) : Node
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> data, Action<string>? onMissing)
            => Left.Evaluate(data, onMissing) && Right.Evaluate(data, onMissing);
        public override void CollectIdentifiers(HashSet<string> sink)
        {
            Left.CollectIdentifiers(sink); Right.CollectIdentifiers(sink);
        }
    }

    private record OrNode(Node Left, Node Right) : Node
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> data, Action<string>? onMissing)
            => Left.Evaluate(data, onMissing) || Right.Evaluate(data, onMissing);
        public override void CollectIdentifiers(HashSet<string> sink)
        {
            Left.CollectIdentifiers(sink); Right.CollectIdentifiers(sink);
        }
    }

    private record NotNode(Node Inner) : Node
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> data, Action<string>? onMissing)
            => !Inner.Evaluate(data, onMissing);
        public override void CollectIdentifiers(HashSet<string> sink) => Inner.CollectIdentifiers(sink);
    }

    private record ComparisonNode(string Property, string Op, string Value) : Node
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> data, Action<string>? onMissing)
        {
            if (!data.TryGetValue(Property, out var raw))
            {
                onMissing?.Invoke(Property);
                return false;
            }
            return Compare(raw, Op, Value);
        }
        public override void CollectIdentifiers(HashSet<string> sink) => sink.Add(Property);
    }

    private record MembershipNode(string Property, bool Negated, IReadOnlyList<string> Values) : Node
    {
        public override bool Evaluate(IReadOnlyDictionary<string, object?> data, Action<string>? onMissing)
        {
            if (!data.TryGetValue(Property, out var raw))
            {
                onMissing?.Invoke(Property);
                return false;
            }

            var found = ConditionOperators.MembershipValues(raw).Any(a => Values.Any(v => string.Equals(a, v, StringComparison.OrdinalIgnoreCase)));
            return Negated ? !found : found;
        }

        public override void CollectIdentifiers(HashSet<string> sink) => sink.Add(Property);
    }

    private class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _pos;
        public Parser(IReadOnlyList<Token> tokens) { _tokens = tokens; }
        public bool IsAtEnd => _pos >= _tokens.Count;
        private Token Peek() => _tokens[_pos];
        private Token Advance() => _tokens[_pos++];
        private bool Match(TokenKind kind)
        {
            if (IsAtEnd || _tokens[_pos].Kind != kind) return false;
            _pos++;
            return true;
        }

        public Node ParseExpr() => ParseOr();

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (!IsAtEnd && Peek().Kind == TokenKind.Or)
            {
                Advance();
                left = new OrNode(left, ParseAnd());
            }
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseUnary();
            while (!IsAtEnd && Peek().Kind == TokenKind.And)
            {
                Advance();
                left = new AndNode(left, ParseUnary());
            }
            return left;
        }

        private Node ParseUnary()
        {
            if (Match(TokenKind.Not)) return new NotNode(ParseUnary());
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            if (Match(TokenKind.LParen))
            {
                var inner = ParseOr();
                if (!Match(TokenKind.RParen)) throw new FormatException("Expected ')'");
                return inner;
            }
            return ParseComparison();
        }

        private Node ParseComparison()
        {
            var idTok = Advance();
            if (idTok.Kind != TokenKind.Identifier)
                throw new FormatException($"Expected property name, got '{idTok.Lexeme}'");

            if (IsAtEnd) throw new FormatException("Expected operator after property name");
            var opTok = Advance();
            if (opTok.Kind != TokenKind.Operator)
                throw new FormatException($"Expected operator, got '{opTok.Lexeme}'");

            if (opTok.Lexeme is "in" or "!in")
                return new MembershipNode(idTok.Lexeme, opTok.Lexeme == "!in", ParseList());

            if (IsAtEnd) throw new FormatException("Expected value after operator");
            var valTok = Advance();
            if (valTok.Kind != TokenKind.Value && valTok.Kind != TokenKind.Identifier)
                throw new FormatException($"Expected value, got '{valTok.Lexeme}'");

            return new ComparisonNode(idTok.Lexeme, opTok.Lexeme, valTok.Lexeme);
        }

        /// <summary>
        /// <c>('a', 'b')</c> or <c>['a', 'b']</c>; bare words are accepted as
        /// values so <c>in (Active, Pending)</c> also works. At least one value.
        /// </summary>
        private IReadOnlyList<string> ParseList()
        {
            if (IsAtEnd) throw new FormatException("Expected a list after 'in'");
            var open = Advance();
            var close = open.Kind switch
            {
                TokenKind.LParen => TokenKind.RParen,
                TokenKind.LBracket => TokenKind.RBracket,
                _ => throw new FormatException($"Expected '(' or '[' after 'in', got '{open.Lexeme}'"),
            };

            var values = new List<string>();
            while (true)
            {
                if (IsAtEnd) throw new FormatException("Unterminated list after 'in'");
                var tok = Advance();
                if (tok.Kind != TokenKind.Value && tok.Kind != TokenKind.Identifier)
                    throw new FormatException($"Expected list value, got '{tok.Lexeme}'");
                values.Add(tok.Lexeme);

                if (Match(TokenKind.Comma)) continue;
                if (Match(close)) break;
                throw new FormatException("Expected ',' or closing bracket in list");
            }

            return values;
        }
    }

    // ── Comparison primitives (compatible with the prior EvaluateCondition) ─

    private static bool Compare(object? actual, string op, string expected) =>
        ConditionOperators.Compare(actual?.ToString() ?? "", op, expected);
}
