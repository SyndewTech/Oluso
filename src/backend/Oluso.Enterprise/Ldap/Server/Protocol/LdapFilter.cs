using System.Formats.Asn1;
using System.Text.RegularExpressions;

namespace Oluso.Enterprise.Ldap.Server.Protocol;

/// <summary>
/// LDAP search filter (RFC 4515)
/// </summary>
public abstract class LdapFilter
{
    public abstract bool Matches(Dictionary<string, List<string>> attributes);

    public static LdapFilter? Decode(AsnReader reader)
    {
        var tag = reader.PeekTag();

        return tag.TagValue switch
        {
            0 => DecodeAnd(reader), // AND
            1 => DecodeOr(reader),  // OR
            2 => DecodeNot(reader), // NOT
            3 => DecodeEqualityMatch(reader),
            4 => DecodeSubstrings(reader),
            5 => DecodeGreaterOrEqual(reader),
            6 => DecodeLessOrEqual(reader),
            7 => DecodePresent(reader),
            8 => DecodeApproxMatch(reader),
            9 => DecodeExtensibleMatch(reader),
            _ => null
        };
    }

    private static LdapFilter DecodeAnd(AsnReader reader)
    {
        var filters = new List<LdapFilter>();
        var setReader = reader.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        while (setReader.HasData)
        {
            var filter = Decode(setReader);
            if (filter != null)
                filters.Add(filter);
        }
        return new AndFilter { Filters = filters };
    }

    private static LdapFilter DecodeOr(AsnReader reader)
    {
        var filters = new List<LdapFilter>();
        var setReader = reader.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 1));
        while (setReader.HasData)
        {
            var filter = Decode(setReader);
            if (filter != null)
                filters.Add(filter);
        }
        return new OrFilter { Filters = filters };
    }

    private static LdapFilter DecodeNot(AsnReader reader)
    {
        var sequence = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
        var inner = Decode(sequence);
        return new NotFilter { Filter = inner ?? new PresentFilter { AttributeDescription = "" } };
    }

    private static LdapFilter DecodeEqualityMatch(AsnReader reader)
    {
        var sequence = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 3));
        var attr = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        var value = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        return new EqualityFilter { AttributeDescription = attr, AssertionValue = value };
    }

    private static LdapFilter DecodeSubstrings(AsnReader reader)
    {
        var sequence = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 4));
        var attr = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        var substrings = sequence.ReadSequence();

        string? initial = null;
        var any = new List<string>();
        string? final = null;

        while (substrings.HasData)
        {
            var tag = substrings.PeekTag();
            var value = System.Text.Encoding.UTF8.GetString(substrings.ReadOctetString(tag));

            switch (tag.TagValue)
            {
                case 0: initial = value; break;
                case 1: any.Add(value); break;
                case 2: final = value; break;
            }
        }

        return new SubstringFilter
        {
            AttributeDescription = attr,
            Initial = initial,
            Any = any,
            Final = final
        };
    }

    private static LdapFilter DecodeGreaterOrEqual(AsnReader reader)
    {
        var sequence = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 5));
        var attr = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        var value = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        return new GreaterOrEqualFilter { AttributeDescription = attr, AssertionValue = value };
    }

    private static LdapFilter DecodeLessOrEqual(AsnReader reader)
    {
        var sequence = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 6));
        var attr = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        var value = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        return new LessOrEqualFilter { AttributeDescription = attr, AssertionValue = value };
    }

    private static LdapFilter DecodePresent(AsnReader reader)
    {
        var attr = System.Text.Encoding.UTF8.GetString(reader.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7)));
        return new PresentFilter { AttributeDescription = attr };
    }

    private static LdapFilter DecodeApproxMatch(AsnReader reader)
    {
        var sequence = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 8));
        var attr = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        var value = System.Text.Encoding.UTF8.GetString(sequence.ReadOctetString());
        return new ApproxMatchFilter { AttributeDescription = attr, AssertionValue = value };
    }

    private static LdapFilter DecodeExtensibleMatch(AsnReader reader)
    {
        // Simplified - just return a present filter for objectClass
        reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 9));
        return new PresentFilter { AttributeDescription = "objectClass" };
    }
}

public class AndFilter : LdapFilter
{
    public List<LdapFilter> Filters { get; set; } = new();

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        return Filters.All(f => f.Matches(attributes));
    }
}

public class OrFilter : LdapFilter
{
    public List<LdapFilter> Filters { get; set; } = new();

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        return Filters.Any(f => f.Matches(attributes));
    }
}

public class NotFilter : LdapFilter
{
    public LdapFilter Filter { get; set; } = null!;

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        return !Filter.Matches(attributes);
    }
}

public class EqualityFilter : LdapFilter
{
    public string AttributeDescription { get; set; } = string.Empty;
    public string AssertionValue { get; set; } = string.Empty;

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        if (!attributes.TryGetValue(AttributeDescription.ToLowerInvariant(), out var values))
            return false;

        return values.Any(v => v.Equals(AssertionValue, StringComparison.OrdinalIgnoreCase));
    }
}

public class SubstringFilter : LdapFilter
{
    public string AttributeDescription { get; set; } = string.Empty;
    public string? Initial { get; set; }
    public List<string> Any { get; set; } = new();
    public string? Final { get; set; }

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        if (!attributes.TryGetValue(AttributeDescription.ToLowerInvariant(), out var values))
            return false;

        foreach (var value in values)
        {
            var match = true;
            var remaining = value;

            if (Initial != null)
            {
                if (!remaining.StartsWith(Initial, StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    continue;
                }
                remaining = remaining[Initial.Length..];
            }

            foreach (var any in Any)
            {
                var idx = remaining.IndexOf(any, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    match = false;
                    break;
                }
                remaining = remaining[(idx + any.Length)..];
            }

            if (match && Final != null)
            {
                if (!remaining.EndsWith(Final, StringComparison.OrdinalIgnoreCase))
                    match = false;
            }

            if (match)
                return true;
        }

        return false;
    }
}

public class GreaterOrEqualFilter : LdapFilter
{
    public string AttributeDescription { get; set; } = string.Empty;
    public string AssertionValue { get; set; } = string.Empty;

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        if (!attributes.TryGetValue(AttributeDescription.ToLowerInvariant(), out var values))
            return false;

        return values.Any(v => string.Compare(v, AssertionValue, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

public class LessOrEqualFilter : LdapFilter
{
    public string AttributeDescription { get; set; } = string.Empty;
    public string AssertionValue { get; set; } = string.Empty;

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        if (!attributes.TryGetValue(AttributeDescription.ToLowerInvariant(), out var values))
            return false;

        return values.Any(v => string.Compare(v, AssertionValue, StringComparison.OrdinalIgnoreCase) <= 0);
    }
}

public class PresentFilter : LdapFilter
{
    public string AttributeDescription { get; set; } = string.Empty;

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        // objectClass is always present
        if (AttributeDescription.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
            return true;

        return attributes.ContainsKey(AttributeDescription.ToLowerInvariant()) &&
               attributes[AttributeDescription.ToLowerInvariant()].Any();
    }
}

public class ApproxMatchFilter : LdapFilter
{
    public string AttributeDescription { get; set; } = string.Empty;
    public string AssertionValue { get; set; } = string.Empty;

    public override bool Matches(Dictionary<string, List<string>> attributes)
    {
        // Simplified - treat as equality for now
        if (!attributes.TryGetValue(AttributeDescription.ToLowerInvariant(), out var values))
            return false;

        return values.Any(v => v.Contains(AssertionValue, StringComparison.OrdinalIgnoreCase));
    }
}
